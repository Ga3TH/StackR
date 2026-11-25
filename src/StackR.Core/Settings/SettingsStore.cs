using System.Text;
using System.Text.Json;
using StackR.Core.IO;

namespace StackR.Core.Settings;

// Lecture et écriture atomique de stackr.json.
public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string DefaultPath { get; } = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StackR", "stackr.json");

    // Fichier absent : défauts. JSON invalide : fichier renommé en .bak, défauts.
    public static StackRSettings Load(string path)
    {
        if (!File.Exists(path))
        {
            return new StackRSettings();
        }

        try
        {
            var json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<StackRSettings>(json, Options) ?? new StackRSettings();
            // "activeVersions": null dans le JSON contourne l'initialiseur : on normalise.
            return loaded.ActiveVersions is null ? loaded with { ActiveVersions = new Dictionary<string, string>() } : loaded;
        }
        catch (JsonException)
        {
            File.Move(path, path + ".bak", overwrite: true);
            return new StackRSettings();
        }
    }

    // Écrit dans un .tmp puis remplace le fichier cible (évite un fichier tronqué).
    public static void Save(string path, StackRSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        AtomicFile.WriteAllText(path, JsonSerializer.Serialize(settings, Options), new UTF8Encoding(false));
    }
}
