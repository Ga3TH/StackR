using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using StackR.Core.Paths;

namespace StackR.Core.Services;

/// <summary>Une base MySQL : nom, taille (données + index), nombre de tables, base système ou non.</summary>
public sealed record DatabaseInfo(string Name, long SizeBytes, int Tables, bool IsSystem);

/// <summary>
/// Liste et création des bases MySQL par le client <c>mysql.exe</c>. Le mot de passe root passe par un
/// fichier d'options temporaire réservé à l'utilisateur (<see cref="MySqlClientFile"/>), jamais par la ligne de commande.
/// </summary>
public static partial class MySqlDatabases
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);
    private static readonly string[] SystemSchemas = ["information_schema", "mysql", "performance_schema", "sys"];

    /// <summary>Une ligne par base : nom, octets, tables (bases vides comprises).</summary>
    internal const string ListQuery =
        "SELECT s.SCHEMA_NAME, COALESCE(SUM(t.DATA_LENGTH + t.INDEX_LENGTH), 0), COUNT(t.TABLE_NAME) "
        + "FROM information_schema.SCHEMATA s LEFT JOIN information_schema.TABLES t ON t.TABLE_SCHEMA = s.SCHEMA_NAME "
        + "GROUP BY s.SCHEMA_NAME ORDER BY s.SCHEMA_NAME";

    public static bool IsSystem(string name) => SystemSchemas.Contains(name, StringComparer.OrdinalIgnoreCase);

    /// <summary>Message si le nom est refusé (lettres, chiffres, _ ; 64 caractères ; pas seulement des chiffres ; pas une base système), sinon null.</summary>
    public static string? ValidateName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Choisissez un nom de base.";
        }

        if (!NameRegex().IsMatch(name))
        {
            return "Utilisez seulement des lettres sans accent, des chiffres et _ (64 caractères au plus).";
        }

        if (name.All(char.IsAsciiDigit))
        {
            return "Le nom ne peut pas être composé uniquement de chiffres.";
        }

        return IsSystem(name) ? $"« {name} » est une base système de MySQL." : null;
    }

    /// <summary>Instruction de création en utf8mb4 (nom validé par <see cref="ValidateName"/>).</summary>
    internal static string CreateStatement(string name)
    {
        if (ValidateName(name) is { } problem)
        {
            throw new ArgumentException(problem, nameof(name));
        }

        return $"CREATE DATABASE `{name}` CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci";
    }

    /// <summary>Lit la sortie <c>--batch --skip-column-names</c> de <see cref="ListQuery"/> (colonnes séparées par des tabulations).</summary>
    internal static IReadOnlyList<DatabaseInfo> Parse(string output)
    {
        var result = new List<DatabaseInfo>();
        foreach (var line in (output ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var cols = line.Split('\t');
            if (cols.Length < 3 || cols[0].Length == 0)
            {
                continue;
            }

            _ = decimal.TryParse(cols[1], NumberStyles.Number, CultureInfo.InvariantCulture, out var size);
            _ = int.TryParse(cols[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var tables);
            result.Add(new DatabaseInfo(cols[0], (long)size, tables, IsSystem(cols[0])));
        }

        return result;
    }

    /// <summary>Bases de l'instance (MySQL doit tourner).</summary>
    /// <exception cref="InvalidOperationException">Client introuvable ou requête refusée (le message de MySQL est repris).</exception>
    public static async Task<IReadOnlyList<DatabaseInfo>> ListAsync(
        StackRPaths paths, string mysqlDir, int port, string password, CancellationToken ct)
    {
        var output = await RunAsync(paths, mysqlDir, port, password, ListQuery, ct).ConfigureAwait(false);
        return Parse(output);
    }

    /// <summary>Crée une base en utf8mb4 / utf8mb4_unicode_ci.</summary>
    /// <exception cref="ArgumentException">Nom refusé par <see cref="ValidateName"/>.</exception>
    /// <exception cref="InvalidOperationException">Échec de MySQL (base existante…).</exception>
    public static async Task CreateAsync(
        StackRPaths paths, string mysqlDir, int port, string password, string name, CancellationToken ct)
    {
        var sql = CreateStatement(name);
        await RunAsync(paths, mysqlDir, port, password, sql, ct).ConfigureAwait(false);
    }

    private static async Task<string> RunAsync(
        StackRPaths paths, string mysqlDir, int port, string password, string sql, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(paths);
        var exe = Path.Combine(mysqlDir, "bin", "mysql.exe");
        if (!File.Exists(exe))
        {
            throw new InvalidOperationException($"mysql.exe introuvable dans {Path.GetDirectoryName(exe)}.");
        }

        Directory.CreateDirectory(paths.Tmp);
        var file = Path.Combine(paths.Tmp, "client-" + Guid.NewGuid().ToString("N") + ".cnf");
        try
        {
            MySqlClientFile.WriteUserOnly(file, MySqlClientFile.ClientSection(port, password));
            var psi = new ProcessStartInfo(exe)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            // --defaults-extra-file doit être le premier argument.
            psi.ArgumentList.Add("--defaults-extra-file=" + Config.AnsiPath.For(file));
            psi.ArgumentList.Add("--default-character-set=utf8mb4");
            psi.ArgumentList.Add("--batch");
            psi.ArgumentList.Add("--skip-column-names");
            psi.ArgumentList.Add("--execute=" + sql);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(Timeout);
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("Impossible de lancer mysql.exe.");
            var stdout = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var stderr = process.StandardError.ReadToEndAsync(timeout.Token);
            try
            {
                await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ProcessIdentity.KillTree(process);
                throw;
            }

            var output = await stdout.ConfigureAwait(false);
            var error = (await stderr.ConfigureAwait(false)).Trim();
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(error.Length > 0 ? error : $"mysql.exe a échoué (code {process.ExitCode}).");
            }

            return output;
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException("Impossible de lancer mysql.exe : " + ex.Message, ex);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Reste réservé à l'utilisateur courant (balayé plus tard).
            }
        }
    }

    [GeneratedRegex("^[A-Za-z0-9_]{1,64}$")]
    private static partial Regex NameRegex();
}
