using System.ComponentModel;
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

    public MainWindow(MainWindowViewModel viewModel, AppShell shell, IServiceProvider services)
    {
        _shell = shell;
        DataContext = viewModel;
        InitializeComponent();

        RootNavigation.SetServiceProvider(services);
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
