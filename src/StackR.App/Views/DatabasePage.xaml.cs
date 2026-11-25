using StackR.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace StackR.App.Views;

/// <summary>Page « Database » (placeholder).</summary>
public partial class DatabasePage : INavigableView<DatabaseViewModel>
{
    public DatabasePage(DatabaseViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public DatabaseViewModel ViewModel { get; }
}
