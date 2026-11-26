using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;
using StackR.Core.Manifests;
using StackR.Core.Paths;

namespace StackR.Core.Shell;

// Intégration Windows sans droits admin : shims et CLI sur le PATH utilisateur, démarrage avec Windows.
// userRootOverride remplace HKCU (tests) ; le broadcast WM_SETTINGCHANGE est alors ignoré.
// installDir : dossier de stackr.exe (par défaut celui de l'exécutable courant) ; il n'est ajouté
// au PATH que s'il contient bien stackr.exe (jamais un dossier de build ou de test).
public sealed partial class ShellIntegration(StackRPaths paths, RegistryKey? userRootOverride = null, string? installDir = null)
{
    private const string RunSubKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunValueName = "StackR";
    private const string CliExeName = "stackr.exe";

    private static readonly string[] AllShims =
        ["php", "mysql", "mysqldump", "composer", "node", "npm", "npx"];

    private readonly string? _installDir = installDir ?? Path.GetDirectoryName(Environment.ProcessPath);

    private RegistryKey UserRoot => userRootOverride ?? Registry.CurrentUser;

    // Écrit les shims des composants actifs et supprime ceux des composants devenus inactifs.
    // cmd.exe lit les .cmd avec la page de code OEM (850) : les cibles sont donc exprimées relativement
    // au shim (%~dp0 = bin\shims\) pour que le contenu reste en ASCII, quel que soit le nom de la racine.
    /// <exception cref="ArgumentException">Un dossier n'est pas sous bin\ ou son nom relatif n'est pas ASCII.</exception>
    public void WriteShims(IReadOnlyDictionary<ComponentKind, string> activeDirs)
    {
        ArgumentNullException.ThrowIfNull(activeDirs);
        Directory.CreateDirectory(paths.Shims);

        var shims = new Dictionary<string, string>();
        if (activeDirs.TryGetValue(ComponentKind.Php, out var php))
        {
            shims["php"] = PhpRc + Exe(Target(php, "php.exe"));
            if (activeDirs.TryGetValue(ComponentKind.Composer, out var composer))
            {
                shims["composer"] = PhpRc + $"\"{Target(php, "php.exe")}\" \"{Target(composer, "composer.phar")}\" %*";
            }
        }

        if (activeDirs.TryGetValue(ComponentKind.MySql, out var mysql))
        {
            shims["mysql"] = Exe(Target(mysql, "bin", "mysql.exe"));
            shims["mysqldump"] = Exe(Target(mysql, "bin", "mysqldump.exe"));
        }

        if (activeDirs.TryGetValue(ComponentKind.Node, out var node))
        {
            shims["node"] = Exe(Target(node, "node.exe"));
            shims["npm"] = "call " + Exe(Target(node, "npm.cmd"));
            shims["npx"] = "call " + Exe(Target(node, "npx.cmd"));
        }

        foreach (var name in AllShims)
        {
            var file = Path.Combine(paths.Shims, name + ".cmd");
            if (shims.TryGetValue(name, out var command))
            {
                File.WriteAllText(file, ShimHeader + command + ShimFooter, Encoding.ASCII);
            }
            else if (File.Exists(file))
            {
                File.Delete(file);
            }
        }
    }

    // Ajoute bin\shims et le dossier de stackr.exe au PATH utilisateur (sans doublon, type de valeur préservé).
    public void EnsureOnUserPath()
    {
        using var env = UserRoot.CreateSubKey("Environment", writable: true);
        var (current, kind) = ReadPath(env);
        var entries = Split(current);
        var changed = false;
        foreach (var dir in OurEntries(requireCli: true))
        {
            if (!entries.Any(e => SamePath(e, dir)))
            {
                entries.Add(dir);
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        env.SetValue("Path", string.Join(';', entries), kind ?? RegistryValueKind.ExpandString);
        Broadcast();
    }

    // Retire uniquement les entrées de StackR (bin\shims et dossier d'installation) du PATH utilisateur.
    public void RemoveFromUserPath()
    {
        using var env = UserRoot.CreateSubKey("Environment", writable: true);
        var (current, kind) = ReadPath(env);
        if (current is null)
        {
            return;
        }

        var entries = Split(current);
        var ours = OurEntries(requireCli: false).ToList();
        if (entries.RemoveAll(e => ours.Any(o => SamePath(e, o))) == 0)
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

    // php.ini de StackR (etc\, à côté de bin\) pour PHP en ligne de commande et Composer : sans lui, aucune extension.
    private const string PhpRc = "set \"PHPRC=%D%..\\..\\etc\"\r\n";

    // %~dp0 est faux quand le shim est lancé par son nom entre guillemets via PATH (« "php" -v ») : le dossier
    // est donc capturé dans une étiquette appelée (%D%, avec « \ » final). Arguments hors du « call » (qui altère « ^ »).
    private const string ShimHeader = "@echo off\r\nsetlocal\r\ncall :d\r\n";

    private const string ShimFooter = "\r\nexit /b %errorlevel%\r\n:d\r\nset \"D=%~dp0\"\r\nexit /b\r\n";

    // Chemin « %D%..\<composant>\<version>\... » ; la cible doit se trouver sous bin\.
    private string Target(string componentDir, params string[] parts)
    {
        var bin = Path.TrimEndingDirectorySeparator(Path.GetFullPath(paths.Bin)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine([componentDir, .. parts]));
        if (!full.StartsWith(bin, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException($"Le composant « {componentDir} » n'est pas sous {paths.Bin} : shim impossible.", nameof(componentDir));
        }

        var relative = Path.GetRelativePath(paths.Shims, full);
        if (relative.Any(c => c is < ' ' or > '~' or '%' or '!' or '"'))
        {
            throw new ArgumentException($"Chemin de composant non pris en charge dans un shim : « {relative} ».", nameof(componentDir));
        }

        return "%D%" + relative;
    }

    private IEnumerable<string> OurEntries(bool requireCli)
    {
        yield return paths.Shims;
        if (!string.IsNullOrEmpty(_installDir) && (!requireCli || File.Exists(Path.Combine(_installDir, CliExeName))))
        {
            yield return _installDir;
        }
    }

    private static (string? Value, RegistryValueKind? Kind) ReadPath(RegistryKey env)
    {
        var value = env.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
        return value is null ? (null, null) : (value, env.GetValueKind("Path"));
    }

    private static List<string> Split(string? value) =>
        string.IsNullOrEmpty(value)
            ? []
            : value.Split(';', StringSplitOptions.RemoveEmptyEntries).ToList();

    private static bool SamePath(string entry, string dir) =>
        string.Equals(Normalize(entry), Normalize(dir), StringComparison.OrdinalIgnoreCase);

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
