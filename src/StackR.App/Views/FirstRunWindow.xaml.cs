using System.ComponentModel;
using StackR.App.Resources;
using StackR.App.ViewModels;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace StackR.App.Views;

/// <summary>
/// Fenêtre de l'assistant de premier lancement. La fermer avant la fin revient à quitter StackR
/// (après confirmation) ; une installation en cours est d'abord annulée proprement.
/// </summary>
public partial class FirstRunWindow : FluentWindow
{
    private readonly FirstRunViewModel _viewModel;
    private bool _allowClose;
    private bool _confirmPending;

    public FirstRunWindow(FirstRunViewModel viewModel, bool followSystemTheme)
    {
        _viewModel = viewModel ?? throw new ArgumentNullException(nameof(viewModel));
        DataContext = viewModel;
        InitializeComponent();

        if (followSystemTheme)
        {
            SystemThemeWatcher.Watch(this, WindowBackdropType.Mica, updateAccents: false);
        }

        viewModel.CloseRequested += (_, _) =>
        {
            _allowClose = true;
            Close();
        };
        viewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(FirstRunViewModel.StepIndex))
            {
                ContentScroll.ScrollToTop();
            }
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);
        if (_allowClose)
        {
            base.OnClosing(e);
            return;
        }

        e.Cancel = true;
        if (_viewModel.IsCompleted)
        {
            // Paramètres déjà enregistrés : on laisse finir la configuration, la fenêtre se fermera seule.
            if (!_viewModel.IsBusy)
            {
                _allowClose = true;
                e.Cancel = false;
            }

            base.OnClosing(e);
            return;
        }

        if (!_confirmPending)
        {
            _ = ConfirmQuitAsync();
        }

        base.OnClosing(e);
    }

    private async Task ConfirmQuitAsync()
    {
        _confirmPending = true;
        try
        {
            var dialog = new Wpf.Ui.Controls.MessageBox
            {
                Title = Strings.FrQuitTitle,
                Content = _viewModel.IsInstalling ? Strings.FrQuitMessageInstalling : Strings.FrQuitMessage,
                PrimaryButtonText = Strings.FrQuitConfirm,
                CloseButtonText = Strings.FrQuitContinue,
                PrimaryButtonAppearance = ControlAppearance.Danger,
                Owner = this,
                WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner,
            };

            if (await dialog.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
            {
                return;
            }

            await _viewModel.CancelAndWaitAsync();
            _allowClose = true;
            Close();
        }
        finally
        {
            _confirmPending = false;
        }
    }
}
