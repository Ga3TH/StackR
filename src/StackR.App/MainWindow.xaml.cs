using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using StackR.App.Services;
using StackR.App.ViewModels;
using StackR.App.Views;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace StackR.App;

/// <summary>Fenêtre principale Fluent (Mica, navigation à gauche).</summary>
public partial class MainWindow : FluentWindow
{
    private readonly AppShell _shell;
    private readonly IServiceProvider _services;

    public MainWindow(MainWindowViewModel viewModel, AppShell shell, IServiceProvider services)
    {
        _shell = shell;
        _services = services;
        DataContext = viewModel;
        InitializeComponent();

        RootNavigation.SetServiceProvider(services);
        if (!Motion.Enabled)
        {
            // Animations coupées dans Windows : pas de transition entre les pages.
            RootNavigation.Transition = Wpf.Ui.Animations.Transition.None;
        }

        Toasts.Attach(SnackbarPresenter);
        PreviewKeyDown += OnShortcut;
        // Suivre le thème de Windows seulement si l'utilisateur n'a pas imposé Clair ou Sombre.
        if (App.FollowsSystemTheme)
        {
            SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: false);
        }
        Loaded += (_, _) =>
        {
            RootNavigation.Navigate(typeof(DashboardPage));
            // Après l'affichage de la fenêtre : recherche de mise à jour en arrière-plan.
            viewModel.Update.StartBackgroundChecks();
        };
    }

    private static readonly Type[] ShortcutPages =
    [
        typeof(DashboardPage), typeof(SitesPage), typeof(VersionsPage), typeof(DatabasePage), typeof(LogsPage), typeof(SettingsPage),
    ];

    /// <summary>Affiche une page de la navigation.</summary>
    public void NavigateTo(Type page) => RootNavigation.Navigate(page);

    /// <summary>Ctrl+1…6 : pages ; F5 : actualiser la page affichée.</summary>
    private void OnShortcut(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.Control && e.Key is >= Key.D1 and <= Key.D6 or >= Key.NumPad1 and <= Key.NumPad6)
        {
            var index = e.Key >= Key.NumPad1 ? e.Key - Key.NumPad1 : e.Key - Key.D1;
            NavigateTo(ShortcutPages[index]);
            e.Handled = true;
        }
        else if (e.Key == Key.F5 && Keyboard.Modifiers == ModifierKeys.None)
        {
            if (RootNavigation.SelectedItem?.TargetPageType is { } type && _services.GetService(type) is FrameworkElement { DataContext: IRefreshable page })
            {
                _ = page.RefreshAsync();
            }

            ((MainWindowViewModel)DataContext).RefreshStatus();
            e.Handled = true;
        }
    }

    /// <summary>Fermer = cacher dans la zone de notification, sauf sortie explicite.</summary>
    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_shell.IsExiting)
        {
            e.Cancel = true;
            Hide();
            _shell.RaiseHiddenToTray();
        }

        base.OnClosing(e);
    }
}
