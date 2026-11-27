using System.Globalization;
using System.Net.Http.Headers;
using System.Text.Json;

namespace StackR.Core.Updates;

/// <summary>
/// Cherche la dernière release de StackR sur GitHub. Ne lève jamais d'exception vers l'interface :
/// toute erreur (réseau, HTTP, JSON) donne <see cref="UpdateCheckStatus.Failed"/>.
/// </summary>
public sealed class UpdateChecker(HttpClient http, Version current, Uri? endpoint = null, TimeSpan? timeout = null)
{
    public static readonly Uri DefaultEndpoint = new("https://api.github.com/repos/Ga3TH/StackR/releases/latest");

    private const long MaxResponseBytes = 2_000_000;
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);

    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly Version _current = Normalize(current ?? throw new ArgumentNullException(nameof(current)));
    private readonly Uri _endpoint = endpoint ?? DefaultEndpoint;
    private readonly TimeSpan _timeout = timeout ?? DefaultTimeout;

    private static UpdateCheckResult Failed => new(UpdateCheckStatus.Failed, null);

    /// <summary>Version de l'assemblage donné, au format X.Y.Z.</summary>
    public static Version RunningVersion(System.Reflection.Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        return Normalize(assembly.GetName().Version ?? new Version(0, 0, 0));
    }

    /// <summary>Version plus récente, ou nul (à jour, ou échec de la recherche).</summary>
    public async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default) =>
        (await CheckDetailedAsync(ct).ConfigureAwait(false)).Info;

    public async Task<UpdateCheckResult> CheckDetailedAsync(CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(_timeout);

            using var request = new HttpRequestMessage(HttpMethod.Get, _endpoint);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue("StackR", _current.ToString(3)));
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaxResponseBytes)
            {
                return Failed;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);
            return Parse(json, _current);
        }
#pragma warning disable CA1031 // Contrat : la recherche ne lève jamais d'exception vers l'interface.
        catch (Exception)
#pragma warning restore CA1031
        {
            return Failed;
        }
    }

    /// <summary>Analyse la réponse de <c>releases/latest</c> pour la version <paramref name="current"/>.</summary>
    public static UpdateCheckResult Parse(string json, Version current)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentNullException.ThrowIfNull(current);
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return Failed;
            }

            // Brouillons et préversions : jamais proposés.
            if (GetBool(root, "draft") || GetBool(root, "prerelease"))
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null);
            }

            if (!TryParseTag(GetString(root, "tag_name"), out var version))
            {
                return Failed;
            }

            if (!IsNewer(version, current))
            {
                return new UpdateCheckResult(UpdateCheckStatus.UpToDate, null);
            }

            var html = Uri.TryCreate(GetString(root, "html_url"), UriKind.Absolute, out var h) && h.Scheme == Uri.UriSchemeHttps
                ? h
                : new Uri(string.Create(CultureInfo.InvariantCulture, $"https://github.com/Ga3TH/StackR/releases/tag/v{version.ToString(3)}"));

            var (url, sha, size) = FindInstaller(root, version);
            return new UpdateCheckResult(
                UpdateCheckStatus.Available,
                new UpdateInfo(version, GetString(root, "body") ?? "", url, sha, size, html));
        }
        catch (JsonException)
        {
            return Failed;
        }
    }

    /// <summary>Accepte « v0.1.4 » ou « 0.1.4 » (trois nombres) ; refuse tout suffixe (-beta...).</summary>
    public static bool TryParseTag(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (string.IsNullOrWhiteSpace(tag))
        {
            return false;
        }

        var text = tag.Trim();
        if (text.StartsWith('v') || text.StartsWith('V'))
        {
            text = text[1..];
        }

        var parts = text.Split('.');
        if (parts.Length != 3 || !parts.All(p => p.Length is > 0 and <= 5 && p.All(char.IsAsciiDigit)))
        {
            return false;
        }

        version = new Version(
            int.Parse(parts[0], CultureInfo.InvariantCulture),
            int.Parse(parts[1], CultureInfo.InvariantCulture),
            int.Parse(parts[2], CultureInfo.InvariantCulture));
        return true;
    }

    public static bool IsNewer(Version candidate, Version current) =>
        Normalize(candidate) > Normalize(current);

    private static Version Normalize(Version v) => new(Math.Max(v.Major, 0), Math.Max(v.Minor, 0), Math.Max(v.Build, 0));

    // Sans empreinte, URL https ou taille : pas d'installation (on propose « Voir la release »).
    private static (Uri? Url, string? Sha, long Size) FindInstaller(JsonElement root, Version version)
    {
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return (null, null, 0);
        }

        var wanted = string.Create(CultureInfo.InvariantCulture, $"StackR-Setup-{version.ToString(3)}.exe");
        foreach (var asset in assets.EnumerateArray())
        {
            if (asset.ValueKind != JsonValueKind.Object
                || !string.Equals(GetString(asset, "name"), wanted, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var ok = Uri.TryCreate(GetString(asset, "browser_download_url"), UriKind.Absolute, out var url)
                && url.Scheme == Uri.UriSchemeHttps;
            var sha = ParseDigest(GetString(asset, "digest"));
            long size = 0;
            if (asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt64(out var n))
            {
                size = n;
            }

            return ok && sha is not null && size > 0 ? (url, sha, size) : (null, null, 0);
        }

        return (null, null, 0);
    }

    private static string? ParseDigest(string? digest)
    {
        const string Prefix = "sha256:";
        if (digest is null || !digest.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var hex = digest[Prefix.Length..];
        return hex.Length == 64 && hex.All(char.IsAsciiHexDigit) ? hex.ToLowerInvariant() : null;
    }

    private static string? GetString(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool GetBool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.True;
}
