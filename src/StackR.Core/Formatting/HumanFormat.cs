using System.Globalization;

namespace StackR.Core.Formatting;

/// <summary>
/// Valeurs lisibles pour l'interface (taille, durée, ancienneté), en français par défaut ou en anglais
/// selon la culture passée.
/// </summary>
public static class HumanFormat
{
    private static readonly string[] UnitsFr = ["o", "Ko", "Mo", "Go", "To"];
    private static readonly string[] UnitsEn = ["B", "KB", "MB", "GB", "TB"];

    /// <summary>« 512 o », « 1,4 Mo », « 35 Mo », « 1,2 Go » (une décimale sous 10).</summary>
    public static string Bytes(long bytes, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var units = IsFrench(culture) ? UnitsFr : UnitsEn;
        if (bytes < 1024)
        {
            return string.Create(culture, $"{Math.Max(0, bytes)} {units[0]}");
        }

        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        var text = value < 10 ? value.ToString("0.#", culture) : Math.Round(value).ToString("0", culture);
        return text + " " + units[unit];
    }

    /// <summary>Durée de fonctionnement : « 45 s », « 12 min », « 2 h 05 min », « 3 j 4 h ».</summary>
    public static string Duration(TimeSpan duration, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        if (duration < TimeSpan.Zero)
        {
            duration = TimeSpan.Zero;
        }

        if (duration.TotalMinutes < 1)
        {
            return string.Create(culture, $"{(int)duration.TotalSeconds} s");
        }

        if (duration.TotalHours < 1)
        {
            return string.Create(culture, $"{(int)duration.TotalMinutes} min");
        }

        if (duration.TotalDays < 1)
        {
            return string.Create(culture, $"{(int)duration.TotalHours} h {duration.Minutes:00} min");
        }

        var day = IsFrench(culture) ? "j" : "d";
        return string.Create(culture, $"{(int)duration.TotalDays} {day} {duration.Hours} h");
    }

    /// <summary>Ancienneté : « à l'instant », « il y a 5 min », « hier », « il y a 4 jours », puis la date.</summary>
    public static string Ago(DateTimeOffset when, DateTimeOffset now, CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        var fr = IsFrench(culture);
        var age = now - when;
        if (age < TimeSpan.FromMinutes(1))
        {
            return fr ? "à l’instant" : "just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            var m = (int)age.TotalMinutes;
            return fr ? string.Create(culture, $"il y a {m} min") : string.Create(culture, $"{m} min ago");
        }

        if (age < TimeSpan.FromDays(1))
        {
            var h = (int)age.TotalHours;
            return fr ? string.Create(culture, $"il y a {h} h") : string.Create(culture, $"{h} h ago");
        }

        var days = (int)age.TotalDays;
        if (days == 1)
        {
            return fr ? "hier" : "yesterday";
        }

        if (days < 30)
        {
            return fr ? string.Create(culture, $"il y a {days} jours") : string.Create(culture, $"{days} days ago");
        }

        return when.ToOffset(now.Offset).ToString("d MMM yyyy", culture);
    }

    private static bool IsFrench(CultureInfo culture) =>
        string.Equals(culture.TwoLetterISOLanguageName, "fr", StringComparison.OrdinalIgnoreCase)
        || culture.Equals(CultureInfo.InvariantCulture);
}
