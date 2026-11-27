using StackR.Core.Download;

namespace StackR.Core.Updates;

/// <summary>Détection du mode d'installation et téléchargement vérifié de l'installateur.</summary>
public static class UpdateInstallation
{
    /// <summary>Nom du désinstalleur Inno Setup, présent à côté de l'exécutable d'une copie installée.</summary>
    public const string UninstallerName = "unins000.exe";

    /// <summary>Arguments de l'installateur : silencieux, sans boîte de dialogue, sans redémarrage, ferme StackR.</summary>
    public const string InstallerArguments = "/SILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS";

    /// <summary>Vrai pour une copie installée par l'installateur (désinstalleur voisin) ; faux pour une copie portable.</summary>
    public static bool IsInstalledCopy(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        var dir = Path.GetDirectoryName(Path.GetFullPath(processPath));
        return dir is not null && File.Exists(Path.Combine(dir, UninstallerName));
    }

    /// <summary>Dossier de travail : <c>%TEMP%\StackR-update</c>.</summary>
    public static string DefaultWorkDir() => Path.Combine(Path.GetTempPath(), "StackR-update");

    /// <summary>
    /// Télécharge l'installateur (HTTPS, SHA-256 vérifié, taille plafonnée à celle annoncée) dans
    /// <paramref name="workDir"/> et renvoie le chemin de <c>StackR-Setup-X.Y.Z.exe</c>.
    /// </summary>
    public static async Task<string> DownloadInstallerAsync(
        SecureDownloader downloader,
        UpdateInfo info,
        string workDir,
        IProgress<DownloadProgress>? progress,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(downloader);
        ArgumentNullException.ThrowIfNull(info);
        if (!info.CanInstall)
        {
            throw new InvalidOperationException("Cette release n'a pas d'installateur vérifiable.");
        }

        if (Directory.Exists(workDir))
        {
            foreach (var old in Directory.EnumerateFiles(workDir))
            {
                try
                {
                    File.Delete(old);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Reste d'une ancienne mise à jour encore verrouillé : sans importance.
                }
            }
        }

        var tmp = await downloader.DownloadVerifiedAsync(
            info.InstallerUrl!, info.Sha256!, workDir, progress, ct, info.SizeBytes).ConfigureAwait(false);
        var final = Path.Combine(workDir, $"StackR-Setup-{info.Version.ToString(3)}.exe");
        File.Move(tmp, final, overwrite: true);
        return final;
    }
}
