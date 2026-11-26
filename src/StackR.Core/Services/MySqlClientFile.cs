using System.Globalization;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using StackR.Core.Paths;

namespace StackR.Core.Services;

// Fichier d'options client MySQL temporaire (<tmp>\client-<guid>.cnf) : jamais de secret
// sur la ligne de commande. Accès réservé à l'utilisateur courant, supprimé dans un finally.
internal static class MySqlClientFile
{
    public static string ClientSection(int port, string? password)
    {
        var text = new StringBuilder()
            .Append("[client]\r\n")
            .Append("host=127.0.0.1\r\n")
            .Append(CultureInfo.InvariantCulture, $"port={port}\r\n")
            .Append("user=root\r\n");
        if (password is not null)
        {
            text.Append(CultureInfo.InvariantCulture, $"password=\"{password}\"\r\n");
        }

        return text.ToString();
    }

    /// <summary>Exécute <c>exe --defaults-extra-file="&lt;fichier&gt;" args</c>.</summary>
    public static async Task<int> RunAsync(
        IProcessRunner runner, StackRPaths paths, string exe, string contents, string args, CancellationToken ct)
    {
        Directory.CreateDirectory(paths.Tmp);
        var file = Path.Combine(paths.Tmp, "client-" + Guid.NewGuid().ToString("N") + ".cnf");
        try
        {
            WriteUserOnly(file, contents);
            return await runner.RunAsync(exe, $"--defaults-extra-file=\"{Config.AnsiPath.For(file)}\" {args}", ct).ConfigureAwait(false);
        }
        finally
        {
            try
            {
                File.Delete(file);
            }
            catch (IOException)
            {
                // Fichier encore ouvert : il reste réservé à l'utilisateur courant (balayé plus tard).
                // Ne jamais masquer l'exception d'origine.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    // Créé directement avec une ACL protégée (sans héritage) : seul l'utilisateur courant y a accès.
    internal static void WriteUserOnly(string path, string contents)
    {
        using var identity = WindowsIdentity.GetCurrent();
        var me = identity.User ?? throw new InvalidOperationException("Utilisateur Windows courant introuvable.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, AccessControlType.Allow));

        using var stream = new FileInfo(path).Create(
            FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, security);
        var bytes = new UTF8Encoding(false).GetBytes(contents);
        stream.Write(bytes);
    }
}
