using System.Windows;
using StackR.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace StackR.App.Views;

/// <summary>Page « Paramètres ».</summary>
public partial class SettingsPage : INavigableView<SettingsViewModel>
{
    public SettingsPage(SettingsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await ViewModel.RefreshAsync();
    }

    public SettingsViewModel ViewModel { get; }
}
