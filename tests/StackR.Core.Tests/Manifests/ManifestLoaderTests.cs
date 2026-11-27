using StackR.Core.Manifests;

namespace StackR.Core.Tests.Manifests;

public class ManifestLoaderTests
{
    private const string Sha = "4a191082e9706866830379d577d2cc8a50c776fcd9436a8d061b063ed0d9a0e3";

    private static string Entry(string component, string version, string channel = "stable",
        string url = "https://example.org/a.zip", string sha = Sha) =>
        "{ \"component\": \"" + component + "\", \"version\": \"" + version + "\", \"channel\": \"" + channel + "\"," +
        " \"url\": \"" + url + "\", \"sha256\": \"" + sha + "\", \"sizeBytes\": 10," +
        " \"package\": \"Zip\", \"requires\": [], \"archiveRoot\": \"node\" }";

    private static string Json(string entries) => "{ \"releases\": [ " + entries + " ] }";

    [Fact]
    public void Parse_ValidManifest()
    {
        var m = ManifestLoader.Parse(Json(Entry("Node", "1.2.3")));
        var r = Assert.Single(m.Releases);
        Assert.Equal(ComponentKind.Node, r.Component);
        Assert.Equal(PackageType.Zip, r.Package);
        Assert.Equal("node", r.ArchiveRoot);
    }

    [Fact]
    public void Parse_NonStableChannel_Throws() =>
        Assert.Throws<ManifestException>(() => ManifestLoader.Parse(Json(Entry("Node", "1.2.3", channel: "beta"))));

    [Fact]
    public void Parse_HttpUrl_Throws() =>
        Assert.Throws<ManifestException>(() => ManifestLoader.Parse(Json(Entry("Node", "1.2.3", url: "http://example.org/a.zip"))));

    [Fact]
    public void Parse_BadSha_Throws() =>
        Assert.Throws<ManifestException>(() => ManifestLoader.Parse(Json(Entry("Node", "1.2.3", sha: "abc"))));

    [Fact]
    public void Parse_EmptyVersion_Throws() =>
        Assert.Throws<ManifestException>(() => ManifestLoader.Parse(Json(Entry("Node", ""))));

    [Fact]
    public void Recommended_ReturnsHighestVersion()
    {
        var m = ManifestLoader.Parse(Json(string.Join(",",
            Entry("Php", "8.3.37"), Entry("Php", "8.10.0"), Entry("Php", "8.4.28"))));
        Assert.Equal("8.10.0", m.Recommended(ComponentKind.Php)!.Version);
        Assert.Null(m.Recommended(ComponentKind.Node));
    }

    [Fact]
    public void LoadEmbedded_EveryComponentHasRecommended()
    {
        var m = ManifestLoader.LoadEmbedded();
        foreach (var kind in Enum.GetValues<ComponentKind>())
            Assert.NotNull(m.Recommended(kind));
        Assert.All(m.Releases, r =>
        {
            Assert.Equal("stable", r.Channel);
            Assert.Equal(Uri.UriSchemeHttps, r.Url.Scheme);
        });
    }

    [Fact]
    public void LoadEmbedded_HeidiSqlAndAdminerThemes_AreDefaultOfficialDownloads()
    {
        var m = ManifestLoader.LoadEmbedded();
        var heidi = m.Recommended(ComponentKind.HeidiSql)!;
        Assert.Equal(PackageType.Zip, heidi.Package);
        Assert.Equal("github.com", heidi.Url.Host);
        Assert.EndsWith("_64_Portable.zip", heidi.Url.AbsolutePath, StringComparison.Ordinal);
        Assert.Equal("adminer.css", Path.GetFileName(m.Recommended(ComponentKind.AdminerCss)!.Url.AbsolutePath));
        Assert.Equal("adminer-dark.css", Path.GetFileName(m.Recommended(ComponentKind.AdminerDarkCss)!.Url.AbsolutePath));
        Assert.All(new[] { ComponentKind.HeidiSql, ComponentKind.AdminerCss, ComponentKind.AdminerDarkCss },
            k => Assert.Contains(k, StackREngine.DefaultComponents));
    }

    [Fact]
    public void Parse_Sha256WithTrailingNewline_Throws() =>
        Assert.Throws<ManifestException>(() => ManifestLoader.Parse(Json(Entry("Node", "1.2.3", sha: Sha + "\\n"))));

    [Theory]
    [InlineData("1")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.x")]
    [InlineData("..\\\\evil")]
    [InlineData("1.2\\n")]
    public void Parse_BadVersion_Throws(string version) =>
        Assert.Throws<ManifestException>(() => ManifestLoader.Parse(Json(Entry("Node", version))));

    [Fact]
    public void Parse_DuplicateComponentVersion_Throws() =>
        Assert.Throws<ManifestException>(() => ManifestLoader.Parse(Json(Entry("Node", "1.2.3") + "," + Entry("Node", "1.2.3"))));

    [Theory]
    [InlineData("\"component\": \"Node\", ")]
    [InlineData("\"package\": \"Zip\",")]
    [InlineData("\"sizeBytes\": 10,")]
    [InlineData("\"channel\": \"stable\",")]
    [InlineData("\"url\": \"https://example.org/a.zip\",")]
    public void Parse_MissingField_Throws(string fragment)
    {
        var entry = Entry("Node", "1.2.3");
        Assert.Contains(fragment, entry);
        Assert.Throws<ManifestException>(() => ManifestLoader.Parse(Json(entry.Replace(fragment, ""))));
    }

    [Fact]
    public void Parse_ZeroSize_Throws() =>
        Assert.Throws<ManifestException>(() => ManifestLoader.Parse(Json(Entry("Node", "1.2.3").Replace("\"sizeBytes\": 10", "\"sizeBytes\": 0"))));

    [Fact]
    public void LoadEmbedded_PhpUrlsUseArchives() =>
        Assert.All(ManifestLoader.LoadEmbedded().Releases.Where(r => r.Component == ComponentKind.Php),
            r => Assert.Contains("/releases/archives/", r.Url.AbsoluteUri));
}
