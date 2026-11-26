using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace StackR.App.Views;

/// <summary>Visible si l'étape courante (int) vaut le paramètre.</summary>
public sealed class StepVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int step && int.TryParse(parameter as string, NumberStyles.Integer, CultureInfo.InvariantCulture, out var expected) && step == expected
            ? Visibility.Visible
            : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Visible si la valeur est « vraie » : booléen vrai, texte non vide, collection non vide, objet non nul.
/// Le paramètre « Invert » inverse le résultat.
/// </summary>
public sealed class TruthyVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var truthy = value switch
        {
            null => false,
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            int i => i != 0,
            ICollection c => c.Count > 0,
            _ => true,
        };

        if (string.Equals(parameter as string, "Invert", StringComparison.OrdinalIgnoreCase))
        {
            truthy = !truthy;
        }

        return truthy ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Inverse un booléen (lecture seule ↔ modifiable, etc.).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}
