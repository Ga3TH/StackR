using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using StackR.App.Services;
using StackR.Core;
using StackR.Core.Manifests;
using StackR.Core.Settings;
using StackR.Core.Sites;
using Wpf.Ui.Controls;

namespace StackR.App.ViewModels;

/// <summary>Création d'un site : nom validé en direct, modèle Vide ou Laravel (Composer), configuration appliquée ensuite.</summary>
public sealed partial class NewSiteViewModel : ObservableObject
{
    private const int MaxOutputChars = 40_000;

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly StackREngine _engine;
    private readonly EngineBridge _bridge;
    private readonly string _www;
    private readonly HashSet<string> _existingHosts;
    private readonly StringBuilder _output = new();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private string _name = "";

    [ObservableProperty]
    private bool _isLaravel;

    [ObservableProperty]
    private string? _nameError;

    [ObservableProperty]
    private string? _urlPreview;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CreateCommand))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isDone;

    [ObservableProperty]
    private string _log = "";

    [ObservableProperty]
    private string? _resultMessage;

    [ObservableProperty]
    private InfoBarSeverity _resultSeverity = InfoBarSeverity.Success;

    public NewSiteViewModel(StackREngine engine, EngineBridge bridge)
    {
        _engine = engine;
        _bridge = bridge;
        _www = engine.Settings.EffectiveWww(engine.Paths);

        // Sites existants : lecture rapide (dossier local), nécessaire pour refuser un doublon.
        _existingHosts = new HashSet<string>(engine.Sites.Scan().Select(s => s.Name.Host), StringComparer.Ordinal);
        LaravelUnavailable = ComputeLaravelUnavailable();
    }

    /// <summary>Raison pour laquelle le modèle Laravel est indisponible (PHP ou Composer manquant), sinon null.</summary>
    public string? LaravelUnavailable { get; }

    public bool IsLaravelEnabled => LaravelUnavailable is null && IsEditable;

    public bool HasLaravelWarning => LaravelUnavailable is not null;

    public bool HasNameError => NameError is not null;

    public bool HasUrlPreview => UrlPreview is not null;

    public bool HasResult => ResultMessage is not null;

    public bool IsEditable => !IsRunning && !IsDone;

    public bool ShowOutput => IsLaravel && (IsRunning || _output.Length > 0);

    /// <summary>Nom d'hôte du site créé (pour le bouton « Ouvrir »).</summary>
    public string? CreatedHost { get; private set; }

    partial void OnNameChanged(string value) => Validate(value);

    partial void OnNameErrorChanged(string? value) => OnPropertyChanged(nameof(HasNameError));

    partial void OnUrlPreviewChanged(string? value) => OnPropertyChanged(nameof(HasUrlPreview));

    partial void OnResultMessageChanged(string? value) => OnPropertyChanged(nameof(HasResult));

    partial void OnIsLaravelChanged(bool value) => OnPropertyChanged(nameof(ShowOutput));

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(IsLaravelEnabled));
        OnPropertyChanged(nameof(ShowOutput));
    }

    partial void OnIsDoneChanged(bool value)
    {
        OnPropertyChanged(nameof(IsEditable));
        OnPropertyChanged(nameof(IsLaravelEnabled));
    }

    private bool CanCreate() => !IsRunning && !IsDone && NameError is null && Name.Trim().Length > 0;

    private void Validate(string value)
    {
        var name = value.Trim();
        NameError = null;
        UrlPreview = null;
        if (name.Length > 0)
        {
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || name.IndexOfAny(['[', ']']) >= 0)
            {
                NameError = Strings.SitesNameChars;
            }
            else if (name.EndsWith('.') || !SiteName.TryFromFolder(name, out var site))
            {
                NameError = Strings.SitesNameInvalid;
            }
            else if (_existingHosts.Contains(site.Host) || Directory.Exists(Path.Combine(_www, name)))
            {
                NameError = Strings.Format(Strings.SitesNameExists, site.Host);
            }
            else
            {
                UrlPreview = EngineBridge.BuildUrl(_engine.Settings, site.Host);
            }
        }

        CreateCommand.NotifyCanExecuteChanged();
    }

    private string? ComputeLaravelUnavailable()
    {
        if (_engine.ActiveDirectory(ComponentKind.Php) is null)
        {
            return Strings.SitesLaravelNeedsPhp;
        }

        return _engine.ActiveDirectory(ComponentKind.Composer) is null ? Strings.SitesLaravelNeedsComposer : null;
    }

    [RelayCommand(CanExecute = nameof(CanCreate), IncludeCancelCommand = true)]
    private async Task CreateAsync(CancellationToken cancellationToken)
    {
        var folderName = Name.Trim();
        var folder = Path.Combine(_www, folderName);
        SiteName.TryFromFolder(folderName, out var site);

        IsRunning = true;
        ResultMessage = null;
        _output.Clear();
        Log = "";
        var createdFolder = false;
        try
        {
            using var pause = _bridge.PauseSiteSync();
            if (Directory.Exists(folder))
            {
                throw new IOException(Strings.Format(Strings.SitesNameExists, site.Host));
            }

            createdFolder = true;
            if (IsLaravel)
            {
                await CreateLaravelAsync(folder, cancellationToken).ConfigureAwait(true);
            }
            else
            {
                await Task.Run(() => CreateEmptySite(folder), cancellationToken).ConfigureAwait(true);
            }

            var result = await _bridge.SyncSitesAsync(force: false).ConfigureAwait(true);
            CreatedHost = site.Host;
            IsDone = true;
            if (result is { Warnings.Count: > 0 })
            {
                ResultSeverity = InfoBarSeverity.Warning;
                ResultMessage = Strings.Format(Strings.SitesCreatedWarnings, site.Host)
                    + Environment.NewLine + string.Join(Environment.NewLine, result.Warnings);
            }
            else
            {
                ResultSeverity = InfoBarSeverity.Success;
                ResultMessage = Strings.Format(Strings.SitesCreated, site.Host);
            }
        }
        catch (OperationCanceledException)
        {
            RemoveFolder(folder, createdFolder);
            ResultSeverity = InfoBarSeverity.Warning;
            ResultMessage = Strings.SitesCancelled;
        }
        catch (Exception ex) when (EngineBridge.IsExpected(ex))
        {
            RemoveFolder(folder, createdFolder);
            ResultSeverity = InfoBarSeverity.Error;
            ResultMessage = Strings.Format(Strings.SitesCreateFailed, ex.Message);
        }
        finally
        {
            IsRunning = false;
        }
    }

    [RelayCommand]
    private void OpenCreated()
    {
        if (CreatedHost is not null)
        {
            _bridge.OpenSite(CreatedHost);
        }
    }

    private static void CreateEmptySite(string folder)
    {
        var publicDir = Path.Combine(folder, "public");
        Directory.CreateDirectory(publicDir);
        File.WriteAllText(Path.Combine(publicDir, "index.php"), WelcomePage.Html, Utf8NoBom);
    }

    private static void RemoveFolder(string folder, bool created)
    {
        if (!created)
        {
            return;
        }

        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dossier partiellement supprimé (fichier verrouillé) : l'utilisateur le videra à la main.
        }
    }

    private async Task CreateLaravelAsync(string folder, CancellationToken ct)
    {
        var phpDir = _engine.ActiveDirectory(ComponentKind.Php);
        var composerDir = _engine.ActiveDirectory(ComponentKind.Composer);
        if (phpDir is null || composerDir is null)
        {
            throw new InvalidOperationException(LaravelUnavailable ?? Strings.SitesLaravelNeedsComposer);
        }

        var php = Path.Combine(phpDir, "php.exe");
        var phar = Path.Combine(composerDir, "composer.phar");
        if (!File.Exists(php) || !File.Exists(phar))
        {
            throw new InvalidOperationException(Strings.SitesLaravelIncomplete);
        }

        if (!File.Exists(Path.Combine(phpDir, "php.ini")))
        {
            // Composer a besoin d'openssl, zip, mbstring… : php.ini est écrit par la configuration.
            Append(Strings.SitesLaravelPreparing);
            await Task.Run(() => _engine.ApplyConfigurationAsync(), ct).ConfigureAwait(true);
        }

        Directory.CreateDirectory(_www);
        var info = new ProcessStartInfo(php)
        {
            WorkingDirectory = _www,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in new[] { phar, "create-project", "laravel/laravel", folder, "--no-interaction", "--prefer-dist", "--no-ansi" })
        {
            info.ArgumentList.Add(arg);
        }

        info.Environment["PATH"] = string.Join(Path.PathSeparator, phpDir, _engine.Paths.Shims, Environment.GetEnvironmentVariable("PATH"));
        info.Environment["COMPOSER_NO_INTERACTION"] = "1";

        Append("> composer create-project laravel/laravel " + Path.GetFileName(folder));
        using var process = new Process { StartInfo = info };
        process.OutputDataReceived += (_, e) => AppendFromProcess(e.Data);
        process.ErrorDataReceived += (_, e) => AppendFromProcess(e.Data);

        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(ex.Message, ex);
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            KillTree(process);
            throw;
        }

        // Vide les derniers messages en attente avant de conclure.
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(Strings.Format(Strings.SitesLaravelFailed, process.ExitCode));
        }
    }

    private static void KillTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
            process.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            // Déjà terminé ou inaccessible.
        }
    }

    private void AppendFromProcess(string? line)
    {
        if (line is not null)
        {
            Application.Current?.Dispatcher.BeginInvoke(() => Append(line));
        }
    }

    private void Append(string line)
    {
        _output.AppendLine(line);
        if (_output.Length > MaxOutputChars)
        {
            _output.Remove(0, _output.Length - MaxOutputChars);
        }

        Log = _output.ToString();
        OnPropertyChanged(nameof(ShowOutput));
    }
}
