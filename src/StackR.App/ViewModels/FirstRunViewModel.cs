using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security;
using System.Security.Cryptography;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using StackR.App.Resources;
using StackR.App.Services;
using StackR.Core;
using StackR.Core.Components;
using StackR.Core.Download;
using StackR.Core.Manifests;
using StackR.Core.Paths;
using StackR.Core.Services;
using StackR.Core.Settings;

namespace StackR.App.ViewModels;

/// <summary>
/// Assistant de premier lancement : dossiers → vérifications → composants → téléchargement vérifié → HTTPS
/// → terminal → résumé. Le moteur n'existe pas avant la fin de l'étape « Composants » : il est créé par
/// <c>startEngine</c> (fourni par l'application) une fois la racine choisie et enregistrée, car sa création
/// construit l'arborescence de la racine.
/// </summary>
public sealed partial class FirstRunViewModel : ObservableObject
{
    public const int FoldersStep = 0;
    public const int ChecksStep = 1;
    public const int ComponentsStep = 2;
    public const int InstallStep = 3;
    public const int HttpsStep = 4;
    public const int TerminalStep = 5;
    public const int SummaryStep = 6;

    private const string LaragonWww = @"C:\laragon\www";

    private static readonly ComponentKind[] ComponentOrder =
        [ComponentKind.Apache, ComponentKind.Php, ComponentKind.MySql, ComponentKind.Mailpit, ComponentKind.Node, ComponentKind.Composer];

    private readonly Func<StackRSettings, Task<StackREngine>> _startEngine;
    private readonly StackRSettings _initialSettings;
    private readonly LocationRules _rules = LocationRules.Default;
    private StackREngine? _engine;
    private CancellationTokenSource? _installCts;
    private Task? _installTask;
    private bool _wwwFollowsRoot;
    private bool _updatingWww;
    private bool _installStarted;

    public FirstRunViewModel(StackRSettings settings, bool settingsWereReset, Manifest manifest, Func<StackRSettings, Task<StackREngine>> startEngine)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(manifest);
        _initialSettings = settings;
        _startEngine = startEngine ?? throw new ArgumentNullException(nameof(startEngine));
        SettingsWereReset = settingsWereReset;

        Steps =
        [
            new(FoldersStep, Strings.FrStepFolders),
            new(ChecksStep, Strings.FrStepChecks),
            new(ComponentsStep, Strings.FrStepComponents),
            new(InstallStep, Strings.FrStepInstall),
            new(HttpsStep, Strings.FrStepHttps),
            new(TerminalStep, Strings.FrStepTerminal),
            new(SummaryStep, Strings.FrStepSummary),
        ];

        // Dossiers : valeurs enregistrées (C:\StackR par défaut) ; www vide = <racine>\www, qui suit la racine.
        _rootPath = settings.Root;
        _wwwFollowsRoot = string.IsNullOrWhiteSpace(settings.WwwPath);
        _wwwPath = _wwwFollowsRoot ? DefaultWwwFor(settings.Root) : settings.WwwPath;
        LaragonWwwAvailable = SafeDirectoryExists(LaragonWww);
        _useLaragonWww = LaragonWwwAvailable && string.Equals(_wwwPath, LaragonWww, StringComparison.OrdinalIgnoreCase);

        foreach (var kind in ComponentOrder)
        {
            if (manifest.Recommended(kind) is { } release)
            {
                var choice = new ComponentChoice(release, ComponentName(kind), ComponentDescription(kind));
                choice.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(ComponentChoice.IsSelected))
                    {
                        OnSelectionChanged();
                    }
                };
                Components.Add(choice);
            }
        }

        UpdateStepItems();
        ValidateFoldersLive();
        OnSelectionChanged();
    }

    /// <summary>Levé quand l'assistant est terminé (paramètres enregistrés) : la fenêtre se ferme.</summary>
    public event EventHandler? CloseRequested;

    public ObservableCollection<FirstRunStepItem> Steps { get; }

    /// <summary>Vrai une fois « Terminer » réussi : la fermeture de la fenêtre n'est plus une annulation.</summary>
    public bool IsCompleted { get; private set; }

    public bool SettingsWereReset { get; }

    // ===== Navigation =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StepTitle), nameof(StepSubtitle), nameof(IsLastStep), nameof(StepCounter))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand), nameof(FinishCommand))]
    private int _stepIndex;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand), nameof(FinishCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private string? _busyText;

    public string StepTitle => Steps[StepIndex].Title;

    public string StepSubtitle => StepIndex switch
    {
        FoldersStep => Strings.FrFoldersSubtitle,
        ChecksStep => Strings.FrChecksSubtitle,
        ComponentsStep => Strings.FrComponentsSubtitle,
        InstallStep => Strings.FrInstallSubtitle,
        HttpsStep => Strings.FrHttpsSubtitle,
        TerminalStep => Strings.FrTerminalSubtitle,
        _ => Strings.FrSummarySubtitle,
    };

    public string StepCounter => FirstRunFormat.Format(Strings.FrStepCounter, StepIndex + 1, Steps.Count);

    public bool IsLastStep => StepIndex == SummaryStep;

    private bool CanGoNext() => !IsBusy && !IsInstalling && StepIndex < SummaryStep;

    // Après création du moteur, la racine est fixée : on ne revient plus avant l'installation.
    private bool CanGoBack() => !IsBusy && !IsInstalling && StepIndex > (_engine is null ? FoldersStep : InstallStep);

    [RelayCommand(CanExecute = nameof(CanGoNext))]
    private async Task NextAsync()
    {
        switch (StepIndex)
        {
            case FoldersStep:
                if (await ValidateFoldersAsync())
                {
                    GoTo(ChecksStep);
                    await RefreshChecksAsync();
                }

                break;

            case ComponentsStep:
                if (await EnsureEngineAsync())
                {
                    GoTo(InstallStep);
                    if (!_installStarted)
                    {
                        _installStarted = true;
                        BuildInstallItems();
                        await RunInstallsAsync(InstallItems);
                    }
                }

                break;

            case InstallStep:
                GoTo(HttpsStep);
                await RefreshTrustAsync();
                break;

            case HttpsStep:
                GoTo(TerminalStep);
                if (!PathAdded)
                {
                    await AddToPathAsync();
                }

                break;

            case TerminalStep:
                PrepareSummary();
                GoTo(SummaryStep);
                break;

            default:
                GoTo(StepIndex + 1);
                break;
        }
    }

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Back() => GoTo(StepIndex - 1);

    private void GoTo(int step) => StepIndex = Math.Clamp(step, FoldersStep, SummaryStep);

    partial void OnStepIndexChanged(int value) => UpdateStepItems();

    private void UpdateStepItems()
    {
        foreach (var item in Steps)
        {
            item.IsCurrent = item.Index == StepIndex;
            item.IsDone = item.Index < StepIndex;
        }
    }

    private void RefreshCommands()
    {
        NextCommand.NotifyCanExecuteChanged();
        BackCommand.NotifyCanExecuteChanged();
        FinishCommand.NotifyCanExecuteChanged();
    }

    // ===== Étape 1 : dossiers =====

    [ObservableProperty]
    private string _rootPath;

    [ObservableProperty]
    private string _wwwPath;

    [ObservableProperty]
    private bool _useLaragonWww;

    [ObservableProperty]
    private string? _rootError;

    [ObservableProperty]
    private string? _wwwError;

    public bool LaragonWwwAvailable { get; }

    public bool IsWwwEditable => !UseLaragonWww;

    partial void OnRootPathChanged(string value)
    {
        if (_wwwFollowsRoot && !UseLaragonWww)
        {
            SetWwwSilently(DefaultWwwFor(value));
        }

        ValidateFoldersLive();
    }

    partial void OnWwwPathChanged(string value)
    {
        if (!_updatingWww && !UseLaragonWww)
        {
            // Saisie manuelle : le dossier www ne suit plus la racine, sauf s'il redevient <racine>\www.
            _wwwFollowsRoot = string.Equals(value?.Trim(), DefaultWwwFor(RootPath), StringComparison.OrdinalIgnoreCase);
        }

        ValidateFoldersLive();
    }

    partial void OnUseLaragonWwwChanged(bool value)
    {
        OnPropertyChanged(nameof(IsWwwEditable));
        SetWwwSilently(value ? LaragonWww : DefaultWwwFor(RootPath));
        if (!value)
        {
            _wwwFollowsRoot = true;
        }

        ValidateFoldersLive();
    }

    [RelayCommand]
    private void BrowseRoot()
    {
        var dialog = new OpenFolderDialog { Title = Strings.FrBrowseRootTitle, Multiselect = false };
        if (InitialFolder(RootPath) is { } initial)
        {
            dialog.InitialDirectory = initial;
        }

        if (dialog.ShowDialog() == true)
        {
            var selected = dialog.FolderName;
            // Racine d'un lecteur : on propose un sous-dossier StackR plutôt que d'écrire à la racine.
            RootPath = Path.GetPathRoot(selected) is { } driveRoot && string.Equals(Path.TrimEndingDirectorySeparator(selected), Path.TrimEndingDirectorySeparator(driveRoot), StringComparison.OrdinalIgnoreCase)
                ? Path.Combine(driveRoot, "StackR")
                : selected;
        }
    }

    [RelayCommand]
    private void BrowseWww()
    {
        var dialog = new OpenFolderDialog { Title = Strings.FrBrowseWwwTitle, Multiselect = false };
        if (InitialFolder(WwwPath) is { } initial)
        {
            dialog.InitialDirectory = initial;
        }

        if (dialog.ShowDialog() == true)
        {
            UseLaragonWww = false;
            WwwPath = dialog.FolderName;
        }
    }

    private void SetWwwSilently(string value)
    {
        _updatingWww = true;
        try
        {
            WwwPath = value;
        }
        finally
        {
            _updatingWww = false;
        }
    }

    // Contrôle immédiat, sans accès disque (aucun dossier créé pendant la saisie).
    private void ValidateFoldersLive()
    {
        var root = InstallLocationValidator.ValidateRoot(RootPath, _rules, checkAccess: false);
        RootError = root.IsValid ? null : DescribeLocationProblem(root);
        WwwError = root.IsValid
            ? Describe(InstallLocationValidator.ValidateWww(WwwPath, root.FullPath!, _rules, checkAccess: false))
            : null;

        static string? Describe(LocationCheck check) => check.IsValid ? null : DescribeLocationProblem(check);
    }

    private async Task<bool> ValidateFoldersAsync()
    {
        IsBusy = true;
        BusyText = Strings.FrBusyCheckingFolders;
        try
        {
            var rootInput = RootPath;
            var wwwInput = WwwPath;
            var (root, www) = await Task.Run(() =>
            {
                var r = InstallLocationValidator.ValidateRoot(rootInput, _rules, checkAccess: true);
                var w = r.IsValid ? InstallLocationValidator.ValidateWww(wwwInput, r.FullPath!, _rules, checkAccess: true) : null;
                return (r, w);
            });

            RootError = root.IsValid ? null : DescribeLocationProblem(root);
            WwwError = www is null || www.IsValid ? null : DescribeLocationProblem(www);
            if (!root.IsValid || www is null || !www.IsValid)
            {
                return false;
            }

            // Chemins normalisés affichés tels qu'ils seront enregistrés.
            RootPath = root.FullPath!;
            WwwPath = www.FullPath!;

            return true;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    private static string DescribeLocationProblem(LocationCheck check)
    {
        var message = check.Problem switch
        {
            LocationProblem.Empty => Strings.FrFolderEmpty,
            LocationProblem.NotAbsolute => Strings.FrFolderNotAbsolute,
            LocationProblem.NetworkPath => Strings.FrFolderNetwork,
            LocationProblem.InvalidCharacters => Strings.FrFolderInvalidChars,
            LocationProblem.DriveRoot => Strings.FrFolderDriveRoot,
            LocationProblem.ProtectedFolder => Strings.FrFolderProtected,
            LocationProblem.LaragonFolder => Strings.FrFolderLaragon,
            LocationProblem.IsFile => Strings.FrFolderIsFile,
            LocationProblem.NotWritable => Strings.FrFolderNotWritable,
            LocationProblem.NotReadable => Strings.FrFolderNotReadable,
            LocationProblem.WwwIsRoot => Strings.FrWwwIsRoot,
            LocationProblem.WwwContainsRoot => Strings.FrWwwContainsRoot,
            LocationProblem.WwwInsideInternalFolder => Strings.FrWwwInternal,
            _ => Strings.FrFolderInvalidChars,
        };

        return string.IsNullOrWhiteSpace(check.Detail) ? message : message + " (" + check.Detail + ")";
    }

    // ===== Étape 2 : vérifications =====

    [ObservableProperty]
    private bool _isChecking;

    [ObservableProperty]
    private bool _laragonRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVcWarning))]
    private bool _vcRedistMissing;

    [ObservableProperty]
    private bool _anyPortBusy;

    [ObservableProperty]
    private bool _allChecksPassed;

    public ObservableCollection<PortCheckItem> Ports { get; } = [];

    [RelayCommand]
    private async Task RefreshChecksAsync()
    {
        if (IsChecking)
        {
            return;
        }

        IsChecking = true;
        try
        {
            var s = _initialSettings;
            (int Port, string Usage)[] wanted =
            [
                (s.HttpPort, Strings.FrPortHttp),
                (s.HttpsPort, Strings.FrPortHttps),
                (s.MySqlPort, Strings.FrPortMySql),
                (s.MailpitUiPort, Strings.FrPortMailpitUi),
                (s.MailpitSmtpPort, Strings.FrPortMailpitSmtp),
            ];

            var (laragon, vc, owners) = await Task.Run(() =>
            {
                var l = SafeCheck(Prerequisites.IsLaragonRunning);
                var v = Prerequisites.HasVcRedistX64();
                var o = wanted.Select(w => (w.Port, w.Usage, Owner: SafeFindOwner(w.Port))).ToList();
                return (l, v, o);
            });

            LaragonRunning = laragon;
            VcRedistMissing = !vc;
            Ports.Clear();
            foreach (var (port, usage, owner) in owners)
            {
                Ports.Add(new PortCheckItem(
                    port,
                    usage,
                    owner is null,
                    owner is null ? Strings.FrPortFree : FirstRunFormat.Format(Strings.FrPortBusy, owner.ProcessName, owner.Pid)));
            }

            AnyPortBusy = Ports.Any(p => !p.IsFree);
            AllChecksPassed = !LaragonRunning && !VcRedistMissing && !AnyPortBusy;
        }
        finally
        {
            IsChecking = false;
        }
    }

    [RelayCommand]
    private static void OpenVcRedist() => OpenInBrowser(Prerequisites.VcRedistUrl.ToString());

    private static bool SafeCheck(Func<bool> check)
    {
        try
        {
            return check();
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static PortOwner? SafeFindOwner(int port)
    {
        try
        {
            return PortProbe.FindOwner(port);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // ===== Étape 3 : composants =====

    public ObservableCollection<ComponentChoice> Components { get; } = [];

    [ObservableProperty]
    private string _totalSizeText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowVcWarning))]
    private bool _needsVcRedist;

    [ObservableProperty]
    private bool _composerWithoutPhp;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasEngineError))]
    private string? _engineError;

    public bool HasEngineError => !string.IsNullOrEmpty(EngineError);

    public bool ShowVcWarning => VcRedistMissing && NeedsVcRedist;

    private void OnSelectionChanged()
    {
        var selected = Components.Where(c => c.IsSelected).ToList();
        TotalSizeText = FirstRunFormat.Format(Strings.FrComponentsTotal, selected.Count, FirstRunFormat.Size(selected.Sum(c => c.Release.SizeBytes)));
        NeedsVcRedist = selected.Any(c => c.Kind is ComponentKind.Apache or ComponentKind.Php);
        ComposerWithoutPhp = selected.Any(c => c.Kind == ComponentKind.Composer) && selected.All(c => c.Kind != ComponentKind.Php);
    }

    /// <summary>Enregistre racine et www puis crée le moteur (une seule fois).</summary>
    private async Task<bool> EnsureEngineAsync()
    {
        if (_engine is not null)
        {
            return true;
        }

        IsBusy = true;
        BusyText = Strings.FrBusyPreparing;
        EngineError = null;
        try
        {
            var root = Path.TrimEndingDirectorySeparator(RootPath);
            var www = Path.TrimEndingDirectorySeparator(WwwPath);
            var isDefaultWww = string.Equals(www, DefaultWwwFor(root), StringComparison.OrdinalIgnoreCase);
            if (!isDefaultWww && !UseLaragonWww)
            {
                // Dossier www personnalisé : le moteur ne crée que <racine>\www, on crée celui-ci.
                await Task.Run(() => Directory.CreateDirectory(www));
            }

            var settings = _initialSettings with { Root = root, WwwPath = isDefaultWww ? "" : www };
            _engine = await _startEngine(settings);
            RefreshCommands();
            return true;
        }
#pragma warning disable CA1031 // Frontière de l'interface : toute erreur de préparation est affichée.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashLog.Write("Assistant : préparation du moteur", ex);
            EngineError = FirstRunFormat.Format(Strings.FrEngineError, ex.Message);
            return false;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    // ===== Étape 4 : téléchargement vérifié =====

    public ObservableCollection<InstallItem> InstallItems { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(NextCommand), nameof(BackCommand), nameof(CancelInstallCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private string? _installSummary;

    [ObservableProperty]
    private bool _installHasProblems;

    [ObservableProperty]
    private bool _installAllDone;

    public bool NothingToInstall => InstallItems.Count == 0;

    private void BuildInstallItems()
    {
        InstallItems.Clear();
        foreach (var choice in Components.Where(c => c.IsSelected))
        {
            InstallItems.Add(new InstallItem(choice));
        }

        OnPropertyChanged(nameof(NothingToInstall));
        UpdateInstallSummary();
    }

    private bool CanCancelInstall() => IsInstalling;

    [RelayCommand(CanExecute = nameof(CanCancelInstall))]
    private void CancelInstall() => _installCts?.Cancel();

    [RelayCommand]
    private async Task RetryInstallAsync(InstallItem? item)
    {
        if (item is null || IsInstalling || !item.CanRetry)
        {
            return;
        }

        await RunInstallsAsync([item]);
    }

    [RelayCommand]
    private async Task RetryAllAsync()
    {
        if (IsInstalling)
        {
            return;
        }

        await RunInstallsAsync([.. InstallItems.Where(i => i.CanRetry)]);
    }

    /// <summary>Installe les éléments un par un ; une erreur n'arrête pas les suivants, l'annulation si.</summary>
    private async Task RunInstallsAsync(IEnumerable<InstallItem> batch)
    {
        var items = batch.ToList();
        if (_engine is null || items.Count == 0 || IsInstalling)
        {
            UpdateInstallSummary();
            return;
        }

        using var cts = new CancellationTokenSource();
        _installCts = cts;
        IsInstalling = true;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _installTask = completion.Task;
        try
        {
            foreach (var item in items)
            {
                item.State = InstallItemState.Pending;
                item.ErrorText = null;
                item.Percent = 0;
                item.StageText = Strings.FrInstallPending;
            }

            foreach (var item in items)
            {
                if (cts.IsCancellationRequested)
                {
                    MarkCancelled(item);
                    continue;
                }

                item.State = InstallItemState.Running;
                item.StageText = Strings.FrStageDownload;
                try
                {
                    await _engine.InstallWithoutApplyAsync(item.Release, new InstallItemProgress(item), cts.Token);
                    item.Percent = 100;
                    item.StageText = Strings.FrStageDone;
                    item.State = InstallItemState.Done;
                }
                catch (OperationCanceledException) when (cts.IsCancellationRequested)
                {
                    MarkCancelled(item);
                }
#pragma warning disable CA1031 // Une erreur sur un composant est affichée ; les suivants sont quand même installés.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    CrashLog.Write($"Assistant : installation de {item.Title}", ex);
                    item.State = InstallItemState.Failed;
                    item.StageText = Strings.FrInstallFailed;
                    item.ErrorText = DescribeInstallError(ex);
                }
            }
        }
        finally
        {
            _installCts = null;
            IsInstalling = false;
            completion.TrySetResult();
            UpdateInstallSummary();
        }
    }

    private static void MarkCancelled(InstallItem item)
    {
        item.State = InstallItemState.Cancelled;
        item.StageText = Strings.FrInstallCancelled;
        item.Percent = 0;
    }

    private void UpdateInstallSummary()
    {
        var failed = InstallItems.Count(i => i.State is InstallItemState.Failed or InstallItemState.Cancelled);
        InstallAllDone = InstallItems.Count > 0 && InstallItems.All(i => i.IsDone);
        InstallHasProblems = !IsInstalling && failed > 0;
        InstallSummary = IsInstalling || InstallItems.Count == 0
            ? null
            : InstallAllDone
                ? Strings.FrInstallAllDone
                : FirstRunFormat.Format(Strings.FrInstallSomeFailed, failed);
    }

    private static string DescribeInstallError(Exception ex) => ex switch
    {
        IntegrityException => FirstRunFormat.Format(Strings.FrErrorIntegrity, ex.Message),
        HttpRequestException => FirstRunFormat.Format(Strings.FrErrorNetwork, ex.Message),
        TimeoutException => FirstRunFormat.Format(Strings.FrErrorTimeout, ex.Message),
        TaskCanceledException => FirstRunFormat.Format(Strings.FrErrorTimeout, ex.Message),
        UnauthorizedAccessException or IOException => FirstRunFormat.Format(Strings.FrErrorDisk, ex.Message),
        _ => FirstRunFormat.Format(Strings.FrErrorOther, ex.Message),
    };

    /// <summary>Annule l'installation en cours et attend sa fin (fichiers temporaires nettoyés par l'installateur).</summary>
    public async Task CancelAndWaitAsync()
    {
        _installCts?.Cancel();
        if (_installTask is { } task)
        {
            await task;
        }
    }

    /// <summary>
    /// Relaye l'avancement sur le thread de l'interface, en limitant la fréquence (un rapport par point de
    /// pourcentage) pour ne pas saturer le répartiteur pendant le téléchargement.
    /// </summary>
    private sealed class InstallItemProgress(InstallItem item) : IProgress<InstallProgress>
    {
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private string? _lastStage;
        private int _lastPercent = -1;

        public void Report(InstallProgress value)
        {
            var percent = (int)Math.Clamp(value.Percent, 0, 100);
            if (value.Stage == _lastStage && percent == _lastPercent)
            {
                return;
            }

            _lastStage = value.Stage;
            _lastPercent = percent;
            _ = _dispatcher.InvokeAsync(() =>
            {
                if (item.State != InstallItemState.Running)
                {
                    return;
                }

                item.StageText = value.Stage switch
                {
                    "Téléchargement" => FirstRunFormat.Format(Strings.FrStageDownloadPercent, percent),
                    "Vérification" => Strings.FrStageVerify,
                    "Extraction" => Strings.FrStageExtract,
                    "Terminé" => Strings.FrStageDone,
                    _ => value.Stage,
                };
                item.Percent = value.Stage == "Téléchargement" ? percent * 0.9 : value.Stage == "Terminé" ? 100 : 95;
            });
        }
    }

    // ===== Étape 5 : HTTPS =====

    [ObservableProperty]
    private bool _caTrusted;

    [ObservableProperty]
    private bool _caDeclined;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCaError))]
    private string? _caError;

    public bool HasCaError => !string.IsNullOrEmpty(CaError);

    private async Task RefreshTrustAsync()
    {
        if (_engine is not { } engine)
        {
            return;
        }

        try
        {
            CaTrusted = await Task.Run(engine.Ca.IsTrusted);
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            CaTrusted = false;
        }
    }

    private bool CanTrust() => !IsBusy && !CaTrusted;

    [RelayCommand(CanExecute = nameof(CanTrust))]
    private async Task TrustAsync()
    {
        if (_engine is not { } engine)
        {
            return;
        }

        IsBusy = true;
        BusyText = Strings.FrBusyTrust;
        CaDeclined = false;
        CaError = null;
        try
        {
            // Laisse l'interface afficher l'attente : Windows ouvre ensuite sa propre fenêtre de confirmation,
            // qui doit appartenir au thread de l'interface pour s'afficher au premier plan.
            await Dispatcher.Yield(DispatcherPriority.Background);
            engine.TrustCertificateAuthority();
            CaTrusted = true;
        }
        catch (CryptographicException)
        {
            // Refus dans la fenêtre de Windows : on continue, HTTPS affichera des alertes.
            CaDeclined = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or TimeoutException)
        {
            CaError = FirstRunFormat.Format(Strings.FrCaError, ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
            TrustCommand.NotifyCanExecuteChanged();
        }
    }

    partial void OnCaTrustedChanged(bool value) => TrustCommand.NotifyCanExecuteChanged();

    partial void OnIsBusyChanged(bool value) => TrustCommand.NotifyCanExecuteChanged();

    // ===== Étape 6 : terminal =====

    [ObservableProperty]
    private bool _pathAdded;

    [ObservableProperty]
    private string? _pathError;

    public string ShimsFolder => _engine?.Paths.Shims ?? Path.Combine(RootPath, "bin", "shims");

    [RelayCommand]
    private async Task AddToPathAsync()
    {
        if (_engine is not { } engine)
        {
            return;
        }

        IsBusy = true;
        BusyText = Strings.FrBusyPath;
        PathError = null;
        OnPropertyChanged(nameof(ShimsFolder));
        try
        {
            await Task.Run(engine.Shell.EnsureOnUserPath);
            PathAdded = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            PathError = FirstRunFormat.Format(Strings.FrPathError, ex.Message);
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }
    }

    // ===== Étape 7 : résumé =====

    public ObservableCollection<string> InstalledSummary { get; } = [];

    public ObservableCollection<string> MissingSummary { get; } = [];

    [ObservableProperty]
    private bool _startServicesNow;

    [ObservableProperty]
    private bool _canStartServices;

    [ObservableProperty]
    private string _exampleText = "";

    [ObservableProperty]
    private string _exampleUrl = "";

    [ObservableProperty]
    private string _httpsSummary = "";

    [ObservableProperty]
    private string _pathSummary = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFinishProblems))]
    private string? _finishProblems;

    public bool HasFinishProblems => !string.IsNullOrEmpty(FinishProblems);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FinishButtonText))]
    private bool _isFinalized;

    public string FinishButtonText => IsFinalized ? Strings.FrOpenStackR : Strings.FrFinish;

    public bool HasMissing => MissingSummary.Count > 0;

    private void PrepareSummary()
    {
        InstalledSummary.Clear();
        MissingSummary.Clear();
        foreach (var item in InstallItems)
        {
            (item.IsDone ? InstalledSummary : MissingSummary).Add(item.Title);
        }

        OnPropertyChanged(nameof(HasMissing));

        var www = WwwPath;
        ExampleUrl = (CaTrusted ? "https" : "http") + "://monsite.test";
        ExampleText = FirstRunFormat.Format(Strings.FrSummaryExample, Path.Combine(www, "monsite"));
        HttpsSummary = CaTrusted ? Strings.FrSummaryHttpsTrusted : Strings.FrSummaryHttpsNotTrusted;
        PathSummary = PathAdded ? Strings.FrSummaryPathAdded : Strings.FrSummaryPathMissing;

        var services = InstallItems.Where(i => i.IsDone).Select(i => i.Release.Component).ToHashSet();
        CanStartServices = (services.Contains(ComponentKind.Apache) && services.Contains(ComponentKind.Php))
            || services.Contains(ComponentKind.MySql) || services.Contains(ComponentKind.Mailpit)
            || (_engine is not null && HasActiveService(_engine));
        StartServicesNow = CanStartServices;
    }

    private static bool HasActiveService(StackREngine engine) =>
        engine.ActiveDirectory(ComponentKind.MySql) is not null
        || engine.ActiveDirectory(ComponentKind.Mailpit) is not null
        || (engine.ActiveDirectory(ComponentKind.Apache) is not null && engine.ActiveDirectory(ComponentKind.Php) is not null);

    private bool CanFinish() => !IsBusy && StepIndex == SummaryStep;

    [RelayCommand(CanExecute = nameof(CanFinish))]
    private async Task FinishAsync()
    {
        if (IsFinalized || _engine is not { } engine)
        {
            Complete();
            return;
        }

        IsBusy = true;
        FinishProblems = null;
        var problems = new List<string>();
        try
        {
            BusyText = Strings.FrBusySaving;
            engine.UpdateSettings(s => s with { FirstRunCompleted = true });
            IsCompleted = true;

            BusyText = Strings.FrBusyApplying;
            try
            {
                var result = await Task.Run(() => engine.ApplyConfigurationAsync());
                problems.AddRange(result.Warnings);
            }
#pragma warning disable CA1031 // La configuration sera réappliquée depuis la fenêtre principale : on signale sans bloquer.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                CrashLog.Write("Assistant : application de la configuration", ex);
                problems.Add(FirstRunFormat.Format(Strings.FrApplyError, ex.Message));
            }

            if (StartServicesNow && CanStartServices)
            {
                BusyText = Strings.FrBusyStarting;
                try
                {
                    var report = await Task.Run(() => engine.StartAllAsync());
                    problems.AddRange(report.Errors);
                }
#pragma warning disable CA1031 // Idem : les services pourront être démarrés depuis le tableau de bord.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    CrashLog.Write("Assistant : démarrage des services", ex);
                    problems.Add(FirstRunFormat.Format(Strings.FrStartError, ex.Message));
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            // Paramètres non enregistrés : l'assistant reste ouvert pour réessayer.
            CrashLog.Write("Assistant : enregistrement des paramètres", ex);
            FinishProblems = FirstRunFormat.Format(Strings.FrSaveError, ex.Message);
            return;
        }
        finally
        {
            IsBusy = false;
            BusyText = null;
        }

        if (problems.Count == 0)
        {
            Complete();
            return;
        }

        FinishProblems = string.Join(Environment.NewLine, problems.Select(p => "• " + p));
        IsFinalized = true;
    }

    private void Complete()
    {
        IsCompleted = true;
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    // ===== Utilitaires =====

    private static string DefaultWwwFor(string? root)
    {
        var trimmed = (root ?? "").Trim().Trim('"');
        return trimmed.Length == 0 ? "" : Path.Combine(Path.TrimEndingDirectorySeparator(trimmed), "www");
    }

    private static string? InitialFolder(string? path)
    {
        var check = InstallLocationValidator.Normalize(path);
        for (var dir = check.FullPath; dir is not null; dir = Path.GetDirectoryName(dir))
        {
            if (SafeDirectoryExists(dir))
            {
                return dir;
            }
        }

        return null;
    }

    private static bool SafeDirectoryExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return false;
        }
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            CrashLog.Write("Assistant : ouverture du navigateur", ex);
        }
    }

    private static string ComponentName(ComponentKind kind) => kind switch
    {
        ComponentKind.Apache => "Apache",
        ComponentKind.Php => "PHP",
        ComponentKind.MySql => "MySQL",
        ComponentKind.Mailpit => "Mailpit",
        ComponentKind.Node => "Node.js",
        ComponentKind.Composer => "Composer",
        _ => kind.ToString(),
    };

    private static string ComponentDescription(ComponentKind kind) => kind switch
    {
        ComponentKind.Apache => Strings.FrCompApache,
        ComponentKind.Php => Strings.FrCompPhp,
        ComponentKind.MySql => Strings.FrCompMySql,
        ComponentKind.Mailpit => Strings.FrCompMailpit,
        ComponentKind.Node => Strings.FrCompNode,
        ComponentKind.Composer => Strings.FrCompComposer,
        _ => "",
    };
}
