using System.Text.Json;

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
            return JsonSerializer.Deserialize<StackRSettings>(json, Options) ?? new StackRSettings();
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

        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(settings, Options));
        File.Move(tmp, path, overwrite: true);
    }
}
