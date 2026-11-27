using System.Windows;
using System.Windows.Threading;
using StackR.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace StackR.App.Views;

/// <summary>Page « Tableau de bord » : accueil, services en direct, mes sites, outils rapides.</summary>
public partial class DashboardPage : INavigableView<DashboardViewModel>
{
    private readonly DispatcherTimer _metrics = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };

    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Mesures (mémoire, durée) seulement tant que la page est affichée ; lecture hors du thread de l'interface.
        _metrics.Tick += async (_, _) => await ViewModel.RefreshMetricsAsync();
        Loaded += async (_, _) =>
        {
            // Versions installées ou réglages modifiés depuis les autres pages : relecture à l'affichage.
            ViewModel.Refresh();
            _metrics.Start();
            await ViewModel.RefreshMetricsAsync();
        };
        Unloaded += (_, _) => _metrics.Stop();
        viewModel.NavigateToVersionsRequested += (_, _) => MainWindow?.NavigateTo(typeof(VersionsPage));
        viewModel.NavigateRequested += (_, page) => MainWindow?.NavigateTo(page switch
        {
            "sites" => typeof(SitesPage),
            "logs" => typeof(LogsPage),
            "database" => typeof(DatabasePage),
            _ => typeof(DashboardPage),
        });
    }

    public DashboardViewModel ViewModel { get; }

    private static MainWindow? MainWindow => Application.Current?.Windows.OfType<MainWindow>().FirstOrDefault();
}
