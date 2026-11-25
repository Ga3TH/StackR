using StackR.Core.Paths;

namespace StackR.Core.Tests.Paths;

public class StackRPathsTests
{
    [Fact]
    public void ComponentDir_BuildsExpectedPath()
    {
        var paths = new StackRPaths(@"C:\StackR");

        Assert.Equal(@"C:\StackR\bin\apache\2.4.62", paths.ComponentDir("apache", "2.4.62"));
    }

    [Fact]
    public void Properties_UseLayoutFromDesign()
    {
        var paths = new StackRPaths(@"C:\StackR");

        Assert.Equal(@"C:\StackR\bin", paths.Bin);
        Assert.Equal(@"C:\StackR\bin\shims", paths.Shims);
        Assert.Equal(@"C:\StackR\etc", paths.Etc);
        Assert.Equal(@"C:\StackR\data", paths.Data);
        Assert.Equal(@"C:\StackR\data\mysql", paths.MySqlData);
        Assert.Equal(@"C:\StackR\data\ssl", paths.Ssl);
        Assert.Equal(@"C:\StackR\data\secrets", paths.Secrets);
        Assert.Equal(@"C:\StackR\www", paths.Www);
        Assert.Equal(@"C:\StackR\logs", paths.Logs);
        Assert.Equal(@"C:\StackR\tmp", paths.Tmp);
        Assert.Equal(@"C:\StackR\tmp\state.json", paths.StateFile);
    }

    [Fact]
    public void EnsureCreated_CreatesDirectories()
    {
        using var temp = new TempDirectory();
        var paths = new StackRPaths(System.IO.Path.Combine(temp.Path, "Dossier é", "StackR"));

        paths.EnsureCreated();

        Assert.True(Directory.Exists(paths.Bin));
        Assert.True(Directory.Exists(paths.Shims));
        Assert.True(Directory.Exists(paths.Etc));
        Assert.True(Directory.Exists(paths.MySqlData));
        Assert.True(Directory.Exists(paths.Ssl));
        Assert.True(Directory.Exists(paths.Secrets));
        Assert.True(Directory.Exists(paths.Www));
        Assert.True(Directory.Exists(paths.Logs));
        Assert.True(Directory.Exists(paths.Tmp));
    }
}
