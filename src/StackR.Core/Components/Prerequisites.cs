using System.Diagnostics;
using Microsoft.Win32;

namespace StackR.Core.Components;

public static class Prerequisites
{
    // Apache du manifeste est compilé avec VS18 : il faut le runtime 14.50 ou plus récent.
    private const int MinimumMinor = 50;
    private const string RuntimeKeyPath = @"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64";

    public static Uri VcRedistUrl { get; } = new("https://aka.ms/vc14/vc_redist.x64.exe");

    /// <summary>
    /// Vrai si le runtime VC++ x64 14.50+ est installé. <paramref name="rootOverride"/> remplace HKLM (tests).
    /// </summary>
    public static bool HasVcRedistX64(RegistryKey? rootOverride = null)
    {
        try
        {
            using var key = rootOverride is null
                ? Registry.LocalMachine.OpenSubKey(RuntimeKeyPath)
                : rootOverride.OpenSubKey(RuntimeKeyPath);
            if (key is null)
            {
                return false;
            }

            // Major == 14 (et non >= 14) est voulu : Apache Lounge est compilé avec VS18 (toolset 14.50+),
            // et le runtime 14.4x installé par l'ancien lien vs/17 ne suffit pas. Un futur Major 15
            // exigera une revue explicite du manifeste plutôt qu'un succès silencieux.
            return key.GetValue("Installed") is int installed && installed == 1
                && key.GetValue("Major") is int major && major == 14
                && key.GetValue("Minor") is int minor && minor >= MinimumMinor;
        }
        catch (System.Security.SecurityException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static bool IsLaragonRunning()
    {
        var processes = Process.GetProcessesByName("laragon");
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (var p in processes)
            {
                p.Dispose();
            }
        }
    }
}
