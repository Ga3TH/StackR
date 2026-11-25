using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace StackR.Core.Manifests;

public static partial class ManifestLoader
{
    private const string ResourceName = "StackR.Core.components.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static Manifest LoadEmbedded()
    {
        using var stream = typeof(ManifestLoader).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new ManifestException($"Ressource intégrée introuvable : {ResourceName}");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    public static Manifest Parse(string json)
    {
        Root? root;
        try
        {
            root = JsonSerializer.Deserialize<Root>(json, Options);
        }
        catch (JsonException ex)
        {
            throw new ManifestException("Manifeste illisible : " + ex.Message, ex);
        }

        if (root?.Releases is null)
            throw new ManifestException("Manifeste vide ou sans liste « releases ».");

        var list = new List<ComponentRelease>();
        foreach (var e in root.Releases)
        {
            var id = $"{e.Component} {e.Version}";
            if (string.IsNullOrWhiteSpace(e.Version))
                throw new ManifestException($"Version vide pour {e.Component}.");
            if (!string.Equals(e.Channel, "stable", StringComparison.Ordinal))
                throw new ManifestException($"{id} : canal « {e.Channel} » refusé (stable uniquement).");
            if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var url) || url.Scheme != Uri.UriSchemeHttps)
                throw new ManifestException($"{id} : l'URL doit être en https.");
            if (e.Sha256 is null || !Sha256Regex().IsMatch(e.Sha256))
                throw new ManifestException($"{id} : SHA-256 invalide.");

            list.Add(new ComponentRelease(
                e.Component, e.Version, "stable", url,
                e.Sha256.ToLower(CultureInfo.InvariantCulture), e.SizeBytes, e.Package,
                e.Requires ?? [], e.ArchiveRoot));
        }
        return new Manifest(list);
    }

    [GeneratedRegex("^[0-9a-fA-F]{64}$")]
    private static partial Regex Sha256Regex();

    private sealed class Root
    {
        public List<Entry>? Releases { get; set; }
    }

    private sealed class Entry
    {
        public ComponentKind Component { get; set; }
        public string? Version { get; set; }
        public string? Channel { get; set; }
        public string? Url { get; set; }
        public string? Sha256 { get; set; }
        public long SizeBytes { get; set; }
        public PackageType Package { get; set; }
        public string[]? Requires { get; set; }
        public string? ArchiveRoot { get; set; }
    }
}
