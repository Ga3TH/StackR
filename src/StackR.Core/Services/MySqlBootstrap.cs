using System.Diagnostics;
using System.Globalization;
using StackR.Core.Config;
using StackR.Core.Paths;
using StackR.Core.Security;
using StackR.Core.Settings;

namespace StackR.Core.Services;

/// <summary>
/// Première initialisation de MySQL : base vide, puis mot de passe root aléatoire
/// (stocké dans le secret <c>mysql-root</c>).
/// </summary>
public static class MySqlBootstrap
{
    internal const string SecretName = "mysql-root";

    /// <summary>
    /// Si <c>data\mysql</c> n'est pas initialisé : <c>--initialize-insecure</c>, démarrage temporaire,
    /// <c>ALTER USER 'root'@'localhost'</c>, arrêt. Tout ou rien : en cas d'échec, le dossier de données
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

        var bin = Path.Combine(mysqlDir, "bin");
        var mysqld = RequireFile(Path.Combine(bin, "mysqld.exe"));
        var mysql = RequireFile(Path.Combine(bin, "mysql.exe"));
        var mysqladmin = RequireFile(Path.Combine(bin, "mysqladmin.exe"));

        Directory.CreateDirectory(dataDir);
        var myIni = new ConfigWriter(paths, settings).WriteMyIni(mysqlDir);
        var port = settings.MySqlPort;
        Process? server = null;
        var done = false;
        try
        {
            var code = await runner.RunAsync(
                mysqld, $"--defaults-file=\"{myIni}\" --initialize-insecure --console", cancellationToken).ConfigureAwait(false);
            if (code != 0 || !Directory.Exists(Path.Combine(dataDir, "mysql")))
            {
                throw Failure($"L'initialisation de MySQL a échoué (code {code}). Voir {ProcessRunner.RunLogPathFor(paths.Logs, mysqld)}.");
            }

            server = runner.Start(MySqlService.ServerStartInfo(mysqlDir, myIni));
            try
            {
                await ManagedService.WaitUntilListeningAsync(
                    server, port, MySqlService.ServerTimeout, "MySQL", MySqlService.ServerLogFiles(paths), cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (ServiceStartException ex)
            {
                throw new InvalidOperationException(ex.Message, ex);
            }

            // Secret enregistré AVANT de changer le mot de passe : un arrêt brutal ne le perd jamais.
            var password = PasswordGenerator.Generate();
            secrets.Set(SecretName, password);

            // Alphabet du générateur : aucun caractère qui sorte des apostrophes SQL.
            var alter = MySqlClientFile.ClientSection(port, password: null)
                + "[mysql]\r\n"
                + $"execute=\"ALTER USER 'root'@'localhost' IDENTIFIED BY '{password}';\"\r\n";
            code = await MySqlClientFile.RunAsync(runner, paths, mysql, alter, "--batch", cancellationToken).ConfigureAwait(false);
            if (code != 0)
            {
                throw Failure($"Impossible de définir le mot de passe root de MySQL (code {code}).");
            }

            // L'arrêt avec le nouveau mot de passe vérifie aussi qu'il est bien en place.
            code = await MySqlService.ShutdownAsync(runner, paths, mysqladmin, port, password, cancellationToken)
                .ConfigureAwait(false);
            if (code != 0)
            {
                throw Failure($"Le nouveau mot de passe root de MySQL n'a pas pu être vérifié (code {code}).");
            }

            await ManagedService.WaitForExitAsync(server, MySqlService.ShutdownTimeout, cancellationToken).ConfigureAwait(false);
            done = true;
        }
        finally
        {
            if (server is not null)
            {
                ProcessIdentity.KillTree(server);
                server.WaitForExit(5000);
                server.Dispose();
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
