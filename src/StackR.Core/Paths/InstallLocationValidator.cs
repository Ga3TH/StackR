using System.Buffers;

namespace StackR.Core.Paths;

/// <summary>Raison pour laquelle un dossier proposé (racine ou www) est refusé.</summary>
public enum LocationProblem
{
    None,
    Empty,
    NotAbsolute,
    NetworkPath,
    InvalidCharacters,
    DriveRoot,
    ProtectedFolder,
    LaragonFolder,
    IsFile,
    NotWritable,
    NotReadable,
    WwwIsRoot,
    WwwContainsRoot,
    WwwInsideInternalFolder,
}

/// <summary>Résultat d'une vérification : chemin normalisé (si analysable) et détail technique éventuel.</summary>
public sealed record LocationCheck(LocationProblem Problem, string? FullPath, string? Detail = null)
{
    public bool IsValid => Problem == LocationProblem.None;
}

/// <summary>Dossiers interdits : protégés par Windows (Program Files, Windows) et installation Laragon.</summary>
public sealed record LocationRules(IReadOnlyList<string> ProtectedFolders, string? LaragonRoot)
{
    /// <summary>Program Files (x64 et x86), dossier Windows et <c>C:\laragon</c>.</summary>
    public static LocationRules Default { get; } = new(DefaultProtectedFolders(), @"C:\laragon");

    private static string[] DefaultProtectedFolders() =>
        [.. new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetEnvironmentVariable("ProgramW6432") ?? "",
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
}

/// <summary>
/// Validation des dossiers choisis à l'assistant (racine d'installation et dossier www). Espaces et accents
/// sont acceptés. Les contrôles d'accès (<paramref name="checkAccess"/>) ne laissent aucune trace : un fichier
/// temporaire est créé puis supprimé, et les dossiers créés pour le test sont retirés.
/// </summary>
public static class InstallLocationValidator
{
    // Caractères interdits dans un nom Windows (':' compris : flux NTFS), en plus des caractères de contrôle.
    private static readonly SearchValues<char> ForbiddenChars =
        SearchValues.Create([.. Path.GetInvalidPathChars(), '<', '>', '"', '|', '?', '*', ':']);

    /// <summary>Vérifie le dossier d'installation : absolu, local, hors dossiers protégés et Laragon, accessible en écriture.</summary>
    public static LocationCheck ValidateRoot(string? path, LocationRules rules, bool checkAccess)
    {
        ArgumentNullException.ThrowIfNull(rules);
        var syntax = Normalize(path);
        if (!syntax.IsValid)
        {
            return syntax;
        }

        var full = syntax.FullPath!;
        if (rules.ProtectedFolders.Any(p => IsSameOrUnder(full, p)))
        {
            return new LocationCheck(LocationProblem.ProtectedFolder, full);
        }

        if (!string.IsNullOrWhiteSpace(rules.LaragonRoot) && IsSameOrUnder(full, rules.LaragonRoot))
        {
            return new LocationCheck(LocationProblem.LaragonFolder, full);
        }

        if (File.Exists(full))
        {
            return new LocationCheck(LocationProblem.IsFile, full);
        }

        return checkAccess ? CheckWritable(full) : syntax;
    }

    /// <summary>
    /// Vérifie le dossier www par rapport à la racine <paramref name="root"/> (déjà normalisée) : il ne doit ni être
    /// la racine, ni la contenir, ni se trouver dans un dossier interne (bin, etc, data, logs, tmp), car chaque
    /// sous-dossier de www devient un site servi par Apache. Un dossier www existant est seulement lu (projets
    /// Laragon compris) ; s'il n'existe pas encore, on vérifie qu'il peut être créé.
    /// </summary>
    public static LocationCheck ValidateWww(string? path, string root, LocationRules rules, bool checkAccess)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentNullException.ThrowIfNull(rules);
        var syntax = Normalize(path);
        if (!syntax.IsValid)
        {
            return syntax;
        }

        var full = syntax.FullPath!;
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            return new LocationCheck(LocationProblem.WwwIsRoot, full);
        }

        if (IsSameOrUnder(root, full))
        {
            return new LocationCheck(LocationProblem.WwwContainsRoot, full);
        }

        var paths = new StackRPaths(root);
        if (new[] { paths.Bin, paths.Etc, paths.Data, paths.Logs, paths.Tmp }.Any(d => IsSameOrUnder(full, d)))
        {
            return new LocationCheck(LocationProblem.WwwInsideInternalFolder, full);
        }

        if (rules.ProtectedFolders.Any(p => IsSameOrUnder(full, p)))
        {
            return new LocationCheck(LocationProblem.ProtectedFolder, full);
        }

        if (File.Exists(full))
        {
            return new LocationCheck(LocationProblem.IsFile, full);
        }

        if (!checkAccess)
        {
            return syntax;
        }

        return Directory.Exists(full) ? CheckReadable(full) : CheckWritable(full);
    }

    /// <summary>Analyse syntaxique seule (aucun accès disque) : chemin absolu local, sans caractère interdit.</summary>
    public static LocationCheck Normalize(string? path)
    {
        var trimmed = (path ?? "").Trim();
        // « Copier en tant que chemin » de l'Explorateur ajoute des guillemets.
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1].Trim();
        }

        if (trimmed.Length == 0)
        {
            return new LocationCheck(LocationProblem.Empty, null);
        }

        trimmed = trimmed.Replace('/', '\\');
        if (trimmed.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return new LocationCheck(LocationProblem.NetworkPath, null);
        }

        if (!Path.IsPathFullyQualified(trimmed) || trimmed.Length < 3 || !char.IsAsciiLetter(trimmed[0]) || trimmed[1] != ':')
        {
            return new LocationCheck(LocationProblem.NotAbsolute, null);
        }

        var rest = trimmed[2..];
        if (rest.AsSpan().ContainsAny(ForbiddenChars))
        {
            return new LocationCheck(LocationProblem.InvalidCharacters, null);
        }

        // Windows ignore les espaces et points en fin de nom : on refuse plutôt que de viser un autre dossier.
        if (rest.Split('\\', StringSplitOptions.RemoveEmptyEntries)
            .Any(s => s is not ("." or "..") && (s.EndsWith(' ') || s.EndsWith('.'))))
        {
            return new LocationCheck(LocationProblem.InvalidCharacters, null);
        }

        string full;
        try
        {
            full = Path.GetFullPath(trimmed);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return new LocationCheck(LocationProblem.InvalidCharacters, null, ex.Message);
        }

        full = Path.TrimEndingDirectorySeparator(full);
        if (full.Length <= 3)
        {
            return new LocationCheck(LocationProblem.DriveRoot, full.Length == 2 ? full + "\\" : full);
        }

        return new LocationCheck(LocationProblem.None, full);
    }

    /// <summary>Vrai si <paramref name="path"/> est <paramref name="parent"/> ou l'un de ses sous-dossiers.</summary>
    public static bool IsSameOrUnder(string path, string parent)
    {
        ArgumentNullException.ThrowIfNull(path);
        ArgumentNullException.ThrowIfNull(parent);
        var p = Path.TrimEndingDirectorySeparator(path);
        var root = Path.TrimEndingDirectorySeparator(parent);
        if (root.Length == 0)
        {
            return false;
        }

        return string.Equals(p, root, StringComparison.OrdinalIgnoreCase)
            || p.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase);
    }

    private static LocationCheck CheckWritable(string full)
    {
        // Dossiers à créer pour le test (du plus haut au plus profond), retirés ensuite.
        var created = new List<string>();
        for (var dir = full; dir is not null && !Directory.Exists(dir); dir = Path.GetDirectoryName(dir))
        {
            created.Insert(0, dir);
        }

        try
        {
            foreach (var dir in created)
            {
                Directory.CreateDirectory(dir);
            }

            var probe = Path.Combine(full, ".stackr-write-test-" + Guid.NewGuid().ToString("N") + ".tmp");
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose))
            {
                // Le fichier est supprimé à la fermeture.
            }

            return new LocationCheck(LocationProblem.None, full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new LocationCheck(LocationProblem.NotWritable, full, ex.Message);
        }
        finally
        {
            for (var i = created.Count - 1; i >= 0; i--)
            {
                TryDeleteEmptyDirectory(created[i]);
            }
        }
    }

    private static LocationCheck CheckReadable(string full)
    {
        try
        {
            using var entries = Directory.EnumerateFileSystemEntries(full).GetEnumerator();
            _ = entries.MoveNext();
            return new LocationCheck(LocationProblem.None, full);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new LocationCheck(LocationProblem.NotReadable, full, ex.Message);
        }
    }

    private static void TryDeleteEmptyDirectory(string dir)
    {
        try
        {
            if (Directory.Exists(dir) && !Directory.EnumerateFileSystemEntries(dir).Any())
            {
                Directory.Delete(dir);
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
