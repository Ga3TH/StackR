using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using StackR.Core.Download;
using StackR.Core.Manifests;
using StackR.Core.Paths;

namespace StackR.Core.Components;

/// <summary>Avancement d'une installation ; <paramref name="Stage"/> vaut Téléchargement, Vérification, Extraction ou Terminé.</summary>
public sealed record InstallProgress(string Stage, double Percent);

/// <param name="downloader">Téléchargement vérifié (repli si aucun paquet local valide).</param>
/// <param name="paths">Arborescence StackR.</param>
/// <param name="packagesDir">
/// Dossier des paquets embarqués dans l'installateur (<c>packages\</c> à côté de l'exécutable) ;
/// par défaut celui de <see cref="Environment.ProcessPath"/>. Chaque paquet porte le nom de fichier de l'URL de la release.
/// </param>
public sealed class ComponentInstaller(SecureDownloader downloader, StackRPaths paths, string? packagesDir = null)
{
    private const int RenameAttempts = 5;
    private static readonly TimeSpan RenameBackoff = TimeSpan.FromMilliseconds(200);

    private readonly SecureDownloader _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    private readonly StackRPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly string? _packagesDir = packagesDir ?? DefaultPackagesDirectory();

    /// <summary><c>packages\</c> à côté de l'exécutable courant (installateur complet), ou null si l'exécutable est inconnu.</summary>
    public static string? DefaultPackagesDirectory() =>
        Environment.ProcessPath is { } exe && Path.GetDirectoryName(exe) is { Length: > 0 } dir ? Path.Combine(dir, "packages") : null;

    /// <summary>Le paquet de <paramref name="r"/> est-il présent dans <paramref name="packagesDir"/> (défaut : <see cref="DefaultPackagesDirectory"/>) ? Empreinte non vérifiée ici.</summary>
    public static bool IsPackagePresent(ComponentRelease r, string? packagesDir = null)
    {
        ArgumentNullException.ThrowIfNull(r);
        var name = Path.GetFileName(r.Url.AbsolutePath);
        return (packagesDir ?? DefaultPackagesDirectory()) is { } dir && !string.IsNullOrEmpty(name) && File.Exists(Path.Combine(dir, name));
    }

    /// <summary>Chemin du paquet local de <paramref name="r"/> s'il existe (son empreinte n'est pas encore vérifiée), sinon null.</summary>
    public string? LocalPackagePath(ComponentRelease r)
    {
        ArgumentNullException.ThrowIfNull(r);
        var name = Path.GetFileName(r.Url.AbsolutePath);
        if (_packagesDir is null || string.IsNullOrEmpty(name))
        {
            return null;
        }

        var path = Path.Combine(_packagesDir, name);
        return File.Exists(path) ? path : null;
    }

    /// <summary>
    /// Télécharge (empreinte vérifiée), extrait puis publie le composant. Sans effet s'il est déjà installé.
    /// Le marqueur est écrit en dernier, dans le dossier de travail, avant le renommage final.
    /// </summary>
    public async Task InstallAsync(ComponentRelease r, IProgress<InstallProgress>? p, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(r);
        ComponentStore.ValidateVersion(r.Version);

        var folder = ComponentStore.FolderName(r.Component);
        var dest = _paths.ComponentDir(folder, r.Version);
        if (File.Exists(Path.Combine(dest, ComponentStore.MarkerFileName)))
        {
            return;
        }

        Directory.CreateDirectory(_paths.Tmp);
        string? download = null;
        var downloaded = false;
        var stage = Path.Combine(_paths.Tmp, "install-" + Guid.NewGuid().ToString("N"));
        try
        {
            // Paquet embarqué : installation hors ligne si son SHA-256 correspond. Sinon (absent, tronqué, altéré)
            // on retombe sur le téléchargement vérifié : jamais d'installation d'un fichier dont l'empreinte est fausse.
            download = await VerifiedLocalPackageAsync(r, p, ct).ConfigureAwait(false);
            if (download is null)
            {
                downloaded = true;
                p?.Report(new InstallProgress("Téléchargement", 0));
                var dl = p is null ? null : new SyncProgress<DownloadProgress>(d =>
                    p.Report(new InstallProgress("Téléchargement", d.TotalBytes is > 0 ? 100.0 * d.BytesReceived / d.TotalBytes.Value : 0)));
                download = await _downloader.DownloadVerifiedAsync(r.Url, r.Sha256, _paths.Tmp, dl, ct, r.SizeBytes).ConfigureAwait(false);
            }

            p?.Report(new InstallProgress("Vérification", 100));

            // Extraction immédiate : le fichier vérifié ne reste pas disponible plus longtemps que nécessaire.
            p?.Report(new InstallProgress("Extraction", 0));
            if (r.Package == PackageType.Zip)
            {
                SafeZipExtractor.Extract(download, stage, r.ArchiveRoot);
            }
            else
            {
                Directory.CreateDirectory(stage);
                var name = Path.GetFileName(r.Url.AbsolutePath);
                if (string.IsNullOrEmpty(name))
                {
                    name = folder;
                }

                File.Copy(download, Path.Combine(stage, name));
            }

            ct.ThrowIfCancellationRequested();
            var marker = new { version = r.Version, sha256 = r.Sha256, date = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture) };
            await File.WriteAllTextAsync(Path.Combine(stage, ComponentStore.MarkerFileName), JsonSerializer.Serialize(marker), ct).ConfigureAwait(false);
            p?.Report(new InstallProgress("Extraction", 100));

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            if (!PublishedMeanwhile(dest))
            {
                RemoveLeftover(dest);
                await MoveWithRetryAsync(stage, dest, ct).ConfigureAwait(false);
            }

            p?.Report(new InstallProgress("Terminé", 100));
        }
        finally
        {
            if (downloaded)
            {
                TryDeleteFile(download); // jamais le paquet embarqué
            }

            TryDeleteDirectory(stage);
            TryDeleteDirectory(stage + ".partial");
        }
    }

    private async Task<string?> VerifiedLocalPackageAsync(ComponentRelease r, IProgress<InstallProgress>? p, CancellationToken ct)
    {
        if (LocalPackagePath(r) is not { } path)
        {
            return null;
        }

        try
        {
            p?.Report(new InstallProgress("Vérification", 0));
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81_920, useAsync: true);
            var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(stream, ct).ConfigureAwait(false));
            return string.Equals(hash, r.Sha256, StringComparison.OrdinalIgnoreCase) ? path : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>
    /// Supprime de <c>tmp\</c> les restes d'installations interrompues plus anciens que
    /// <paramref name="olderThan"/> : <c>install-*</c>, <c>*.download</c>, <c>client-*.cnf</c>, <c>init-*.sql</c>.
    /// À appeler au démarrage ; au mieux, sans jamais lever pour un fichier verrouillé.
    /// </summary>
    public void SweepStaleTemp(TimeSpan olderThan) => TempSweeper.Sweep(_paths.Tmp, olderThan);

    // Progress<T> poste sur le thread pool : on veut un ordre de rapports déterministe.
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
    }

    // Le renommage publie un dossier qui contient déjà le marqueur : un dossier sans marqueur n'est jamais
    // une installation en cours, seulement un reste (désinstallation interrompue par un fichier verrouillé).
    private static bool PublishedMeanwhile(string dest) => File.Exists(Path.Combine(dest, ComponentStore.MarkerFileName));

    private static void RemoveLeftover(string dest)
    {
        if (!Directory.Exists(dest))
        {
            return;
        }

        try
        {
            Directory.Delete(dest, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Le dossier « {dest} » existe déjà (reste d'une désinstallation inachevée) et n'a pas pu être supprimé : "
                + $"fermez les programmes qui l'utilisent puis réessayez ({e.Message}).",
                e);
        }
    }

    private static async Task MoveWithRetryAsync(string from, string to, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                Directory.Move(from, to);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && PublishedMeanwhile(to))
            {
                // Une installation concurrente de la même version a publié le dossier la première.
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException && attempt < RenameAttempts)
            {
                // Antivirus ou indexeur qui verrouille brièvement le dossier.
                await Task.Delay(RenameBackoff, ct).ConfigureAwait(false);
            }
        }
    }

    private static void TryDeleteFile(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Nettoyage au mieux.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem.
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
            // Nettoyage au mieux.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem.
        }
    }
}
