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
}
