using System.Diagnostics;
using StackR.Core.Paths;

namespace StackR.Core.Services;

/// <summary>
/// Ouverture du client <c>mysql.exe</c> dans un terminal, connecté en root sans mot de passe sur la ligne de
/// commande : un fichier d'options temporaire (réservé à l'utilisateur) le porte et doit être supprimé par l'appelant.
/// </summary>
public static class MySqlTerminal
{
    /// <summary>Prépare le lancement ; <paramref name="optionsFile"/> est à supprimer peu après le démarrage du client.</summary>
    /// <param name="windowsTerminal">Chemin de <c>wt.exe</c> s'il existe, sinon null (console classique).</param>
    public static ProcessStartInfo CreateStartInfo(
        StackRPaths paths, string mysqlExe, int port, string password, string? windowsTerminal, out string optionsFile)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentException.ThrowIfNullOrWhiteSpace(mysqlExe);
        ArgumentNullException.ThrowIfNull(password);

        Directory.CreateDirectory(paths.Tmp);
        optionsFile = Path.Combine(paths.Tmp, "client-" + Guid.NewGuid().ToString("N") + ".cnf");
        MySqlClientFile.WriteUserOnly(optionsFile, MySqlClientFile.ClientSection(port, password));

        var defaults = "--defaults-extra-file=" + optionsFile;
        ProcessStartInfo psi;
        if (windowsTerminal is not null)
        {
            psi = new ProcessStartInfo(windowsTerminal) { UseShellExecute = false };
            psi.ArgumentList.Add(mysqlExe);
            psi.ArgumentList.Add(defaults);
        }
        else
        {
            // Console classique : mysql.exe ouvre sa propre fenêtre.
            psi = new ProcessStartInfo(mysqlExe) { UseShellExecute = true };
            psi.ArgumentList.Add(defaults);
        }

        psi.WorkingDirectory = Path.GetDirectoryName(mysqlExe)!;
        return psi;
    }
}
