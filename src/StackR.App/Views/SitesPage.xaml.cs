using StackR.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace StackR.App.Views;

/// <summary>Page « Sites » (placeholder).</summary>
public partial class SitesPage : INavigableView<SitesViewModel>
{
    public SitesPage(SitesViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public SitesViewModel ViewModel { get; }
}
