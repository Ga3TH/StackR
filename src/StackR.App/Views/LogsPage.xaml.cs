using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using StackR.App.ViewModels;
using Wpf.Ui.Abstractions.Controls;

namespace StackR.App.Views;

/// <summary>Page « Journaux » : le suivi ne tourne que tant que la page est chargée.</summary>
public partial class LogsPage : INavigableView<LogsViewModel>
{
    public LogsPage(LogsViewModel viewModel)
    {
        ViewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public LogsViewModel ViewModel { get; }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.PropertyChanged += OnViewModelPropertyChanged;
        ViewModel.Start();
        ScrollToEndIfFollowing();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ViewModel.PropertyChanged -= OnViewModelPropertyChanged;
        ViewModel.Stop();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LogsViewModel.LogText) or nameof(LogsViewModel.Follow))
        {
            ScrollToEndIfFollowing();
        }
    }

    // La liaison met le texte à jour après la notification : on diffère le défilement.
    private void ScrollToEndIfFollowing()
    {
        if (ViewModel.Follow)
        {
            Dispatcher.BeginInvoke(() => LogBox.ScrollToEnd(), DispatcherPriority.Loaded);
        }
    }
}
