using StackR.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace StackR.App.Views;

/// <summary>Page « Logs » (placeholder).</summary>
public partial class LogsPage : INavigableView<LogsViewModel>
{
    public LogsPage(LogsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
    }

    public LogsViewModel ViewModel { get; }
}
