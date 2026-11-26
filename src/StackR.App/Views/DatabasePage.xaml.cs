using StackR.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace StackR.App.Views;

/// <summary>Page « Base de données » : connexion MySQL, mot de passe root et client en terminal.</summary>
public partial class DatabasePage : INavigableView<DatabaseViewModel>
{
    public DatabasePage(DatabaseViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        // MySQL a pu être installé ou initialisé entre-temps : relecture à chaque affichage.
        Loaded += (_, _) => ViewModel.Refresh();
    }

    public DatabaseViewModel ViewModel { get; }
}
