using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace StackR.Core.Download;

/// <summary>Avancement d'un téléchargement ; <paramref name="TotalBytes"/> est nul si le serveur ne donne pas la taille.</summary>
public sealed record DownloadProgress(long BytesReceived, long? TotalBytes);

/// <summary>
/// Télécharge un fichier en HTTPS uniquement et vérifie son empreinte SHA-256 au fil de l'eau.
/// Le fichier n'est rendu à l'appelant que si l'empreinte correspond ; dans tous les autres cas
/// (empreinte fausse, erreur réseau, annulation) le fichier temporaire est supprimé.
/// </summary>
public sealed class SecureDownloader(HttpClient http)
{
    private const int Sha256Length = 32;
    private const int BufferSize = 81_920;

    private static readonly ProductInfoHeaderValue UserAgent = new(
        "StackR",
        (typeof(SecureDownloader).Assembly.GetName().Version ?? new Version(0, 0, 0)).ToString(3));

    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <summary>
    /// Télécharge <paramref name="url"/> dans un fichier au nom unique de <paramref name="tmpDir"/>,
    /// vérifie son SHA-256 et renvoie le chemin du fichier vérifié.
    /// </summary>
    /// <exception cref="ArgumentException">URL non HTTPS ou empreinte mal formée (aucune requête n'est émise).</exception>
    /// <exception cref="IntegrityException">Empreinte différente ou redirection vers une URL non HTTPS.</exception>
    public async Task<string> DownloadVerifiedAsync(
        Uri url,
        string expectedSha256,
        string tmpDir,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(url);
        ArgumentNullException.ThrowIfNull(expectedSha256);
        ArgumentException.ThrowIfNullOrWhiteSpace(tmpDir);

        if (!IsHttps(url))
        {
            throw new ArgumentException($"Seules les URL https:// sont autorisées : « {url} ».", nameof(url));
        }

        var expected = TryParseSha256(expectedSha256) ?? throw new ArgumentException(
            "L'empreinte SHA-256 attendue doit comporter exactement 64 caractères hexadécimaux.",
            nameof(expectedSha256));

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        // En-tête posé sur la requête, pas sur le HttpClient partagé.
        request.Headers.UserAgent.Add(UserAgent);

        using var response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
            .ConfigureAwait(false);

        // Défense en profondeur : une redirection ne doit jamais aboutir hors HTTPS.
        var finalUri = response.RequestMessage?.RequestUri ?? url;
        if (!IsHttps(finalUri))
        {
            throw new IntegrityException(
                $"Téléchargement refusé : « {url} » a été redirigé vers une URL non HTTPS (« {finalUri} »).");
        }

        response.EnsureSuccessStatusCode();

        Directory.CreateDirectory(tmpDir);
        var path = Path.Combine(Path.GetFullPath(tmpDir), Guid.NewGuid().ToString("N") + ".download");

        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var total = response.Content.Headers.ContentLength;

            var source = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using (source.ConfigureAwait(false))
            {
                var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true);
                await using (file.ConfigureAwait(false))
                {
                    var buffer = new byte[BufferSize];
                    long received = 0;
                    int read;
                    while ((read = await source.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        hash.AppendData(buffer, 0, read);
                        await file.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        received += read;
                        progress?.Report(new DownloadProgress(received, total));
                        ct.ThrowIfCancellationRequested();
                    }

                    await file.FlushAsync(ct).ConfigureAwait(false);
                }
            }

            var actual = hash.GetHashAndReset();
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
            {
                throw new IntegrityException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Empreinte SHA-256 invalide pour « {url} » : attendue {Convert.ToHexStringLower(expected)}, obtenue {Convert.ToHexStringLower(actual)}. Le fichier a été supprimé."));
            }

            return path;
        }
        catch
        {
            TryDelete(path);
            throw;
        }
    }

    private static bool IsHttps(Uri uri) =>
        uri.IsAbsoluteUri && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);

    private static byte[]? TryParseSha256(string hex)
    {
        // Format strict : exactement 64 caractères hexadécimaux, sans espace ni préfixe.
        if (hex.Length != Sha256Length * 2 || !hex.All(char.IsAsciiHexDigit))
        {
            return null;
        }

        return Convert.FromHexString(hex);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Fichier non vérifié et jamais renvoyé : on conserve l'exception d'origine.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem.
        }
    }
}
