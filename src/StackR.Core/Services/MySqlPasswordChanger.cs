using System.Text.RegularExpressions;
using StackR.Core.Paths;
using StackR.Core.Security;

namespace StackR.Core.Services;

/// <summary>
/// Changement du mot de passe root de MySQL : <c>ALTER USER</c> exécuté par <c>mysql.exe</c> via un fichier
/// d'options temporaire (réservé à l'utilisateur, supprimé ensuite) ; ni l'ancien ni le nouveau mot de passe
/// n'apparaissent sur une ligne de commande. Le secret DPAPI n'est mis à jour qu'après la réussite.
/// </summary>
public static partial class MySqlPasswordChanger
{
    public const int MinLength = 8;
    public const int MaxLength = 128;

    // Caractères sûrs à la fois dans une chaîne SQL entre apostrophes, dans un fichier d'options (entre
    // guillemets) et dans un .env : pas d'apostrophe, de guillemet, d'antislash, de # ni d'espace.
    [GeneratedRegex(@"^[A-Za-z0-9._\-!@$%^&*+~]+$")]
    private static partial Regex AllowedChars();

    /// <summary>Message d'erreur (français) si le mot de passe n'est pas acceptable, sinon null.</summary>
    public static string? Validate(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < MinLength)
        {
            return $"Le mot de passe doit faire au moins {MinLength} caractères.";
        }

        if (password.Length > MaxLength)
        {
            return $"Le mot de passe doit faire au plus {MaxLength} caractères.";
        }

        return AllowedChars().IsMatch(password)
            ? null
            : "Caractères autorisés : lettres, chiffres et . _ - ! @ $ % ^ & * + ~ (ni espace, ni apostrophe, ni guillemet, ni #, ni antislash).";
    }

    /// <summary>Génère un mot de passe fort (24 caractères) accepté par <see cref="Validate"/>.</summary>
    public static string Generate() => PasswordGenerator.Generate();

    /// <summary>Contenu du fichier d'options : connexion avec l'ancien mot de passe + ALTER USER à la connexion.</summary>
    internal static string BuildOptions(int port, string current, string next) =>
        MySqlClientFile.ClientSection(port, current)
        + "[mysql]\r\n"
        + $"init-command=\"ALTER USER 'root'@'localhost' IDENTIFIED BY '{next}'\"\r\n";

    /// <exception cref="ArgumentException">Nouveau mot de passe refusé par <see cref="Validate"/>.</exception>
    /// <exception cref="InvalidOperationException">Secret absent, mysql.exe introuvable, ou échec de la commande (MySQL arrêté, mot de passe actuel refusé…).</exception>
    public static async Task ChangeRootPasswordAsync(
        IProcessRunner runner,
        StackRPaths paths,
        SecretStore secrets,
        string mysqlDir,
        int port,
        string newPassword,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(secrets);
        ArgumentException.ThrowIfNullOrWhiteSpace(mysqlDir);
        if (Validate(newPassword) is { } problem)
        {
            throw new ArgumentException(problem, nameof(newPassword));
        }

        var current = secrets.Get(MySqlBootstrap.SecretName)
            ?? throw new InvalidOperationException("MySQL n'a pas encore été initialisé : aucun mot de passe root enregistré.");
        var mysql = Path.Combine(mysqlDir, "bin", "mysql.exe");
        if (!File.Exists(mysql))
        {
            throw new InvalidOperationException($"mysql.exe introuvable dans {Path.GetDirectoryName(mysql)}.");
        }

        // L'ALTER USER est passé par init-command (fichier d'options) : exécuté dès la connexion.
        var code = await MySqlClientFile.RunAsync(
            runner, paths, mysql, BuildOptions(port, current, newPassword), "--batch --skip-column-names --execute=\"SELECT 1\"",
            cancellationToken).ConfigureAwait(false);
        if (code != 0)
        {
            throw new InvalidOperationException(
                $"Le changement du mot de passe a échoué (code {code}) : MySQL est-il démarré et le mot de passe actuel valide ?");
        }

        try
        {
            secrets.Set(MySqlBootstrap.SecretName, newPassword);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException(
                "Le mot de passe a été changé dans MySQL mais n'a pas pu être enregistré : " + ex.Message, ex);
        }
    }
}
