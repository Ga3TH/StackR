using StackR.Core.Components;
using StackR.Core.Manifests;
using StackR.Core.Paths;

namespace StackR.Core.Tests.Components;

public sealed class ComponentStoreTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly StackRPaths _paths;

    public ComponentStoreTests() => _paths = new StackRPaths(_root.Path);

    public void Dispose() => _root.Dispose();

    private void Make(string comp, string ver, bool marker)
    {
        var d = _paths.ComponentDir(comp, ver);
        Directory.CreateDirectory(d);
        File.WriteAllText(Path.Combine(d, "x.txt"), "x");
        if (marker)
        {
            File.WriteAllText(Path.Combine(d, ".stackr-installed"), "{}");
        }
    }

    [Fact]
    public void Installed_IgnoresFolderWithoutMarker()
    {
        Make("php", "8.4.1", marker: true);
        Make("php", "8.3.0", marker: false);
        Make("php", "8.2.0.partial", marker: true);

        var store = new ComponentStore(_paths, _ => null);

        Assert.Equal(["8.4.1"], store.Installed(ComponentKind.Php));
        Assert.False(store.IsInstalled(ComponentKind.Php, "8.3.0"));
        Assert.Empty(store.Installed(ComponentKind.Apache));
    }

    [Fact]
    public void Uninstall_Active_Throws()
    {
        Make("php", "8.4.1", marker: true);
        var store = new ComponentStore(_paths, _ => "8.4.1");

        Assert.Throws<InvalidOperationException>(() => store.Uninstall(ComponentKind.Php, "8.4.1"));
        Assert.True(store.IsInstalled(ComponentKind.Php, "8.4.1"));
    }

    [Fact]
    public void Uninstall_Inactive_RemovesFolder()
    {
        Make("php", "8.3.0", marker: true);
        var store = new ComponentStore(_paths, _ => "8.4.1");

        store.Uninstall(ComponentKind.Php, "8.3.0");

        Assert.False(Directory.Exists(_paths.ComponentDir("php", "8.3.0")));
    }

    [Fact]
    public void Uninstall_PartialFailure_NoLongerLooksInstalled()
    {
        Make("php", "8.3.0", marker: true);
        var dir = _paths.ComponentDir("php", "8.3.0");
        var store = new ComponentStore(_paths, _ => "8.4.1");

        // Une DLL verrouillée fait échouer la suppression récursive à mi-chemin.
        using (new FileStream(Path.Combine(dir, "x.txt"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => store.Uninstall(ComponentKind.Php, "8.3.0"));

            Assert.False(store.IsInstalled(ComponentKind.Php, "8.3.0"));
            Assert.Empty(store.Installed(ComponentKind.Php));
        }
    }
}
