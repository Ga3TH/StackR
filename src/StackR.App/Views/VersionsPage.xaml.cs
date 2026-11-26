using StackR.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace StackR.App.Views;

/// <summary>Page « Versions » : installer, activer et désinstaller les versions des composants.</summary>
public partial class VersionsPage : INavigableView<VersionsViewModel>
{
    public VersionsPage(VersionsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // Les versions ont pu changer ailleurs (assistant, ligne de commande) : relecture à chaque affichage.
        Loaded += (_, _) => ViewModel.Refresh();
    }

    public VersionsViewModel ViewModel { get; }
}
