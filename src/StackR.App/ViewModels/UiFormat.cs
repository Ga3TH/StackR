using System.Globalization;

namespace StackR.App.ViewModels;

/// <summary>Mise en forme des chaînes localisées (culture de l'interface).</summary>
internal static class UiFormat
{
    public static string Of(string format, params object[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);
}
