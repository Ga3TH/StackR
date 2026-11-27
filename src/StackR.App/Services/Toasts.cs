using StackR.App.Resources;
using Wpf.Ui.Controls;

namespace StackR.App.Services;

/// <summary>Courtes notifications de réussite en bas de la fenêtre principale (Snackbar WPF-UI).</summary>
public static class Toasts
{
    private static SnackbarPresenter? _presenter;

    /// <summary>Branché par la fenêtre principale.</summary>
    public static void Attach(SnackbarPresenter presenter) => _presenter = presenter;

    /// <summary>Affiche « C'est fait » + <paramref name="message"/> ; vrai si un toast a pu être montré.</summary>
    public static bool Success(string message)
    {
        if (_presenter is null)
        {
            return false;
        }

        var snackbar = new Snackbar(_presenter)
        {
            Title = Strings.UiToastDone,
            Content = message,
            Appearance = ControlAppearance.Success,
            Icon = new SymbolIcon(SymbolRegular.CheckmarkCircle24),
            Timeout = TimeSpan.FromSeconds(3),
        };
        _ = _presenter.ImmediatelyDisplay(snackbar);
        return true;
    }
}
