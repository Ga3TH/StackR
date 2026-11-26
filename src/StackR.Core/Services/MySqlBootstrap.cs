using System.Globalization;
using StackR.Core.Config;
using StackR.Core.Paths;
using StackR.Core.Security;
using StackR.Core.Settings;

namespace StackR.Core.Services;

/// <summary>
/// Première initialisation de MySQL : base vide dont le mot de passe root aléatoire
/// (secret <c>mysql-root</c>) est posé pendant l'initialisation elle-même.
/// </summary>
public static class MySqlBootstrap
{
    internal const string SecretName = "mysql-root";

    /// <summary>
    /// Si <c>data\mysql</c> n'est pas initialisé : <c>--initialize-insecure --init-file=…</c>, le fichier
    /// (réservé à l'utilisateur, supprimé ensuite) contenant <c>ALTER USER 'root'@'localhost'</c>. Tout ou rien : en cas d'échec, le dossier de données
    /// (vide au départ) est vidé pour qu'une nouvelle tentative reparte de zéro.
    /// </summary>
    /// <exception cref="InvalidOperationException">Échec, avec un message destiné à l'utilisateur.</exception>
    public static async Task EnsureInitializedAsync(
        StackRPaths paths,
        StackRSettings settings,
        IProcessRunner runner,
        SecretStore secrets,
        string mysqlDir,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentException.ThrowIfNullOrWhiteSpace(mysqlDir);

        var dataDir = paths.MySqlData;
        if (Directory.Exists(Path.Combine(dataDir, "mysql")))
        {
            return;
        }

        if (Directory.Exists(dataDir) && Directory.EnumerateFileSystemEntries(dataDir).Any())
        {
            throw new InvalidOperationException(
                $"Le dossier de données MySQL « {dataDir} » n'est pas vide mais ne contient pas de base initialisée : "
                + "StackR ne l'initialise pas pour ne rien écraser.");
        }

        var mysqld = RequireFile(Path.Combine(mysqlDir, "bin", "mysqld.exe"));

        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(paths.Tmp);
        var myIni = new ConfigWriter(paths, settings).WriteMyIni(mysqlDir);
        var initFile = Path.Combine(paths.Tmp, "init-" + Guid.NewGuid().ToString("N") + ".sql");
        var done = false;
        try
        {
            // Secret enregistré AVANT l'initialisation : un arrêt brutal ne le perd jamais.
            var password = PasswordGenerator.Generate();
            secrets.Set(SecretName, password);

            // Le mot de passe est posé pendant l'initialisation par --init-file : root n'est jamais
            // joignable sans mot de passe (pas de serveur temporaire). Alphabet du générateur :
            // aucun caractère qui sorte des apostrophes SQL.
            MySqlClientFile.WriteUserOnly(
                initFile, $"ALTER USER 'root'@'localhost' IDENTIFIED BY '{password}';\r\n");

            var code = await runner.RunAsync(
                mysqld,
                $"--defaults-file=\"{AnsiPath.For(myIni)}\" --initialize-insecure --init-file=\"{AnsiPath.For(initFile)}\" --console",
                cancellationToken).ConfigureAwait(false);
            if (code != 0 || !Directory.Exists(Path.Combine(dataDir, "mysql")))
            {
                throw Failure($"L'initialisation de MySQL a échoué (code {code}). Voir {ProcessRunner.RunLogPathFor(paths.Logs, mysqld)}.");
            }

            done = true;
        }
        finally
        {
            try
            {
                File.Delete(initFile);
            }
            catch (IOException)
            {
                // Reste réservé à l'utilisateur courant ; balayé plus tard.
            }
            catch (UnauthorizedAccessException)
            {
            }

            if (!done)
            {
                Clear(dataDir);
            }
        }
    }

    private static InvalidOperationException Failure(string message) => new(message);

    private static string RequireFile(string path) =>
        File.Exists(path)
            ? path
            : throw new InvalidOperationException(string.Create(
                CultureInfo.InvariantCulture, $"MySQL est incomplet : {Path.GetFileName(path)} introuvable dans {Path.GetDirectoryName(path)}."));

    // Le dossier était vide avant l'initialisation : tout son contenu vient de cette tentative.
    private static void Clear(string dataDir)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(dataDir).ToList())
        {
            try
            {
                if (Directory.Exists(entry))
                {
                    Directory.Delete(entry, recursive: true);
                }
                else
                {
                    File.Delete(entry);
                }
            }
            catch (IOException)
            {
                // Meilleur effort : un fichier encore verrouillé sera signalé au prochain démarrage.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
