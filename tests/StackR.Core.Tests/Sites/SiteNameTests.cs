using StackR.Core.Sites;

namespace StackR.Core.Tests.Sites;

public class SiteNameTests
{
    [Theory]
    [InlineData("Mon Projet", "mon-projet")]
    [InlineData("Été_2026", "ete-2026")]
    [InlineData("otelo", "otelo")]
    [InlineData(".git", null)]
    [InlineData("a..b", "a-b")]
    [InlineData("---", null)]
    [InlineData("é!", "e")]
    public void SiteName_FromFolder_Cases(string folder, string? expected)
    {
        var ok = SiteName.TryFromFolder(folder, out var name);
        Assert.Equal(expected is not null, ok);
        if (expected is not null)
        {
            Assert.Equal(expected, name.Value);
            Assert.Equal(expected + ".test", name.Host);
        }
    }

    [Fact]
    public void SiteName_FromFolder_TooLong_Ignored()
    {
        Assert.False(SiteName.TryFromFolder(new string('a', 70), out _));
        Assert.True(SiteName.TryFromFolder(new string('a', 63), out _));
    }

    [Theory]
    [InlineData("x y")]
    [InlineData("")]
    [InlineData("-a")]
    [InlineData("a.b")]
    [InlineData("A")]
    public void SiteName_Constructor_RejectsInvalid(string value)
    {
        Assert.Throws<ArgumentException>(() => new SiteName(value));
    }

    [Fact]
    public void SiteName_Constructor_AcceptsValid()
    {
        Assert.Equal("mon-app.test", new SiteName("mon-app").Host);
    }
}
