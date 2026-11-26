using StackR.Core.Sites;

namespace StackR.Core.Tests.Sites;

public class SiteScannerTests
{
    [Fact]
    public void Scan_PrefersPublicFolder()
    {
        using var t = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(t.Path, "app", "public"));
        Directory.CreateDirectory(Path.Combine(t.Path, "plain"));
        File.WriteAllText(Path.Combine(t.Path, "file.txt"), "x");
        Directory.CreateDirectory(Path.Combine(t.Path, ".git"));

        using var scanner = new SiteScanner(t.Path);
        var sites = scanner.Scan();

        Assert.Equal(2, sites.Count);
        Assert.Equal(Path.Combine(t.Path, "app", "public"), sites.Single(s => s.Name.Value == "app").DocumentRoot);
        Assert.Equal(Path.Combine(t.Path, "plain"), sites.Single(s => s.Name.Value == "plain").DocumentRoot);
    }

    [Fact]
    public void Scan_DuplicateSlugs_FirstWins()
    {
        using var t = new TempDirectory();
        Directory.CreateDirectory(Path.Combine(t.Path, "Mon Projet"));
        Directory.CreateDirectory(Path.Combine(t.Path, "mon_projet"));

        using var scanner = new SiteScanner(t.Path);
        var sites = scanner.Scan();

        var site = Assert.Single(sites);
        Assert.Equal("mon-projet", site.Name.Value);
        Assert.Equal(Path.Combine(t.Path, "Mon Projet"), site.FolderPath);
    }

    [Fact]
    public void Scan_MissingWww_ReturnsEmpty()
    {
        using var t = new TempDirectory();
        using var scanner = new SiteScanner(Path.Combine(t.Path, "absent"));
        Assert.Empty(scanner.Scan());
    }

    [Fact]
    public void Scan_SkipsHiddenDirectories()
    {
        using var t = new TempDirectory();
        var hidden = Directory.CreateDirectory(Path.Combine(t.Path, "secret"));
        hidden.Attributes |= FileAttributes.Hidden;
        Directory.CreateDirectory(Path.Combine(t.Path, "visible"));

        using var scanner = new SiteScanner(t.Path);
        Assert.Equal("visible", Assert.Single(scanner.Scan()).Name.Value);
    }

    [Fact]
    public async Task Changed_FiresAfterFolderCreated()
    {
        using var t = new TempDirectory();
        using var scanner = new SiteScanner(t.Path);
        var tcs = new TaskCompletionSource();
        scanner.Changed += () => tcs.TrySetResult();

        Directory.CreateDirectory(Path.Combine(t.Path, "nouveau"));

        var done = await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.Same(tcs.Task, done);
    }

    [Fact]
    public void Scan_FolderUnderGlobPath_IsSkippedWithReason()
    {
        using var t = new TempDirectory();
        var www = Path.Combine(t.Path, "www[1]");
        Directory.CreateDirectory(Path.Combine(www, "app"));

        using var scanner = new SiteScanner(www);

        Assert.Empty(scanner.Scan());
        Assert.Contains("caractère générique", Assert.Single(scanner.Skipped), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Changed_FiresWhenWwwAppearsLater()
    {
        using var t = new TempDirectory();
        var www = Path.Combine(t.Path, "www");
        using var scanner = new SiteScanner(www);
        var tcs = new TaskCompletionSource();
        scanner.Changed += () => tcs.TrySetResult();

        Directory.CreateDirectory(www);
        Assert.Same(tcs.Task, await Task.WhenAny(tcs.Task, Task.Delay(TimeSpan.FromSeconds(5))));

        // Et la surveillance de www est active ensuite.
        var again = new TaskCompletionSource();
        scanner.Changed += () => again.TrySetResult();
        Directory.CreateDirectory(Path.Combine(www, "nouveau"));
        Assert.Same(again.Task, await Task.WhenAny(again.Task, Task.Delay(TimeSpan.FromSeconds(5))));
    }

    [Fact]
    public void Scan_WwwAppearsAfterConstruction_IsScanned()
    {
        using var t = new TempDirectory();
        var www = Path.Combine(t.Path, "absent", "www");
        using var scanner = new SiteScanner(www);
        Assert.Empty(scanner.Scan());

        Directory.CreateDirectory(Path.Combine(www, "app"));

        Assert.Equal("app", Assert.Single(scanner.Scan()).Name.Value);
    }
}
