using System.Windows;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StackR.Core;
using StackR.App.Resources;
using StackR.App.Services;
using StackR.App.Tray;
using StackR.App.ViewModels;
using StackR.App.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace StackR.App;

/// <summary>Point d'entrée : instance unique, hôte générique, thème, fenêtre et zone de notification.</summary>
#pragma warning disable CA1001 // _guard et _host sont libérés dans OnExit.
public partial class App : System.Windows.Application
{
    private static readonly Color BrandAccent = Color.FromRgb(0x7C, 0x3A, 0xED);

    private SingleInstanceGuard? _guard;
    private IHost? _host;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _guard = new SingleInstanceGuard();
        if (!_guard.TryAcquire())
        {
            SingleInstanceGuard.SignalExistingInstance();
            _guard.Dispose();
            _guard = null;
            Shutdown();
            return;
        }

        var minimized = e.Args.Contains("--minimized", StringComparer.OrdinalIgnoreCase);

        ApplyTheme();

        _host = BuildHost(e.Args);
        _host.Start();

        var shell = _host.Services.GetRequiredService<AppShell>();
        shell.ShowWindowRequested += (_, _) => ShowMainWindow();
        shell.QuitRequested += async (_, _) => await QuitAsync();

        _host.Services.GetRequiredService<TrayIconService>().Initialize();
        _guard.StartListening(() => Dispatcher.BeginInvoke(ShowMainWindow));

        if (!minimized)
        {
            ShowMainWindow();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _host?.Services.GetService<TrayIconService>()?.Dispose();
        _host?.StopAsync(TimeSpan.FromSeconds(3)).GetAwaiter().GetResult();
        _host?.Dispose();
        _guard?.Dispose();
        base.OnExit(e);
    }

    private static IHost BuildHost(string[] args)
    {
        var builder = Host.CreateApplicationBuilder(args);
        var services = builder.Services;

        // Moteur unique partagé par toutes les pages (libéré avec l'hôte).
        services.AddSingleton(_ => StackREngine.Create());

        services.AddSingleton<AppShell>();
        services.AddSingleton<TrayIconService>();

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

    /// <summary>Thème Windows (clair/sombre) + accent violet de la marque, réappliqué à chaque changement de thème.</summary>
    private static void ApplyTheme()
    {
        ApplicationThemeManager.ApplySystemTheme(updateAccent: false);
        ApplicationAccentColorManager.Apply(BrandAccent, ApplicationThemeManager.GetAppTheme());
        ApplicationThemeManager.Changed += (theme, _) =>
            Current.Dispatcher.BeginInvoke(() => ApplicationAccentColorManager.Apply(BrandAccent, theme));
    }

    private void ShowMainWindow()
    {
        var window = _host!.Services.GetRequiredService<MainWindow>();
        if (!window.IsVisible)
        {
            window.Show();
        }

        if (window.WindowState == WindowState.Minimized)
        {
            window.WindowState = WindowState.Normal;
        }

        window.Activate();
        window.Topmost = true;
        window.Topmost = false;
        window.Focus();
    }

    /// <summary>Quitter : propose d'arrêter les services s'il en tourne (Oui / Non / Annuler).</summary>
    private async Task QuitAsync()
    {
        var shell = _host!.Services.GetRequiredService<AppShell>();
        if (shell.AnyServiceRunning())
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
                shell.RaiseStopAll();
            }
            else if (result != Wpf.Ui.Controls.MessageBoxResult.Secondary)
            {
                return;
            }
        }

        shell.IsExiting = true;
        Shutdown();
    }
}
