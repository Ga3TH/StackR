using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using StackR.Core.Manifests;
using StackR.Core.Paths;

namespace StackR.Core.Shell;

// Intégration Windows sans droits admin : shims sur le PATH utilisateur et démarrage avec Windows.
// userRootOverride remplace HKCU (tests) ; le broadcast WM_SETTINGCHANGE est alors ignoré.
public sealed partial class ShellIntegration(StackRPaths paths, RegistryKey? userRootOverride = null)
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "StackR";

    private static readonly string[] AllShims =
        ["php", "mysql", "mysqldump", "composer", "node", "npm", "npx"];

    private RegistryKey UserRoot => userRootOverride ?? Registry.CurrentUser;

    // Écrit les shims des composants actifs et supprime ceux des composants devenus inactifs.
    public void WriteShims(IReadOnlyDictionary<ComponentKind, string> activeDirs)
    {
        ArgumentNullException.ThrowIfNull(activeDirs);
        Directory.CreateDirectory(paths.Shims);

        var shims = new Dictionary<string, string>();
        if (activeDirs.TryGetValue(ComponentKind.Php, out var php))
        {
            shims["php"] = Exe(Path.Combine(php, "php.exe"));
            if (activeDirs.TryGetValue(ComponentKind.Composer, out var composer))
            {
                shims["composer"] =
                    $"\"{Path.Combine(php, "php.exe")}\" \"{Path.Combine(composer, "composer.phar")}\" %*";
            }
        }

        if (activeDirs.TryGetValue(ComponentKind.MySql, out var mysql))
        {
            shims["mysql"] = Exe(Path.Combine(mysql, "bin", "mysql.exe"));
            shims["mysqldump"] = Exe(Path.Combine(mysql, "bin", "mysqldump.exe"));
        }

        if (activeDirs.TryGetValue(ComponentKind.Node, out var node))
        {
            shims["node"] = Exe(Path.Combine(node, "node.exe"));
            shims["npm"] = "call " + Exe(Path.Combine(node, "npm.cmd"));
            shims["npx"] = "call " + Exe(Path.Combine(node, "npx.cmd"));
        }

        foreach (var name in AllShims)
        {
            var file = Path.Combine(paths.Shims, name + ".cmd");
            if (shims.TryGetValue(name, out var command))
            {
                File.WriteAllText(file, "@echo off\r\n" + command + "\r\n", new UTF8Encoding(false));
            }
            else if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    // Ajoute bin\shims au PATH utilisateur (sans doublon, type de valeur préservé).
    public void EnsureOnUserPath()
    {
        using var env = UserRoot.CreateSubKey("Environment", writable: true);
        var (current, kind) = ReadPath(env);
        var entries = Split(current);
        if (entries.Any(IsOurs))
        {
            return;
        }

        entries.Add(paths.Shims);
        env.SetValue("Path", string.Join(';', entries), kind ?? RegistryValueKind.ExpandString);
        Broadcast();
    }

    // Retire uniquement l'entrée bin\shims du PATH utilisateur.
    public void RemoveFromUserPath()
    {
        using var env = UserRoot.CreateSubKey("Environment", writable: true);
        var (current, kind) = ReadPath(env);
        if (current is null)
        {
            return;
        }

        var entries = Split(current);
        if (entries.RemoveAll(IsOurs) == 0)
        {
            return;
        }

        env.SetValue("Path", string.Join(';', entries), kind ?? RegistryValueKind.ExpandString);
        Broadcast();
    }

    // Active ou désactive le démarrage avec Windows (HKCU\...\Run, valeur StackR).
    public void SetStartWithWindows(bool enabled, string exePath)
    {
        using var run = UserRoot.CreateSubKey(RunSubKey, writable: true);
        if (enabled)
        {
            run.SetValue(RunValueName, $"\"{exePath}\" --minimized", RegistryValueKind.String);
        }
        else
        {
            run.DeleteValue(RunValueName, throwOnMissingValue: false);
        }
    }

    private static string Exe(string path) => $"\"{path}\" %*";

    private static (string? Value, RegistryValueKind? Kind) ReadPath(RegistryKey env)
    {
        var value = env.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        return value is null ? (null, null) : (value, env.GetValueKind("Path"));
    }

    private static List<string> Split(string? value) =>
        string.IsNullOrEmpty(value)
            ? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

    private bool IsOurs(string entry) =>
        string.Equals(Normalize(entry), Normalize(paths.Shims), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string entry) => entry.Trim().TrimEnd('\\');

    private void Broadcast()
    {
        if (userRootOverride is not null)
        {
            return;
        }

        // Meilleur effort : un échec du broadcast ne doit pas faire échouer la modification du PATH.
        _ = SendMessageTimeout(new IntPtr(0xFFFF), 0x001A, UIntPtr.Zero, "Environment", 0x0002, 5000, out _);
    }

    [LibraryImport("user32.dll", EntryPoint = "SendMessageTimeoutW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr SendMessageTimeout(
        IntPtr hWnd, uint msg, UIntPtr wParam, string lParam, uint flags, uint timeout, out UIntPtr result);
}
