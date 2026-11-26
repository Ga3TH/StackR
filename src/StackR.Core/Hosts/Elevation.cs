using System.ComponentModel;
using System.Diagnostics;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace StackR.Core.Hosts;

/// <summary>
/// Détection des droits administrateur et relance du CLI <c>stackr.exe</c> via UAC.
/// </summary>
public static partial class Elevation
{
    private const string CliExeName = "stackr.exe";
    private const int ErrorCancelled = 1223; // ERROR_CANCELLED : l'utilisateur a refusé l'UAC.

    // Assez pour une liste de noms d'hôtes séparés par des virgules (« hosts sync --names a.test,b.test »),
    // très en deçà de la limite de ligne de commande de ShellExecute.
    internal const int MaxTokenLength = 8192;

    // Liste blanche : jetons simples uniquement (pas d'espace, guillemet, métacaractère, chemin ni non-ASCII),
    // de sorte que la jonction par espaces ne puisse jamais être réinterprétée par le parseur de ligne de commande.
    // La virgule (liste de noms) n'est spéciale ni pour cmd.exe ni pour CommandLineToArgvW.
    [GeneratedRegex(@"^-{0,2}[A-Za-z0-9][A-Za-z0-9._,-]*\z", RegexOptions.CultureInvariant)]
    private static partial Regex SafeTokenRegex();

    /// <summary>Vrai si le processus courant s'exécute avec un jeton administrateur (élevé).</summary>
    public static bool IsElevated
    {
        get
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    /// <summary>
    /// Lance <c>stackr.exe</c> (dossier de l'exécutable courant) élevé via UAC et attend sa fin.
    /// </summary>
    /// <returns>Le code de sortie du CLI, ou <c>-1</c> si l'utilisateur a refusé l'élévation.</returns>
    /// <exception cref="ArgumentException">Un argument n'est pas un jeton simple autorisé.</exception>
    public static async Task<int> RunCliElevatedAsync(string[] args)
    {
        // Validation avant toute autre action : rien n'est lancé avec des arguments douteux.
        var arguments = BuildArguments(args);
        var exe = ResolveCliPath(Environment.ProcessPath);
        if (!File.Exists(exe))
        {
            throw new FileNotFoundException($"CLI StackR introuvable : « {exe} ».", exe);
        }

        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = arguments,
            Verb = "runas",
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            WindowStyle = ProcessWindowStyle.Hidden,
        };

        Process? process;
        try
        {
            process = Process.Start(psi);
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == ErrorCancelled)
        {
            return -1;
        }

        if (process is null)
        {
            throw new InvalidOperationException("Le CLI StackR élevé n'a pas pu être démarré.");
        }

        using (process)
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
            return process.ExitCode;
        }
    }

    /// <summary>Valide chaque argument contre la liste blanche puis les joint par des espaces.</summary>
    internal static string BuildArguments(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length == 0)
        {
            throw new ArgumentException("Aucun argument fourni pour le CLI élevé.", nameof(args));
        }

        foreach (var token in args)
        {
            if (token is null || token.Length > MaxTokenLength || !SafeTokenRegex().IsMatch(token))
            {
                throw new ArgumentException("Argument refusé pour le CLI élevé : seuls des jetons simples (lettres, chiffres, « . », « _ », « - », « , ») sont autorisés.", nameof(args));
            }
        }

        return string.Join(' ', args);
    }

    /// <summary>Chemin de <c>stackr.exe</c> situé dans le même dossier que <paramref name="processPath"/>.</summary>
    internal static string ResolveCliPath(string? processPath)
    {
        var directory = string.IsNullOrEmpty(processPath) ? null : Path.GetDirectoryName(processPath);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("Impossible de déterminer le dossier de l'exécutable courant.");
        }

        return Path.Combine(directory, CliExeName);
    }
}
