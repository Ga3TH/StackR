using StackR.Core.Sites;

namespace StackR.Core.Tests.Sites;

public class SiteInspectorTests
{
    private static string Make(TempDirectory temp, string name, params string[] files)
    {
        var dir = Path.Combine(temp.Path, name);
        Directory.CreateDirectory(dir);
        foreach (var file in files)
        {
            var full = Path.Combine(dir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "x");
        }

        return dir;
    }

    [Theory]
    [InlineData(SiteFramework.Laravel, "artisan", "public/index.php")]
    [InlineData(SiteFramework.Symfony, "symfony.lock", "public/index.php")]
    [InlineData(SiteFramework.Symfony, "bin/console", "config/bundles.php")]
    [InlineData(SiteFramework.WordPress, "wp-config.php", "index.php")]
    [InlineData(SiteFramework.WordPress, "wp-includes/version.php")]
    [InlineData(SiteFramework.Php, "index.php")]
    [InlineData(SiteFramework.Php, "public/index.php", "public/page.html")]
    [InlineData(SiteFramework.Static, "index.html")]
    [InlineData(SiteFramework.Php)]
    public void DetectFramework_TypicalFiles_Recognized(SiteFramework expected, params string[] files)
    {
        using var temp = new TempDirectory();
        Assert.Equal(expected, SiteInspector.DetectFramework(Make(temp, "site", files)));
    }

    [Fact]
    public void DetectFramework_MissingFolder_IsPhp()
    {
        using var temp = new TempDirectory();
        Assert.Equal(SiteFramework.Php, SiteInspector.DetectFramework(Path.Combine(temp.Path, "absent")));
    }

    [Fact]
    public void Inspect_ReportsGitComposerNodeAndDate()
    {
        using var temp = new TempDirectory();
        var details = SiteInspector.Inspect(Make(temp, "app", "composer.json", "package.json", ".git/HEAD"));
        Assert.True(details.HasGit);
        Assert.True(details.HasComposer);
        Assert.True(details.HasNode);
        Assert.NotNull(details.LastModified);
    }

    [Fact]
    public void LastModified_IgnoresVendor()
    {
        using var temp = new TempDirectory();
        var dir = Make(temp, "app", "index.php", "vendor/autoload.php");
        var old = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(Path.Combine(dir, "index.php"), old);
        Directory.SetLastWriteTimeUtc(Path.Combine(dir, "vendor"), DateTime.UtcNow);
        Directory.SetLastWriteTimeUtc(dir, old);
        Assert.Equal(old, SiteInspector.LastModified(dir)!.Value.UtcDateTime);
    }
}
