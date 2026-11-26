using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using StackR.Core.Paths;

namespace StackR.App.Services;

/// <summary>Ouvertures externes (navigateur, explorateur, terminal, VS Code) : jamais d'exception à l'appelant.</summary>
public static class ShellLauncher
{
    /// <summary>Ouvre une adresse http(s) dans le navigateur par défaut.</summary>
    public static bool OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
        {
            return false;
        }

        return TryStart(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
    }

    /// <summary>Ouvre un dossier dans l'explorateur (il est créé s'il n'existe pas encore).</summary>
    public static bool OpenFolder(string path)
    {
        try
        {
            Directory.CreateDirectory(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }

        return TryStart(new ProcessStartInfo("explorer.exe") { ArgumentList = { path }, UseShellExecute = false });
    }

    /// <summary>Ouvre un terminal (Windows Terminal sinon cmd) dans <paramref name="directory"/>, shims de StackR en tête du PATH.</summary>
    public static bool OpenTerminal(StackRPaths paths, string directory)
    {
        try
        {
            Directory.CreateDirectory(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            directory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        var path = paths.Shims + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");

        var wt = new ProcessStartInfo("wt.exe") { ArgumentList = { "-d", directory }, UseShellExecute = false };
        wt.Environment["PATH"] = path;
        if (TryStart(wt))
        {
            return true;
        }

        var cmd = new ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/k" },
            WorkingDirectory = directory,
            UseShellExecute = false,
        };
        cmd.Environment["PATH"] = path;
        return TryStart(cmd);
    }

    /// <summary>Chemin de la commande <c>code</c> de VS Code si elle est dans le PATH, sinon null.</summary>
    public static string? FindVsCode()
    {
        foreach (var dir in (Environment.GetEnvironmentVariable("PATH") ?? "").Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in new[] { "code.cmd", "code.exe" })
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim('"'), name);
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch (ArgumentException)
                {
                    // Entrée de PATH invalide : ignorée.
                }
            }
        }

        return null;
    }

    public static bool OpenVsCode(string folder)
    {
        if (FindVsCode() is null)
        {
            return false;
        }

        return TryStart(new ProcessStartInfo("cmd.exe")
        {
            ArgumentList = { "/c", "code", folder },
            UseShellExecute = false,
            CreateNoWindow = true,
        });
    }

    private static bool TryStart(ProcessStartInfo info)
    {
        try
        {
            using var process = Process.Start(info);
            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException)
        {
            return false;
        }
    }
}
