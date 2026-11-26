using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using StackR.Core.Manifests;
using StackR.Core.Paths;
using StackR.Core.Shell;

namespace StackR.Core.Tests.Shell;

public sealed class ShellIntegrationTests : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _keyPath = @"Software\StackR.Tests\" + Guid.NewGuid().ToString("N");
    private readonly RegistryKey _root;
    private readonly StackRPaths _paths;
    private readonly ShellIntegration _shell;

    public ShellIntegrationTests()
    {
        _root = Registry.CurrentUser.CreateSubKey(_keyPath);
        _paths = new StackRPaths(Path.Combine(_temp.Path, "Dossier é", "StackR"));
        _paths.EnsureCreated();

        // Dossier d'installation sans stackr.exe : il n'est jamais ajouté au PATH.
        var noCli = Path.Combine(_temp.Path, "sans cli");
        Directory.CreateDirectory(noCli);
        _shell = new ShellIntegration(_paths, _root, noCli);
    }

    // Dossier d'installation (espace + accent) contenant un stackr.exe factice.
    private string MakeInstallDir()
    {
        var dir = Path.Combine(_temp.Path, "Programmes é", "StackR");
        Directory.CreateDirectory(dir);
        File.WriteAllBytes(Path.Combine(dir, "stackr.exe"), []);
        return dir;
    }

    public void Dispose()
    {
        _root.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        _temp.Dispose();
    }

    private string? ReadPath(out RegistryValueKind kind)
    {
        using var env = _root.CreateSubKey("Environment");
        kind = env.GetValueKind("Path");
        return (string?)env.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames);
    }

    private void SeedPath(string value, RegistryValueKind kind)
    {
        using var env = _root.CreateSubKey("Environment");
        env.SetValue("Path", value, kind);
    }

    [Fact]
    public void EnsureOnUserPath_AddsOnce()
    {
        SeedPath(@"C:\a;C:\b", RegistryValueKind.ExpandString);
        _shell.EnsureOnUserPath();
        _shell.EnsureOnUserPath();
        var entries = ReadPath(out _)!.Split(';');
        Assert.Equal(1, entries.Count(e => string.Equals(e.TrimEnd('\\'), _paths.Shims, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(3, entries.Length);
    }

    [Fact]
    public void EnsureOnUserPath_WhenAbsent_CreatesExpandString()
    {
        _shell.EnsureOnUserPath();
        var value = ReadPath(out var kind);
        Assert.Equal(RegistryValueKind.ExpandString, kind);
        Assert.Equal(_paths.Shims, value);
    }

    [Fact]
    public void EnsureOnUserPath_PreservesExpandSz()
    {
        SeedPath(@"%USERPROFILE%\bin;C:\b", RegistryValueKind.ExpandString);
        _shell.EnsureOnUserPath();
        var value = ReadPath(out var kind);
        Assert.Equal(RegistryValueKind.ExpandString, kind);
        Assert.StartsWith(@"%USERPROFILE%\bin;C:\b;", value);
    }

    [Fact]
    public void EnsureOnUserPath_PreservesPlainString()
    {
        SeedPath(@"C:\a", RegistryValueKind.String);
        _shell.EnsureOnUserPath();
        ReadPath(out var kind);
        Assert.Equal(RegistryValueKind.String, kind);
    }

    [Fact]
    public void EnsureOnUserPath_TrailingBackslashAndCase_NoDuplicate()
    {
        SeedPath(@"C:\a;" + _paths.Shims.ToUpperInvariant() + @"\", RegistryValueKind.ExpandString);
        _shell.EnsureOnUserPath();
        Assert.Equal(2, ReadPath(out _)!.Split(';').Length);
    }

    [Fact]
    public void RemoveFromUserPath_RemovesOnlyOurs()
    {
        SeedPath(@"%USERPROFILE%\bin;" + _paths.Shims + @"\;C:\b", RegistryValueKind.ExpandString);
        _shell.RemoveFromUserPath();
        var value = ReadPath(out var kind);
        Assert.Equal(@"%USERPROFILE%\bin;C:\b", value);
        Assert.Equal(RegistryValueKind.ExpandString, kind);
    }

    [Fact]
    public void RemoveFromUserPath_WhenAbsent_DoesNothing()
    {
        _shell.RemoveFromUserPath();
        using var env = _root.OpenSubKey("Environment");
        Assert.Null(env?.GetValue("Path"));
    }

    private Dictionary<ComponentKind, string> MakeDirs()
    {
        return new()
        {
            [ComponentKind.Php] = Path.Combine(_paths.Bin, "php", "8.3"),
            [ComponentKind.MySql] = Path.Combine(_paths.Bin, "mysql", "8.4"),
            [ComponentKind.Node] = Path.Combine(_paths.Bin, "node", "22"),
            [ComponentKind.Composer] = Path.Combine(_paths.Bin, "composer", "2"),
        };
    }

    [Fact]
    public void WriteShims_ComposerUsesActivePhp()
    {
        var dirs = MakeDirs();
        _shell.WriteShims(dirs);
        var composer = File.ReadAllText(Path.Combine(_paths.Shims, "composer.cmd"));
        // Chemins relatifs au shim (%~dp0 = bin\shims\) : contenu ASCII quel que soit le nom de la racine.
        Assert.Contains(@"""%~dp0..\php\8.3\php.exe"" ""%~dp0..\composer\2\composer.phar"" %*", composer);
        Assert.StartsWith("@echo off", composer);
        Assert.Contains(@"""%~dp0..\php\8.3\php.exe"" %*", File.ReadAllText(Path.Combine(_paths.Shims, "php.cmd")));
        Assert.Contains(@"""%~dp0..\mysql\8.4\bin\mysql.exe"" %*", File.ReadAllText(Path.Combine(_paths.Shims, "mysql.cmd")));
        Assert.Contains(@"""%~dp0..\mysql\8.4\bin\mysqldump.exe"" %*", File.ReadAllText(Path.Combine(_paths.Shims, "mysqldump.cmd")));
        Assert.Contains(@"""%~dp0..\node\22\node.exe"" %*", File.ReadAllText(Path.Combine(_paths.Shims, "node.cmd")));
        Assert.Contains(@"call ""%~dp0..\node\22\npm.cmd"" %*", File.ReadAllText(Path.Combine(_paths.Shims, "npm.cmd")));
        Assert.Contains(@"call ""%~dp0..\node\22\npx.cmd"" %*", File.ReadAllText(Path.Combine(_paths.Shims, "npx.cmd")));
    }

    [Fact]
    public void WriteShims_ContentIsAsciiOnly_UnderAccentedRoot()
    {
        _shell.WriteShims(MakeDirs());
        foreach (var file in Directory.EnumerateFiles(_paths.Shims, "*.cmd"))
        {
            Assert.All(File.ReadAllBytes(file), b => Assert.True(b < 0x80, $"{Path.GetFileName(file)} contient un octet non ASCII"));
        }
    }

    [Fact]
    public async Task WriteShims_ShimRunsUnderOemCodePage850_WithAccentedRoot()
    {
        // Cible factice : un npm.cmd qui affiche ses arguments, sous une racine « Dossier é ».
        var nodeDir = Path.Combine(_paths.Bin, "node", "22");
        Directory.CreateDirectory(nodeDir);
        File.WriteAllText(Path.Combine(nodeDir, "npm.cmd"), "@echo off\r\necho SHIM-OK %*\r\n", Encoding.ASCII);
        _shell.WriteShims(new Dictionary<ComponentKind, string> { [ComponentKind.Node] = nodeDir });

        var shim = Path.Combine(_paths.Shims, "npm.cmd");
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "cmd.exe"))
        {
            // /s : cmd retire seulement les guillemets extérieurs de la commande.
            Arguments = $"/d /s /c \"chcp 850 >nul & \"{shim}\" bonjour\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout;
        Assert.True(process.ExitCode == 0, output + await stderr);
        Assert.Contains("SHIM-OK bonjour", output);
    }

    [Fact]
    public void WriteShims_TargetOutsideBin_Throws()
    {
        var outside = new Dictionary<ComponentKind, string> { [ComponentKind.Php] = Path.Combine(_temp.Path, "ailleurs", "php") };
        Assert.Throws<ArgumentException>(() => _shell.WriteShims(outside));
    }

    [Fact]
    public void EnsureOnUserPath_AddsShimsAndInstallDir()
    {
        var install = MakeInstallDir();
        var shell = new ShellIntegration(_paths, _root, install);
        SeedPath(@"C:\a", RegistryValueKind.ExpandString);
        shell.EnsureOnUserPath();
        shell.EnsureOnUserPath();
        Assert.Equal([@"C:\a", _paths.Shims, install], ReadPath(out _)!.Split(';'));
    }

    [Fact]
    public void EnsureOnUserPath_InstallDirAlreadyPresent_AddsOnlyShims()
    {
        var install = MakeInstallDir();
        var shell = new ShellIntegration(_paths, _root, install);
        SeedPath(install.ToUpperInvariant() + @"\", RegistryValueKind.ExpandString);
        shell.EnsureOnUserPath();
        Assert.Equal([install.ToUpperInvariant() + @"\", _paths.Shims], ReadPath(out _)!.Split(';'));
    }

    [Fact]
    public void EnsureOnUserPath_InstallDirWithoutCli_IsNotAdded()
    {
        _shell.EnsureOnUserPath();
        Assert.Equal(_paths.Shims, ReadPath(out _));
    }

    [Fact]
    public void RemoveFromUserPath_RemovesShimsAndInstallDir()
    {
        var install = MakeInstallDir();
        var shell = new ShellIntegration(_paths, _root, install);
        SeedPath(@"C:\a;" + _paths.Shims + ";" + install + @"\;C:\b", RegistryValueKind.ExpandString);
        shell.RemoveFromUserPath();
        Assert.Equal(@"C:\a;C:\b", ReadPath(out _));
    }

    [Fact]
    public void WriteShims_SkipsInactiveAndDeletesStale()
    {
        _shell.WriteShims(MakeDirs());
        var dirs = MakeDirs();
        dirs.Remove(ComponentKind.Node);
        dirs.Remove(ComponentKind.Php);
        _shell.WriteShims(dirs);
        Assert.False(File.Exists(Path.Combine(_paths.Shims, "node.cmd")));
        Assert.False(File.Exists(Path.Combine(_paths.Shims, "npm.cmd")));
        Assert.False(File.Exists(Path.Combine(_paths.Shims, "php.cmd")));
        Assert.False(File.Exists(Path.Combine(_paths.Shims, "composer.cmd")));
        Assert.True(File.Exists(Path.Combine(_paths.Shims, "mysql.cmd")));
    }

    [Fact]
    public void StartWithWindows_TogglesRunValue()
    {
        const string exe = @"C:\Dossier é\StackR.exe";
        _shell.SetStartWithWindows(true, exe);
        using var run = _root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run")!;
        Assert.Equal($"\"{exe}\" --minimized", run.GetValue("StackR"));
        _shell.SetStartWithWindows(false, exe);
        Assert.Null(run.GetValue("StackR"));
    }
}
