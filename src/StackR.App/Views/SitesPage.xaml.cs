using StackR.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace StackR.App.Views;

/// <summary>Page « Sites » : liste des sites .test, actions et création.</summary>
public partial class SitesPage : INavigableView<SitesViewModel>
{
    public SitesPage(SitesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Dossier www modifié pendant que la page était cachée : relecture à l'affichage.
        Loaded += async (_, _) => await ViewModel.RefreshAsync();
    }

    public SitesViewModel ViewModel { get; }
}
