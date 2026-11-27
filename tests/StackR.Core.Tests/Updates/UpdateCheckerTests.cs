using System.Net;
using System.Security.Cryptography;
using System.Text;
using StackR.Core.Download;
using StackR.Core.Updates;

namespace StackR.Core.Tests.Updates;

public sealed class UpdateCheckerTests : IDisposable
{
    private static readonly string Sha = new('a', 64);
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "stackr-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir))
        {
            Directory.Delete(_dir, recursive: true);
        }
    }

    private static string Release(string tag = "v0.1.4", string assetName = "StackR-Setup-0.1.4.exe", string? digest = null, bool prerelease = false, bool draft = false, string body = "Notes") =>
        $$"""
        {"tag_name":"{{tag}}","draft":{{draft.ToString().ToLowerInvariant()}},"prerelease":{{prerelease.ToString().ToLowerInvariant()}},
         "html_url":"https://github.com/Ga3TH/StackR/releases/tag/{{tag}}","body":"{{body}}",
         "assets":[{"name":"other.zip","size":5,"browser_download_url":"https://example.org/other.zip","digest":"sha256:{{Sha}}"},
                   {"name":"{{assetName}}","size":1234,"browser_download_url":"https://github.com/Ga3TH/StackR/releases/download/{{tag}}/{{assetName}}"{{(digest is null ? "" : $",\"digest\":\"{digest}\"")}}}]}
        """;

    [Theory]
    [InlineData("v0.1.4", "0.1.3", true)]
    [InlineData("0.2.0", "0.1.9", true)]
    [InlineData("v0.1.3", "0.1.3", false)]
    [InlineData("v0.1.2", "0.1.3", false)]
    [InlineData("v0.1.4", "0.1.4.0", false)]
    [InlineData("v1.0.0", "0.9.9.9", true)]
    public void IsNewer_Compares_ThreeComponents(string tag, string current, bool expected)
    {
        Assert.True(UpdateChecker.TryParseTag(tag, out var v));
        Assert.Equal(expected, UpdateChecker.IsNewer(v, Version.Parse(current)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("v0.1")]
    [InlineData("v0.1.4-beta")]
    [InlineData("v0.1.x")]
    public void TryParseTag_Invalid_ReturnsFalse(string tag) =>
        Assert.False(UpdateChecker.TryParseTag(tag, out _));

    [Fact]
    public void Parse_NewerWithDigest_ReturnsInstallableInfo()
    {
        var result = UpdateChecker.Parse(Release(digest: "sha256:" + Sha.ToUpperInvariant()), new Version(0, 1, 3, 0));

        Assert.Equal(UpdateCheckStatus.Available, result.Status);
        var info = result.Info!;
        Assert.Equal(new Version(0, 1, 4), info.Version);
        Assert.Equal("Notes", info.ReleaseNotes);
        Assert.Equal(Sha, info.Sha256);
        Assert.Equal(1234, info.SizeBytes);
        Assert.EndsWith("StackR-Setup-0.1.4.exe", info.InstallerUrl!.AbsoluteUri, StringComparison.Ordinal);
        Assert.Equal("https://github.com/Ga3TH/StackR/releases/tag/v0.1.4", info.HtmlUrl.AbsoluteUri);
        Assert.True(info.CanInstall);
    }

    [Fact]
    public void Parse_NoDigest_NotInstallable()
    {
        var info = UpdateChecker.Parse(Release(digest: null), new Version(0, 1, 3)).Info!;

        Assert.False(info.CanInstall);
        Assert.Null(info.InstallerUrl);
        Assert.Null(info.Sha256);
    }

    [Fact]
    public void Parse_MalformedDigest_NotInstallable()
    {
        Assert.False(UpdateChecker.Parse(Release(digest: "sha1:abc"), new Version(0, 1, 3)).Info!.CanInstall);
        Assert.False(UpdateChecker.Parse(Release(digest: "sha256:zz"), new Version(0, 1, 3)).Info!.CanInstall);
    }

    [Fact]
    public void Parse_MissingAsset_NotInstallable()
    {
        var info = UpdateChecker.Parse(Release(assetName: "StackR-Setup-0.1.9.exe", digest: "sha256:" + Sha), new Version(0, 1, 3)).Info!;

        Assert.False(info.CanInstall);
        Assert.Equal(new Version(0, 1, 4), info.Version);
    }

    [Fact]
    public void Parse_PrereleaseOrDraft_Ignored()
    {
        Assert.Equal(UpdateCheckStatus.UpToDate, UpdateChecker.Parse(Release(prerelease: true, digest: "sha256:" + Sha), new Version(0, 1, 3)).Status);
        Assert.Equal(UpdateCheckStatus.UpToDate, UpdateChecker.Parse(Release(draft: true, digest: "sha256:" + Sha), new Version(0, 1, 3)).Status);
    }

    [Fact]
    public void Parse_SameVersion_UpToDate() =>
        Assert.Equal(UpdateCheckStatus.UpToDate, UpdateChecker.Parse(Release(), new Version(0, 1, 4)).Status);

    [Fact]
    public void Parse_Garbage_Failed()
    {
        Assert.Equal(UpdateCheckStatus.Failed, UpdateChecker.Parse("not json", new Version(0, 1, 3)).Status);
        Assert.Equal(UpdateCheckStatus.Failed, UpdateChecker.Parse("[]", new Version(0, 1, 3)).Status);
        Assert.Equal(UpdateCheckStatus.Failed, UpdateChecker.Parse("{\"tag_name\":\"nightly\"}", new Version(0, 1, 3)).Status);
    }

    [Fact]
    public async Task Check_Success_SendsUserAgentAndReturnsInfo()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Release(digest: "sha256:" + Sha)) });
        using var client = new HttpClient(handler);

        var info = await new UpdateChecker(client, new Version(0, 1, 3, 0)).CheckAsync();

        Assert.NotNull(info);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(UpdateChecker.DefaultEndpoint, request.RequestUri);
        Assert.Contains("StackR/0.1.3", request.Headers.UserAgent.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_NetworkFailure_ReturnsNullWithoutThrowing()
    {
        using var handler = new FakeHandler(_ => throw new HttpRequestException("offline"));
        using var client = new HttpClient(handler);
        var checker = new UpdateChecker(client, new Version(0, 1, 3));

        Assert.Null(await checker.CheckAsync());
        Assert.Equal(UpdateCheckStatus.Failed, (await checker.CheckDetailedAsync()).Status);
    }

    [Fact]
    public async Task Check_HttpError_ReturnsNull()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden));
        using var client = new HttpClient(handler);

        Assert.Equal(UpdateCheckStatus.Failed, (await new UpdateChecker(client, new Version(0, 1, 3)).CheckDetailedAsync()).Status);
    }

    [Fact]
    public async Task Check_Timeout_ReturnsNull()
    {
        using var handler = new HangingHandler();
        using var client = new HttpClient(handler);
        var checker = new UpdateChecker(client, new Version(0, 1, 3), timeout: TimeSpan.FromMilliseconds(100));

        Assert.Null(await checker.CheckAsync());
    }

    [Fact]
    public void IsInstalledCopy_DependsOnUninstallerNextToExe()
    {
        Directory.CreateDirectory(_dir);
        var exe = Path.Combine(_dir, "StackR.App.exe");
        File.WriteAllText(exe, "x");
        Assert.False(UpdateInstallation.IsInstalledCopy(exe));

        File.WriteAllText(Path.Combine(_dir, "unins000.exe"), "x");
        Assert.True(UpdateInstallation.IsInstalledCopy(exe));
        Assert.False(UpdateInstallation.IsInstalledCopy(null));
        Assert.False(UpdateInstallation.IsInstalledCopy(""));
    }

    [Fact]
    public async Task DownloadInstaller_Verified_ReturnsNamedExe()
    {
        var payload = Encoding.ASCII.GetBytes(new string('x', 5000));
        var sha = Convert.ToHexStringLower(SHA256.HashData(payload));
        var info = new UpdateInfo(new Version(0, 1, 4), "", new Uri("https://example.org/s.exe"), sha, payload.Length, new Uri("https://example.org/r"));
        using var handler = new FakeHandler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload), RequestMessage = r });
        using var client = new HttpClient(handler);

        var path = await UpdateInstallation.DownloadInstallerAsync(new SecureDownloader(client), info, _dir, null, CancellationToken.None);

        Assert.Equal("StackR-Setup-0.1.4.exe", Path.GetFileName(path));
        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
    }

    [Fact]
    public async Task DownloadInstaller_WrongHash_Throws_AndKeepsNothing()
    {
        var payload = new byte[2000];
        var info = new UpdateInfo(new Version(0, 1, 4), "", new Uri("https://example.org/s.exe"), Sha, payload.Length, new Uri("https://example.org/r"));
        using var handler = new FakeHandler(r => new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload), RequestMessage = r });
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<IntegrityException>(() =>
            UpdateInstallation.DownloadInstallerAsync(new SecureDownloader(client), info, _dir, null, CancellationToken.None));
        Assert.Empty(Directory.GetFiles(_dir));
    }

    [Fact]
    public async Task DownloadInstaller_NotInstallable_Throws()
    {
        var info = new UpdateInfo(new Version(0, 1, 4), "", null, null, 0, new Uri("https://example.org/r"));
        using var client = new HttpClient(new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            UpdateInstallation.DownloadInstallerAsync(new SecureDownloader(client), info, _dir, null, CancellationToken.None));
    }

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return new HttpResponseMessage();
        }
    }
}
