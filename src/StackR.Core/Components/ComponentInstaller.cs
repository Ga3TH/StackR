using System.Globalization;
using System.Text.Json;
using StackR.Core.Download;
using StackR.Core.Manifests;
using StackR.Core.Paths;

namespace StackR.Core.Components;

/// <summary>Avancement d'une installation ; <paramref name="Stage"/> vaut Téléchargement, Vérification, Extraction ou Terminé.</summary>
public sealed record InstallProgress(string Stage, double Percent);

public sealed class ComponentInstaller(SecureDownloader downloader, StackRPaths paths)
{
    private const int RenameAttempts = 5;
    private static readonly TimeSpan RenameBackoff = TimeSpan.FromMilliseconds(200);

    private readonly SecureDownloader _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
    private readonly StackRPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));

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
        var stage = Path.Combine(_paths.Tmp, "install-" + Guid.NewGuid().ToString("N"));
        try
        {
            p?.Report(new InstallProgress("Téléchargement", 0));
            var dl = p is null ? null : new SyncProgress<DownloadProgress>(d =>
                p.Report(new InstallProgress("Téléchargement", d.TotalBytes is > 0 ? 100.0 * d.BytesReceived / d.TotalBytes.Value : 0)));
            download = await _downloader.DownloadVerifiedAsync(r.Url, r.Sha256, _paths.Tmp, dl, ct, r.SizeBytes).ConfigureAwait(false);
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
            await MoveWithRetryAsync(stage, dest, ct).ConfigureAwait(false);
            p?.Report(new InstallProgress("Terminé", 100));
        }
        finally
        {
            TryDeleteFile(download);
            TryDeleteDirectory(stage);
            TryDeleteDirectory(stage + ".partial");
        }
    }

    // Progress<T> poste sur le thread pool : on veut un ordre de rapports déterministe.
    private sealed class SyncProgress<T>(Action<T> handler) : IProgress<T>
    {
        public void Report(T value) => handler(value);
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
