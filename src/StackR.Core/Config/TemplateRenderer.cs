using System.Reflection;
using System.Text.RegularExpressions;

namespace StackR.Core.Config;

// Rendu de gabarits « {{Clé}} » : toute clé absente lève KeyNotFoundException.
public static partial class TemplateRenderer
{
    public static string Render(string template, IReadOnlyDictionary<string, string> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(values);

        return PlaceholderRegex().Replace(template, match =>
        {
            var key = match.Groups[1].Value;
            if (!values.TryGetValue(key, out var value))
            {
                throw new KeyNotFoundException($"Clé de gabarit manquante : {key}");
            }

            return value;
        });
    }

    // Chemin Windows -> slashs « / » (utilisé entre guillemets dans les gabarits).
    public static string ConfPath(string windowsPath)
    {
        ArgumentNullException.ThrowIfNull(windowsPath);
        return windowsPath.Replace('\\', '/');
    }

    internal static string LoadEmbedded(string fileName)
    {
        var name = "StackR.Core.Templates." + fileName;
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new FileNotFoundException($"Gabarit embarqué introuvable : {name}");
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return reader.ReadToEnd();
    }

    [GeneratedRegex(@"\{\{(\w+)\}\}")]
    private static partial Regex PlaceholderRegex();
}
