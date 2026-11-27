using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using StackR.App;
using StackR.App.Services;
using StackR.App.ViewModels;
using StackR.App.Views;
using StackR.Core;
using StackR.Core.Updates;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;
using Wpf.Ui.Markup;

namespace StackR.Screens;

/// <summary>
/// Captures d'écran de développement : chaque page (et la fenêtre « Nouveau site ») en clair puis en sombre,
/// 1280×800 (et une version haute pour voir la suite de la page), dans le dossier passé en argument.
/// </summary>
internal static class Program
{
    private static readonly Color BrandAccent = Color.FromRgb(0x7C, 0x3A, 0xED);

    private static readonly (Type Page, string Name)[] Pages =
    [
        (typeof(DashboardPage), "dashboard"),
        (typeof(SitesPage), "sites"),
        (typeof(VersionsPage), "versions"),
        (typeof(DatabasePage), "database"),
        (typeof(LogsPage), "logs"),
        (typeof(SettingsPage), "settings"),
    ];

    [STAThread]
    private static int Main(string[] args)
    {
        var outDir = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "StackR.Screens");
        var only = args.Length > 1 ? args[1] : null;
        Directory.CreateDirectory(outDir);
        var work = Path.Combine(Path.GetTempPath(), "StackR.Screens.work");
        var fake = FakeEnvironment.Create(work);

        // Jamais l'installation réelle : paramètres factices pour tout ce qui lirait le chemin par défaut.
        Environment.SetEnvironmentVariable("STACKR_SETTINGS", fake.SettingsPath);
        CultureInfo.CurrentUICulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.GetCultureInfo("fr");
        CultureInfo.CurrentCulture = CultureInfo.DefaultThreadCurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        StackR.App.App.FollowsSystemTheme = false;

        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Dark });
        app.Resources.MergedDictionaries.Add(new ControlsDictionary());
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/StackR.App;component/Resources/Styles.xaml", UriKind.Absolute),
        });

        var exit = 0;
        app.Startup += async (_, _) =>
        {
            try
            {
                await ShootAsync(fake, outDir, only);
            }
#pragma warning disable CA1031 // Outil : toute erreur est affichée.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                Console.Error.WriteLine(ex);
                exit = 1;
            }
            finally
            {
                app.Shutdown();
            }
        };
        app.Run();
        Console.WriteLine("Captures : " + outDir);
        return exit;
    }

    private static async Task ShootAsync(FakeEnvironment fake, string outDir, string? only)
    {
        using var engine = StackREngine.Create(fake.SettingsPath, new EngineOptions
        {
            HostsPath = Path.Combine(fake.Root, "hosts"),
            ManageCertificateStore = false,
            IsElevated = () => false,
            RunElevated = _ => Task.FromResult(-1),
            InstallDirectory = Path.Combine(fake.Root, "app"),
        });
        engine.Secrets.Set("mysql-root", "pK8#vQ2m!Lx9Wz4r");

        var services = new ServiceCollection();
        services.AddSingleton(engine);
        services.AddSingleton<AppShell>();
        services.AddSingleton<EngineBridge>();
        services.AddSingleton(sp => new UpdateManager(
            _ => Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.UpToDate, null)),
            () => engine.Settings,
            _ => { },
            isInstalledCopy: false));
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
        using var provider = services.BuildServiceProvider();

        Preview.Apply(provider, engine);

        var window = provider.GetRequiredService<MainWindow>();
        window.WindowBackdropType = WindowBackdropType.None;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        window.Left = -20000;
        window.Top = -20000;
        window.Width = 1280;
        window.Height = 800;
        window.ShowActivated = false;
        window.ShowInTaskbar = false;
        window.Show();
        await Settle(1200);

        foreach (var theme in new[] { ApplicationTheme.Light, ApplicationTheme.Dark })
        {
            ApplicationThemeManager.Apply(theme, WindowBackdropType.None, updateAccent: false);
            ApplicationAccentColorManager.Apply(BrandAccent, theme);
            var themeName = theme == ApplicationTheme.Light ? "light" : "dark";
            await Settle(400);

            foreach (var (page, name) in Pages)
            {
                if (only is not null && !name.Contains(only, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                window.Height = 800;
                window.RootNavigation.Navigate(page);
                await Settle(1500);
                Save(window, 1280, 800, Path.Combine(outDir, $"{themeName}-{name}.png"));

                window.Height = 1500;
                await Settle(600);
                Save(window, 1280, 1500, Path.Combine(outDir, $"{themeName}-{name}-tall.png"));
            }

            window.Height = 800;
            if (only is null || "newsite".Contains(only, StringComparison.OrdinalIgnoreCase))
            {
                var dialog = new NewSiteDialog(new NewSiteViewModel(engine, provider.GetRequiredService<EngineBridge>()))
                {
                    WindowBackdropType = WindowBackdropType.None,
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000,
                    Top = -20000,
                    ShowActivated = false,
                };
                Preview.ApplyNewSite(dialog);
                dialog.Show();
                await Settle(900);
                Save(dialog, (int)Math.Ceiling(dialog.ActualWidth), (int)Math.Ceiling(dialog.ActualHeight), Path.Combine(outDir, $"{themeName}-newsite.png"));
                dialog.Close();
            }
        }

        window.Hide();
    }

    private static async Task Settle(int ms)
    {
        await Task.Delay(ms);
        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
    }

    private static void Save(Window window, int width, int height, string path)
    {
        window.UpdateLayout();
        var rtb = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var background = new DrawingVisual();
        using (var dc = background.RenderOpen())
        {
            var brush = Application.Current.Resources["ApplicationBackgroundBrush"] as Brush ?? Brushes.White;
            dc.DrawRectangle(brush, null, new Rect(0, 0, width, height));
        }

        rtb.Render(background);
        rtb.Render(window);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Console.WriteLine(path);
    }
}
