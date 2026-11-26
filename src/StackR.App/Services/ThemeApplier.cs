using System.Windows.Media;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace StackR.App.Services;

/// <summary>
/// Applique le thème choisi dans les paramètres (« System », « Light », « Dark ») en gardant l'accent violet de la marque.
/// À appeler aussi au démarrage avec <c>engine.Settings.Theme</c>.
/// </summary>
public static class ThemeApplier
{
    private static readonly Color BrandAccent = Color.FromRgb(0x7C, 0x3A, 0xED);

    public static void Apply(string theme)
    {
        switch (theme)
        {
            case "Light":
                ApplicationThemeManager.Apply(ApplicationTheme.Light, WindowBackdropType.Mica, updateAccent: false);
                break;
            case "Dark":
                ApplicationThemeManager.Apply(ApplicationTheme.Dark, WindowBackdropType.Mica, updateAccent: false);
                break;
            default:
                ApplicationThemeManager.ApplySystemTheme(updateAccent: false);
                break;
        }

        ApplicationAccentColorManager.Apply(BrandAccent, ApplicationThemeManager.GetAppTheme());
    }
}
