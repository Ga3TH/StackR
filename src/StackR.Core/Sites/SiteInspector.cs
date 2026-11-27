namespace StackR.Core.Sites;

/// <summary>Type de projet reconnu dans le dossier d'un site.</summary>
public enum SiteFramework
{
    /// <summary>PHP sans framework reconnu (ou dossier vide).</summary>
    Php,
    Laravel,
    Symfony,
    WordPress,

    /// <summary>Uniquement des fichiers HTML (aucun fichier PHP à la racine ni dans public).</summary>
    Static,
}

/// <summary>Détails d'un site pour l'affichage (lecture rapide : jamais de parcours récursif).</summary>
/// <param name="Framework">Type de projet reconnu.</param>
/// <param name="LastModified">Dernière modification visible (dossier et entrées de premier niveau).</param>
/// <param name="HasGit">Dépôt Git (<c>.git</c>).</param>
/// <param name="HasComposer">Projet Composer (<c>composer.json</c>).</param>
/// <param name="HasNode">Projet Node (<c>package.json</c>).</param>
public sealed record SiteDetails(SiteFramework Framework, DateTimeOffset? LastModified, bool HasGit, bool HasComposer, bool HasNode);

/// <summary>Reconnaît le type d'un projet d'après quelques fichiers caractéristiques.</summary>
public static class SiteInspector
{
    /// <summary>
    /// Laravel (<c>artisan</c>), Symfony (<c>symfony.lock</c>, ou <c>bin/console</c> avec <c>config/bundles.php</c>),
    /// WordPress (<c>wp-config.php</c>, <c>wp-login.php</c> ou <c>wp-includes</c>), sinon PHP, ou HTML s'il n'y a que du HTML.
    /// </summary>
    public static SiteFramework DetectFramework(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        if (!SafeDirectory(folder))
        {
            return SiteFramework.Php;
        }

        if (SafeFile(Path.Combine(folder, "artisan")))
        {
            return SiteFramework.Laravel;
        }

        if (SafeFile(Path.Combine(folder, "symfony.lock"))
            || (SafeFile(Path.Combine(folder, "bin", "console")) && SafeFile(Path.Combine(folder, "config", "bundles.php"))))
        {
            return SiteFramework.Symfony;
        }

        if (SafeFile(Path.Combine(folder, "wp-config.php"))
            || SafeFile(Path.Combine(folder, "wp-login.php"))
            || SafeDirectory(Path.Combine(folder, "wp-includes")))
        {
            return SiteFramework.WordPress;
        }

        var publicDir = Path.Combine(folder, "public");
        var hasPhp = HasAny(folder, "*.php") || HasAny(publicDir, "*.php");
        var hasHtml = HasAny(folder, "*.html") || HasAny(publicDir, "*.html");
        return !hasPhp && hasHtml ? SiteFramework.Static : SiteFramework.Php;
    }

    /// <summary>Détails d'un site ; un dossier illisible donne des valeurs par défaut (jamais d'exception d'E/S).</summary>
    public static SiteDetails Inspect(string folder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(folder);
        return new SiteDetails(
            DetectFramework(folder),
            LastModified(folder),
            SafeDirectory(Path.Combine(folder, ".git")) || SafeFile(Path.Combine(folder, ".git")),
            SafeFile(Path.Combine(folder, "composer.json")),
            SafeFile(Path.Combine(folder, "package.json")));
    }

    /// <summary>
    /// Date la plus récente parmi le dossier et ses entrées de premier niveau
    /// (hors <c>vendor</c>, <c>node_modules</c>, <c>.git</c>) ; null si le dossier est absent ou illisible.
    /// </summary>
    public static DateTimeOffset? LastModified(string folder)
    {
        try
        {
            var dir = new DirectoryInfo(folder);
            if (!dir.Exists)
            {
                return null;
            }

            var latest = dir.LastWriteTimeUtc;
            foreach (var entry in dir.EnumerateFileSystemInfos())
            {
                if (entry.Name is "vendor" or "node_modules" or ".git")
                {
                    continue;
                }

                if (entry.LastWriteTimeUtc > latest)
                {
                    latest = entry.LastWriteTimeUtc;
                }
            }

            return new DateTimeOffset(latest, TimeSpan.Zero);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static bool HasAny(string dir, string pattern)
    {
        try
        {
            return Directory.Exists(dir) && Directory.EnumerateFiles(dir, pattern).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return false;
        }
    }

    private static bool SafeFile(string path)
    {
        try
        {
            return File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool SafeDirectory(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }
}
