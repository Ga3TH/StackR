using StackR.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace StackR.App.Views;

/// <summary>Page « Settings » (placeholder).</summary>
public partial class SettingsPage : INavigableView<SettingsViewModel>
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public SettingsViewModel ViewModel { get; }
}
