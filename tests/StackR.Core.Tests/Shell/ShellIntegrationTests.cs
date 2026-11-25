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
        _shell = new ShellIntegration(_paths, _root);
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
        Assert.Contains($"\"{dirs[ComponentKind.Php]}\\php.exe\" \"{dirs[ComponentKind.Composer]}\\composer.phar\" %*", composer);
        Assert.StartsWith("@echo off", composer);
        Assert.Contains($"\"{dirs[ComponentKind.Php]}\\php.exe\" %*", File.ReadAllText(Path.Combine(_paths.Shims, "php.cmd")));
        Assert.Contains($"\"{dirs[ComponentKind.MySql]}\\bin\\mysql.exe\" %*", File.ReadAllText(Path.Combine(_paths.Shims, "mysql.cmd")));
        Assert.Contains($"\"{dirs[ComponentKind.MySql]}\\bin\\mysqldump.exe\" %*", File.ReadAllText(Path.Combine(_paths.Shims, "mysqldump.cmd")));
        Assert.Contains($"\"{dirs[ComponentKind.Node]}\\node.exe\" %*", File.ReadAllText(Path.Combine(_paths.Shims, "node.cmd")));
        Assert.Contains($"call \"{dirs[ComponentKind.Node]}\\npm.cmd\" %*", File.ReadAllText(Path.Combine(_paths.Shims, "npm.cmd")));
        Assert.Contains($"call \"{dirs[ComponentKind.Node]}\\npx.cmd\" %*", File.ReadAllText(Path.Combine(_paths.Shims, "npx.cmd")));
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
