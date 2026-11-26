using System.Windows;
using StackR.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace StackR.App.Views;

/// <summary>Page « Tableau de bord » : cartes de services, PHP actif, raccourcis.</summary>
public partial class DashboardPage : INavigableView<DashboardViewModel>
{
    public DashboardPage(DashboardViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Versions installées ou réglages modifiés depuis les autres pages : relecture à l'affichage.
        Loaded += (_, _) => ViewModel.Refresh();
        viewModel.NavigateToVersionsRequested += (_, _) =>
            (Application.Current?.MainWindow as MainWindow)?.RootNavigation.Navigate(typeof(VersionsPage));
    }

    public DashboardViewModel ViewModel { get; }
}
