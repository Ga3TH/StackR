using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Security;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackR.App.Resources;
using StackR.App.Services;
using StackR.App.Tray;
using StackR.App.ViewModels;
using StackR.App.Views;
using StackR.Core;
using StackR.Core.Manifests;
using StackR.Core.Settings;
using StackR.Core.Updates;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace StackR.App;

/// <summary>
/// Point d'entrée. Ordre de démarrage :
/// 1. gestionnaires d'erreurs globaux, instance unique (attente de ~10 s après une relance) ;
/// 2. lecture de stackr.json SANS créer le moteur, culture et thème de l'interface ;
/// 3. premier lancement : l'assistant choisit la racine, l'enregistre, puis fait construire l'hôte et le moteur
///    (la création du moteur construit l'arborescence de la racine) ; sinon, hôte et moteur directement ;
/// 4. coquille : zone de notification, pont moteur (<see cref="EngineBridge.Initialize"/>), fenêtre principale.
/// </summary>
#pragma warning disable CA1001 // _guard et _host sont libérés dans OnExit.
public partial class App : System.Windows.Application
{
    private const string MinimizedArg = "--minimized";
    private const string RestartedArg = "--restarted";

    private static readonly Color BrandAccent = Color.FromRgb(0x7C, 0x3A, 0xED);
    private static readonly TimeSpan RestartMutexWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StopServicesTimeout = TimeSpan.FromSeconds(30);
    private static bool _themeHooked;

    private SingleInstanceGuard? _guard;
    private IHost? _host;
    private FirstRunWindow? _firstRunWindow;
    private string[] _args = [];
    private bool _shellReady;
    private bool _exiting;
    private bool _confirmingExit;
    private bool _errorDialogOpen;

    /// <summary>Vrai si le thème suit celui de Windows (paramètre « System »).</summary>
    internal static bool FollowsSystemTheme { get; private set; } = true;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        RegisterGlobalErrorHandlers();
        MouseWheelScrolling.Register();
        _args = e.Args;

        // Relance : l'ancienne instance libère le mutex juste avant de nous lancer, puis se ferme.
        var restarted = HasArg(RestartedArg);
        _guard = new SingleInstanceGuard();
        if (!_guard.TryAcquire(restarted ? RestartMutexWait : TimeSpan.Zero))
        {
            SingleInstanceGuard.SignalExistingInstance();
            _guard.Dispose();
            _guard = null;
            Shutdown();
            return;
        }

        _guard.StartListening(() => Dispatcher.BeginInvoke(ActivateCurrentWindow));
        _ = StartAsync();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Services.GetService<TrayIconService>()?.Dispose();
        try
        {
            _host?.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Arrêt de l'hôte trop long : on libère quand même.
        }

        _host?.Dispose();
        _guard?.Dispose();
        base.OnExit(e);
    }

    // ===== Démarrage =====

    private async Task StartAsync()
    {
        try
        {
            var (settings, settingsWereReset, loadError) = LoadSettings();
            if (settings is null)
            {
                ApplyTheme("System");
                await ShowStartupErrorAsync(
                    Strings.StartupSettingsErrorTitle,
                    FirstRunFormat.Format(Strings.StartupSettingsErrorMessage, SettingsStore.DefaultPath, loadError?.Message, CrashLog.FilePath),
                    offerWizard: false);
                ExitNow();
                return;
            }

            // Avant toute fenêtre : les textes x:Static et le thème sont lus à la création.
            ApplyCulture(settings.Language);
            ApplyTheme(settings.Theme);

            if (settings.FirstRunCompleted)
            {
                try
                {
                    await StartHostAsync();
                }
#pragma warning disable CA1031 // Frontière du démarrage : toute erreur de création du moteur est expliquée.
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    CrashLog.Write("Démarrage : création du moteur", ex);
                    var runWizard = await ShowStartupErrorAsync(
                        Strings.StartupEngineErrorTitle,
                        FirstRunFormat.Format(Strings.StartupEngineErrorMessage, settings.Root, ex.Message, CrashLog.FilePath),
                        offerWizard: true);
                    if (!runWizard)
                    {
                        ExitNow();
                        return;
                    }

                    // L'assistant repart des paramètres actuels ; il les réenregistre en choisissant la racine.
                    settings = settings with { FirstRunCompleted = false };
                }
            }

            var ranWizard = false;
            if (!settings.FirstRunCompleted)
            {
                ranWizard = true;
                if (!await RunFirstRunAsync(settings, settingsWereReset))
                {
                    ExitNow();
                    return;
                }
            }

            CompleteStartup(showWindow: ranWizard || !HasArg(MinimizedArg));
        }
#pragma warning disable CA1031 // Dernier rempart : message clair plutôt qu'un arrêt silencieux.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashLog.Write("Démarrage", ex);
            ShowUnexpectedError(ex);
            ExitNow();
        }
    }

    /// <summary>Lit les paramètres sans créer le moteur (aucun dossier créé sous la racine).</summary>
    private static (StackRSettings? Settings, bool WasReset, Exception? Error) LoadSettings()
    {
        var path = SettingsStore.DefaultPath;
        try
        {
            var existed = File.Exists(path);
            var settings = SettingsStore.Load(path);
            // JSON illisible : SettingsStore l'a renommé en .bak et renvoie les valeurs par défaut.
            var wasReset = existed && !File.Exists(path) && File.Exists(path + ".bak");
            return (settings, wasReset, null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or NotSupportedException)
        {
            CrashLog.Write("Démarrage : lecture des paramètres", ex);
            return (null, false, ex);
        }
    }

    /// <summary>Construit et démarre l'hôte puis crée le moteur (hors du thread de l'interface).</summary>
    private async Task StartHostAsync()
    {
        if (_host is not null)
        {
            return;
        }

        var host = BuildHost();
        try
        {
            await Task.Run(() =>
            {
                host.Start();
                _ = host.Services.GetRequiredService<StackREngine>();
            });
        }
        catch
        {
            host.Dispose();
            throw;
        }

        _host = host;
    }

    /// <summary>Rappel de l'assistant : enregistre racine et www, puis seulement crée l'hôte et le moteur.</summary>
    private async Task<StackREngine> StartEngineForFirstRunAsync(StackRSettings settings)
    {
        if (_host is null)
        {
            SettingsStore.Save(SettingsStore.DefaultPath, settings);
            await StartHostAsync();
        }

        return _host!.Services.GetRequiredService<StackREngine>();
    }

    /// <summary>Affiche l'assistant et attend sa fermeture ; vrai s'il a été mené à terme.</summary>
    private async Task<bool> RunFirstRunAsync(StackRSettings settings, bool settingsWereReset)
    {
        var viewModel = new FirstRunViewModel(settings, settingsWereReset, ManifestLoader.LoadEmbedded(), StartEngineForFirstRunAsync);
        var window = new FirstRunWindow(viewModel, FollowsSystemTheme);
        // Suite asynchrone : la fenêtre principale ne s'ouvre pas depuis l'événement Closed de l'assistant.
        var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        window.Closed += (_, _) => closed.TrySetResult();

        _firstRunWindow = window;
        MainWindow = window;
        window.Show();
        window.Activate();
        await closed.Task;
        _firstRunWindow = null;
        if (ReferenceEquals(MainWindow, window))
        {
            // Une fenêtre fermée ne doit pas rester propriétaire par défaut des boîtes de dialogue.
            MainWindow = null;
        }

        return viewModel.IsCompleted && _host is not null;
    }

    /// <summary>Branche la coquille sur le moteur, après l'assistant.</summary>
    private void CompleteStartup(bool showWindow)
    {
        var services = _host!.Services;
        var shell = services.GetRequiredService<AppShell>();
        shell.ShowWindowRequested += (_, _) => ShowMainWindow();
        shell.QuitRequested += async (_, _) => await QuitAsync();
        shell.RestartRequested += async (_, _) => await RestartAsync();
        shell.ExitForUpdateRequested += (_, _) => ExitNow();

        services.GetRequiredService<TrayIconService>().Initialize();
        try
        {
            services.GetRequiredService<EngineBridge>().Initialize();
        }
#pragma warning disable CA1031 // Le pont ne doit pas empêcher l'ouverture de la fenêtre : l'erreur est signalée.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashLog.Write("Démarrage : pont moteur", ex);
            ShowUnexpectedError(ex);
        }

        _shellReady = true;
        if (showWindow)
        {
            ShowMainWindow();
        }

        // Recherche de mise à jour en arrière-plan (aussi quand StackR démarre réduit dans la zone de notification).
        services.GetRequiredService<UpdateBannerViewModel>().StartBackgroundChecks();
    }

    private static IHost BuildHost()
    {
        // Pas d'arguments (--minimized, --restarted ne sont pas de la configuration) ; racine de contenu = dossier
        // de l'exécutable (au démarrage avec Windows, le dossier courant est System32).
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            Args = [],
            ContentRootPath = AppContext.BaseDirectory,
        });
        var services = builder.Services;

        // Moteur unique partagé par toutes les pages (libéré avec l'hôte). Il n'est résolu qu'après le choix
        // de la racine : sa création construit l'arborescence.
        services.AddSingleton(_ => StackREngine.Create());

        services.AddSingleton<AppShell>();
        services.AddSingleton<TrayIconService>();
        services.AddSingleton<EngineBridge>();

        services.AddSingleton(sp =>
        {
            var engine = sp.GetRequiredService<StackREngine>();
            var checker = new UpdateChecker(UpdateHttp.Client, UpdateChecker.RunningVersion(typeof(App).Assembly));
            return new UpdateManager(
                checker.CheckDetailedAsync,
                () => engine.Settings,
                change => engine.UpdateSettings(change),
                UpdateInstallation.IsInstalledCopy(Environment.ProcessPath));
        });
        services.AddSingleton<UpdateBannerViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();

        services.AddSingleton<DashboardViewModel>();
        services.AddSingleton<DashboardPage>();
        services.AddSingleton<SitesViewModel>();
        services.AddSingleton<SitesPage>();
        services.AddSingleton<VersionsViewModel>();
        services.AddSingleton<VersionsPage>();
        services.AddSingleton<DatabaseViewModel>();
        services.AddSingleton<DatabasePage>();
        services.AddSingleton<LogsViewModel>();
        services.AddSingleton<LogsPage>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<SettingsPage>();

        return builder.Build();
    }

    // ===== Culture et thème =====

    /// <summary>Langue de l'interface (« fr » par défaut, « en ») pour ce thread et les suivants.</summary>
    private static void ApplyCulture(string? language)
    {
        var culture = CultureInfo.GetCultureInfo(string.Equals(language, "en", StringComparison.OrdinalIgnoreCase) ? "en" : "fr");
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    /// <summary>Thème « System », « Light » ou « Dark » + accent violet de la marque, réappliqué à chaque changement.</summary>
    private static void ApplyTheme(string? theme)
    {
        if (string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase))
        {
            FollowsSystemTheme = false;
            ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, updateAccent: false);
        }
        else if (string.Equals(theme, "Dark", StringComparison.OrdinalIgnoreCase))
        {
            FollowsSystemTheme = false;
            ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, updateAccent: false);
        }
        else
        {
            FollowsSystemTheme = true;
            ApplicationThemeManager.ApplySystemTheme(updateAccent: false);
        }

        ApplicationAccentColorManager.Apply(BrandAccent, ApplicationThemeManager.GetAppTheme());
        if (!_themeHooked)
        {
            _themeHooked = true;
            ApplicationThemeManager.Changed += (newTheme, _) =>
                Current.Dispatcher.BeginInvoke(() => ApplicationAccentColorManager.Apply(BrandAccent, newTheme));
        }
    }

    // ===== Fenêtres =====

    /// <summary>Deuxième lancement de StackR : on ramène au premier plan l'assistant ou la fenêtre principale.</summary>
    private void ActivateCurrentWindow()
    {
        if (_firstRunWindow is { } wizard)
        {
            BringToFront(wizard);
        }
        else if (_shellReady)
        {
            ShowMainWindow();
        }
    }

    private void ShowMainWindow()
    {
        if (_host is null)
        {
            return;
        }

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        if (!window.IsVisible)
        {
            window.Show();
        }

        BringToFront(window);
    }

    private static void BringToFront(Window window)
    {
        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    // ===== Quitter et relancer =====

    /// <summary>Quitter : propose d'arrêter les services s'il en tourne (Oui / Non / Annuler).</summary>
    private async Task QuitAsync()
    {
        if (_exiting || !await ConfirmStopServicesAsync())
        {
            return;
        }

        ExitNow();
    }

    /// <summary>
    /// Relance StackR (paramètres qui n'agissent qu'au lancement) : même question que pour Quitter, libération du
    /// mutex d'instance unique, lancement de la nouvelle instance avec <c>--restarted</c>, puis fermeture.
    /// </summary>
    private async Task RestartAsync()
    {
        if (_exiting || !await ConfirmStopServicesAsync())
        {
            return;
        }

        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
        {
            ShowRestartError(null);
            return;
        }

        _exiting = true;
        var startInfo = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            WorkingDirectory = AppContext.BaseDirectory,
        };
        foreach (var arg in _args.Where(a =>
                     !string.Equals(a, RestartedArg, StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(a, MinimizedArg, StringComparison.OrdinalIgnoreCase)))
        {
            startInfo.ArgumentList.Add(arg);
        }

        startInfo.ArgumentList.Add(RestartedArg);

        // Mutex libéré AVANT le lancement : la nouvelle instance l'attend jusqu'à 10 s.
        _guard?.Dispose();
        _guard = null;
        try
        {
            using var _ = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            CrashLog.Write("Relance", ex);
            // Échec : on reste l'instance principale.
            _guard = new SingleInstanceGuard();
            if (_guard.TryAcquire())
            {
                _guard.StartListening(() => Dispatcher.BeginInvoke(ActivateCurrentWindow));
            }

            _exiting = false;
            ShowRestartError(ex);
            return;
        }

        ExitNow();
    }

    /// <summary>Si des services tournent : Oui (les arrêter), Non (les laisser), Annuler. Faux si annulé.</summary>
    private async Task<bool> ConfirmStopServicesAsync()
    {
        var shell = _host?.Services.GetRequiredService<AppShell>();
        if (shell is null || !shell.AnyServiceRunning())
        {
            return true;
        }

        if (_confirmingExit)
        {
            // Une question est déjà affichée (double clic sur Quitter, par exemple).
            return false;
        }

        _confirmingExit = true;
        try
        {
            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = Strings.QuitTitle,
                Content = Strings.QuitMessage,
                PrimaryButtonText = Strings.QuitYes,
                SecondaryButtonText = Strings.QuitNo,
                CloseButtonText = Strings.QuitCancel,
                PrimaryButtonAppearance = ControlAppearance.Primary,
                Topmost = true,
            };

            var result = await dialog.ShowDialogAsync();
            if (result == Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                await StopServicesAsync();
                return true;
            }

            return result == Wpf.Ui.Controls.MessageBoxResult.Secondary;
        }
        finally
        {
            _confirmingExit = false;
        }
    }

    /// <summary>Arrête réellement les services (attendu, 30 s au plus) avant de fermer.</summary>
    private async Task StopServicesAsync()
    {
        if (_host?.Services.GetService<StackREngine>() is not { } engine)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(StopServicesTimeout);
        try
        {
            await Task.Run(() => engine.StopAllAsync(timeout.Token));
        }
#pragma warning disable CA1031 // On quitte quand même : l'échec est journalisé.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            CrashLog.Write("Arrêt des services avant fermeture", ex);
        }
    }

    private void ExitNow()
    {
        _exiting = true;
        if (_host?.Services.GetService<AppShell>() is { } shell)
        {
            shell.IsExiting = true;
        }

        Shutdown();
    }

    private static void ShowRestartError(Exception? ex) =>
        System.Windows.MessageBox.Show(
            FirstRunFormat.Format(Strings.StartupRestartErrorMessage, ex?.Message ?? "Environment.ProcessPath"),
            Strings.StartupRestartErrorTitle,
            System.Windows.MessageBoxButton.OK,
            System.Windows.MessageBoxImage.Warning);

    // ===== Erreurs =====

    private void RegisterGlobalErrorHandlers()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => CrashLog.Write("Erreur fatale", e.ExceptionObject as Exception);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        CrashLog.Write("Interface", e.Exception);
        e.Handled = true;
        ShowUnexpectedError(e.Exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        CrashLog.Write("Tâche non observée", e.Exception);
        e.SetObserved();
        var error = e.Exception.InnerException ?? e.Exception;
        Dispatcher.BeginInvoke(() => ShowUnexpectedError(error));
    }

    /// <summary>Message d'erreur inattendue (un seul à la fois, pour ne pas empiler les fenêtres).</summary>
    private void ShowUnexpectedError(Exception error)
    {
        if (_errorDialogOpen)
        {
            return;
        }

        _errorDialogOpen = true;
        try
        {
            System.Windows.MessageBox.Show(
                FirstRunFormat.Format(Strings.StartupUnexpectedMessage, error.Message, CrashLog.FilePath),
                Strings.StartupUnexpectedTitle,
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Error);
        }
        finally
        {
            _errorDialogOpen = false;
        }
    }

    /// <summary>Erreur de démarrage expliquée ; avec <paramref name="offerWizard"/>, propose de relancer l'assistant.</summary>
    private static async Task<bool> ShowStartupErrorAsync(string title, string message, bool offerWizard)
    {
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = title,
            Content = new System.Windows.Controls.TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, MaxWidth = 520 },
            CloseButtonText = Strings.StartupQuit,
            Topmost = true,
        };
        if (offerWizard)
        {
            dialog.PrimaryButtonText = Strings.StartupRunWizard;
            dialog.PrimaryButtonAppearance = ControlAppearance.Primary;
        }

        return await dialog.ShowDialogAsync() == Wpf.Ui.Controls.MessageBoxResult.Primary && offerWizard;
    }

    private bool HasArg(string name) => _args.Contains(name, StringComparer.OrdinalIgnoreCase);
}
