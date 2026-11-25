using System.IO.Compression;

namespace StackR.Core.Download;

/// <summary>
/// Extraction d'archives zip protégée contre le « zip slip ».
/// Tout est extrait dans <c>destDir + ".partial"</c>, puis renommé en <c>destDir</c> seulement
/// si l'extraction complète a réussi ; en cas d'échec, le dossier partiel est supprimé.
/// </summary>
public static class SafeZipExtractor
{
    private const string PartialSuffix = ".partial";

    private static readonly char[] InvalidNameChars = Path.GetInvalidFileNameChars()
        .Where(c => c is not '/' and not '\\')
        .ToArray();

    private static readonly HashSet<string> ReservedDeviceNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
        "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
        "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³",
    };

    /// <summary>
    /// Extrait <paramref name="zipPath"/> dans <paramref name="destDir"/>, qui ne doit pas exister.
    /// Si <paramref name="archiveRoot"/> est donné (comparaison insensible à la casse), seul ce
    /// sous-dossier de l'archive est extrait, son contenu étant remonté à la racine de <paramref name="destDir"/>.
    /// </summary>
    /// <exception cref="IntegrityException">Entrée dangereuse (chemin absolu, <c>..</c>, lecteur, flux NTFS,
    /// doublon…) ou <paramref name="archiveRoot"/> absent de l'archive.</exception>
    /// <exception cref="IOException"><paramref name="destDir"/> existe déjà.</exception>
    /// <exception cref="ArgumentException"><paramref name="archiveRoot"/> invalide.</exception>
    public static void Extract(string zipPath, string destDir, string? archiveRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(zipPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(destDir);

        var rootSegments = archiveRoot is null ? [] : ParseArchiveRoot(archiveRoot);

        var dest = Path.TrimEndingDirectorySeparator(Path.GetFullPath(destDir));
        if (Directory.Exists(dest) || File.Exists(dest))
        {
            throw new IOException($"Le dossier de destination « {dest} » existe déjà ; il ne sera pas écrasé.");
        }

        var partial = dest + PartialSuffix;

        using var zip = ZipFile.OpenRead(zipPath);

        // Toutes les entrées sont validées avant d'écrire quoi que ce soit sur le disque.
        var plan = BuildPlan(zip, rootSegments, archiveRoot);

        DeleteLeftover(partial);
        Directory.CreateDirectory(partial);
        try
        {
            var partialPrefix = partial + Path.DirectorySeparatorChar;
            foreach (var item in plan)
            {
                var target = Path.GetFullPath(Path.Join(partial, item.RelativePath));

                // Garde-fou final : le chemin résolu doit rester strictement sous le dossier partiel.
                if (!target.StartsWith(partialPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IntegrityException(
                        $"Archive refusée : l'entrée « {item.Entry.FullName} » sort du dossier d'extraction.");
                }

                if (item.IsDirectory)
                {
                    Directory.CreateDirectory(target);
                    continue;
                }

                if (File.Exists(target) || Directory.Exists(target))
                {
                    throw new IntegrityException(
                        $"Archive refusée : l'entrée « {item.Entry.FullName} » est en double.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                item.Entry.ExtractToFile(target, overwrite: false);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            Directory.Move(partial, dest);
        }
        catch
        {
            TryDeleteDirectory(partial);
            throw;
        }
    }

    private static List<PlannedEntry> BuildPlan(ZipArchive zip, string[] rootSegments, string? archiveRoot)
    {
        var plan = new List<PlannedEntry>();
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var directories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in zip.Entries)
        {
            // Validation de TOUTES les entrées, y compris celles hors de archiveRoot :
            // une seule entrée malveillante suffit à rejeter l'archive.
            var segments = ParseEntryName(entry.FullName);
            var isDirectory = entry.FullName.EndsWith('/') || entry.FullName.EndsWith('\\');

            if (!StartsWithSegments(segments, rootSegments))
            {
                continue;
            }

            var relative = segments[rootSegments.Length..];
            if (relative.Length == 0)
            {
                // Le dossier racine lui-même.
                continue;
            }

            var relativePath = string.Join(Path.DirectorySeparatorChar, relative);
            for (var i = 1; i < relative.Length; i++)
            {
                directories.Add(string.Join(Path.DirectorySeparatorChar, relative[..i]));
            }

            if (isDirectory)
            {
                directories.Add(relativePath);
            }
            else if (!files.Add(relativePath))
            {
                throw new IntegrityException($"Archive refusée : l'entrée « {entry.FullName} » est en double.");
            }

            plan.Add(new PlannedEntry(entry, relativePath, isDirectory));
        }

        if (files.Overlaps(directories))
        {
            throw new IntegrityException("Archive refusée : un même chemin y désigne à la fois un fichier et un dossier.");
        }

        if (rootSegments.Length > 0 && plan.Count == 0)
        {
            throw new IntegrityException($"Le dossier racine « {archiveRoot} » est absent de l'archive.");
        }

        return plan;
    }

    /// <summary>Découpe et valide un nom d'entrée ; lève <see cref="IntegrityException"/> s'il est dangereux.</summary>
    private static string[] ParseEntryName(string fullName)
    {
        if (!TryParseRelativePath(fullName, out var segments, out var reason))
        {
            throw new IntegrityException($"Archive refusée : l'entrée « {fullName} » est dangereuse ({reason}).");
        }

        return segments;
    }

    private static string[] ParseArchiveRoot(string archiveRoot)
    {
        if (!TryParseRelativePath(archiveRoot, out var segments, out var reason) || segments.Length == 0)
        {
            throw new ArgumentException(
                $"Dossier racine d'archive invalide « {archiveRoot} » ({reason ?? "vide"}).",
                nameof(archiveRoot));
        }

        return segments;
    }

    /// <summary>
    /// Accepte uniquement un chemin relatif simple : ni racine (<c>/</c>, <c>\</c>, UNC), ni lecteur (<c>C:</c>),
    /// ni flux NTFS (<c>:</c>), ni segment <c>..</c>, ni caractère interdit ou nom de périphérique Windows.
    /// </summary>
    private static bool TryParseRelativePath(string path, out string[] segments, out string? reason)
    {
        segments = [];
        reason = null;

        var normalized = path.Replace('\\', '/');
        if (normalized.Length == 0)
        {
            reason = "nom vide";
            return false;
        }

        if (normalized[0] == '/' || Path.IsPathRooted(path))
        {
            reason = "chemin absolu";
            return false;
        }

        if (normalized.Contains(':', StringComparison.Ordinal))
        {
            reason = "lecteur ou flux NTFS";
            return false;
        }

        var result = new List<string>();
        foreach (var segment in normalized.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == "..")
            {
                reason = "segment « .. »";
                return false;
            }

            if (segment.IndexOfAny(InvalidNameChars) >= 0)
            {
                reason = "caractère interdit";
                return false;
            }

            // Windows retire silencieusement points et espaces finaux (« ..  » deviendrait « .. »).
            if (segment.EndsWith('.') || segment.EndsWith(' '))
            {
                reason = "point ou espace final";
                return false;
            }

            var baseName = segment.Split('.')[0].TrimEnd(' ');
            if (ReservedDeviceNames.Contains(baseName))
            {
                reason = "nom de périphérique réservé";
                return false;
            }

            result.Add(segment);
        }

        segments = [.. result];
        return true;
    }

    private static bool StartsWithSegments(string[] segments, string[] prefix)
    {
        if (segments.Length < prefix.Length)
        {
            return false;
        }

        for (var i = 0; i < prefix.Length; i++)
        {
            if (!string.Equals(segments[i], prefix[i], StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Supprime un dossier <c>.partial</c> laissé par une tentative interrompue.</summary>
    private static void DeleteLeftover(string partial)
    {
        if (File.Exists(partial))
        {
            File.Delete(partial);
        }
        else if (Directory.Exists(partial))
        {
            // Sur Windows, la suppression récursive ne suit pas les jonctions / liens symboliques.
            Directory.Delete(partial, recursive: true);
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
            // On conserve l'exception d'origine ; le reste sera nettoyé à la prochaine tentative.
        }
        catch (UnauthorizedAccessException)
        {
            // Idem.
        }
    }

    private sealed record PlannedEntry(ZipArchiveEntry Entry, string RelativePath, bool IsDirectory);
}
