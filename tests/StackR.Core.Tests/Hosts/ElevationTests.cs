using StackR.Core.Hosts;

namespace StackR.Core.Tests.Hosts;

public class ElevationTests
{
    [Fact]
    public void IsElevated_DoesNotThrow()
    {
        _ = Elevation.IsElevated;
    }

    [Fact]
    public void BuildArguments_SimpleTokens_JoinedWithSpaces()
    {
        Assert.Equal("hosts sync --verbose a.b_c-1", Elevation.BuildArguments(["hosts", "sync", "--verbose", "a.b_c-1"]));
    }

    [Theory]
    [InlineData("")]
    [InlineData("a b")]
    [InlineData("\"x")]
    [InlineData("x\"")]
    [InlineData("a&calc")]
    [InlineData("a|b")]
    [InlineData("a^b")]
    [InlineData("%PATH%")]
    [InlineData("a\r\nb")]
    [InlineData("a\\b")]
    [InlineData("C:/x")]
    [InlineData("été")]
    [InlineData("---x")]
    [InlineData("-")]
    [InlineData("a\0")]
    public void BuildArguments_UnsafeToken_Throws(string token)
    {
        Assert.Throws<ArgumentException>(() => Elevation.BuildArguments(["hosts", token]));
    }

    [Fact]
    public void BuildArguments_TooLongToken_Throws()
    {
        Assert.Throws<ArgumentException>(() => Elevation.BuildArguments([new string('a', 65)]));
    }

    [Fact]
    public void BuildArguments_NullOrEmpty_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => Elevation.BuildArguments(null!));
        Assert.Throws<ArgumentException>(() => Elevation.BuildArguments([]));
        Assert.Throws<ArgumentException>(() => Elevation.BuildArguments(["hosts", null!]));
    }

    [Fact]
    public async Task RunCliElevatedAsync_UnsafeArgs_ThrowsBeforeLaunching()
    {
        // La validation doit précéder toute résolution d'exécutable ou tout lancement :
        // sinon on obtiendrait FileNotFoundException (stackr.exe absent du dossier de test).
        await Assert.ThrowsAsync<ArgumentException>(() => Elevation.RunCliElevatedAsync(["hosts", "sync\" & calc"]));
    }

    [Fact]
    public void ResolveCliPath_UsesStackrExeNextToCurrentProcess()
    {
        Assert.Equal(@"C:\Dossier é\StackR\stackr.exe", Elevation.ResolveCliPath(@"C:\Dossier é\StackR\StackR.App.exe"));
    }

    [Fact]
    public void ResolveCliPath_NoProcessPath_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Elevation.ResolveCliPath(null));
    }
}
