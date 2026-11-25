using StackR.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace StackR.App.Views;

/// <summary>Page « Versions » (placeholder).</summary>
public partial class VersionsPage : INavigableView<VersionsViewModel>
{
    public VersionsPage(VersionsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public VersionsViewModel ViewModel { get; }
}
