namespace StackR.Core.Sites;

/// <summary>
/// Retrait d'un site du dossier www : seulement un sous-dossier direct et réel de www (jamais www lui-même,
/// un lien, une jonction ni un chemin extérieur), envoyé à la Corbeille pour pouvoir être restauré.
/// </summary>
public static class SiteRemoval
{
    /// <summary>Message d'erreur si <paramref name="folder"/> ne peut pas être retiré, sinon null.</summary>
    public static string? Validate(string www, string folder)
    {
        if (string.IsNullOrWhiteSpace(www) || string.IsNullOrWhiteSpace(folder))
        {
            return "Dossier invalide.";
        }

        string wwwFull;
        string target;
        try
        {
            wwwFull = Path.TrimEndingDirectorySeparator(Path.GetFullPath(www));
            target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(folder));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return "Dossier invalide.";
        }

        var parent = Path.GetDirectoryName(target);
        if (parent is null || !string.Equals(parent, wwwFull, StringComparison.OrdinalIgnoreCase))
        {
            return $"« {folder} » n'est pas un dossier de site du dossier www.";
        }

        try
        {
            var info = new DirectoryInfo(target);
            if (!info.Exists)
            {
                return $"Le dossier « {info.Name} » n'existe plus.";
            }

            if (info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                return $"« {info.Name} » est un lien ou une jonction : supprimez-le depuis l'explorateur.";
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return ex.Message;
        }

        return null;
    }

    /// <summary>Envoie le dossier du site à la Corbeille (après <see cref="Validate"/>).</summary>
    /// <exception cref="InvalidOperationException">Dossier refusé par <see cref="Validate"/>.</exception>
    /// <exception cref="IOException">Échec ou annulation de l'envoi (fichier ouvert…).</exception>
    public static void MoveToRecycleBin(string www, string folder)
    {
        if (Validate(www, folder) is { } problem)
        {
            throw new InvalidOperationException(problem);
        }

        try
        {
            Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                Path.GetFullPath(folder),
                Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin,
                Microsoft.VisualBasic.FileIO.UICancelOption.ThrowException);
        }
        catch (OperationCanceledException ex)
        {
            throw new IOException("Envoi à la Corbeille annulé.", ex);
        }
    }
}
