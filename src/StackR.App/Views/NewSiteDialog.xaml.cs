using System.ComponentModel;
using System.Windows;
using StackR.App.ViewModels;
using Wpf.Ui.Controls;

namespace StackR.App.Views;

/// <summary>Fenêtre « Nouveau site » : nom validé en direct, modèle Vide ou Laravel, sortie de Composer.</summary>
public partial class NewSiteDialog : FluentWindow
{
    private readonly NewSiteViewModel _viewModel;
    private bool _closeWhenIdle;

    public NewSiteDialog(NewSiteViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();

        Loaded += (_, _) => NameBox.Focus();
        viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_viewModel.IsRunning)
        {
            // La création est annulée proprement (processus arrêté, dossier nettoyé), puis la fenêtre se ferme.
            e.Cancel = true;
            _closeWhenIdle = true;
            _viewModel.CreateCancelCommand.Execute(null);
        }

        base.OnClosing(e);
    }

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(NewSiteViewModel.Log))
        {
            Dispatcher.BeginInvoke(LogBox.ScrollToEnd);
        }
        else if (e.PropertyName == nameof(NewSiteViewModel.IsRunning) && !_viewModel.IsRunning && _closeWhenIdle)
        {
            _closeWhenIdle = false;
            Close();
        }
    }
}
