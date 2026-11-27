using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using StackR.Core.Components;
using StackR.Core.Download;
using StackR.Core.Manifests;
using StackR.Core.Paths;

namespace StackR.Core.Tests.Components;

// Paquets embarqués (packages\ à côté de l'exécutable) : installation hors ligne, repli sur le téléchargement.
public sealed class ComponentInstallerPackagesTests : IDisposable
{
    private const string FileName = "tool-1.2.zip";
    private readonly TempDirectory _root = new();
    private readonly TempDirectory _packages = new();
    private readonly StackRPaths _paths;

    public ComponentInstallerPackagesTests() => _paths = new StackRPaths(_root.Path);

    public void Dispose()
    {
        _root.Dispose();
        _packages.Dispose();
    }

    private static byte[] MakeZip(string content)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var w = new StreamWriter(zip.CreateEntry("tool.exe").Open());
            w.Write(content);
        }

        return ms.ToArray();
    }

    private static ComponentRelease Release(byte[] payload) =>
        new(ComponentKind.Mailpit, "1.2", "stable", new Uri("https://example.org/x/" + FileName),
            Convert.ToHexStringLower(SHA256.HashData(payload)), payload.Length, PackageType.Zip, [], null);

    private ComponentInstaller Installer(CountingHandler handler) =>
        new(new SecureDownloader(new HttpClient(handler)), _paths, _packages.Path);

    [Fact]
    public async Task Install_LocalPackageWithGoodHash_InstallsWithoutAnyHttpCall()
    {
        var payload = MakeZip("local");
        File.WriteAllBytes(Path.Combine(_packages.Path, FileName), payload);
        var handler = new CountingHandler([]);

        await Installer(handler).InstallAsync(Release(payload), null, CancellationToken.None);

        Assert.Equal(0, handler.Calls);
        Assert.Equal("local", File.ReadAllText(Path.Combine(_paths.ComponentDir("mailpit", "1.2"), "tool.exe")));
        Assert.True(File.Exists(Path.Combine(_packages.Path, FileName)), "le paquet embarqué ne doit pas être supprimé");
    }

    [Fact]
    public async Task Install_LocalPackageWithBadHash_FallsBackToVerifiedDownload()
    {
        var good = MakeZip("réseau");
        File.WriteAllBytes(Path.Combine(_packages.Path, FileName), MakeZip("altéré"));
        var handler = new CountingHandler(good);

        await Installer(handler).InstallAsync(Release(good), null, CancellationToken.None);

        Assert.Equal(1, handler.Calls);
        Assert.Equal("réseau", File.ReadAllText(Path.Combine(_paths.ComponentDir("mailpit", "1.2"), "tool.exe")));
    }

    [Fact]
    public async Task Install_LocalPackageWithBadHash_AndBadDownload_FailsCleanly()
    {
        var good = MakeZip("attendu");
        File.WriteAllBytes(Path.Combine(_packages.Path, FileName), MakeZip("altéré"));

        await Assert.ThrowsAsync<IntegrityException>(() =>
            Installer(new CountingHandler(MakeZip("autre altéré"))).InstallAsync(Release(good), null, CancellationToken.None));

        Assert.False(new ComponentStore(_paths, _ => null).IsInstalled(ComponentKind.Mailpit, "1.2"));
        Assert.True(File.Exists(Path.Combine(_packages.Path, FileName)));
    }

    [Fact]
    public async Task Install_NoLocalPackage_Downloads()
    {
        var payload = MakeZip("dl");
        var handler = new CountingHandler(payload);

        await Installer(handler).InstallAsync(Release(payload), null, CancellationToken.None);

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public void LocalPackagePath_UsesFileNameOfReleaseUrl()
    {
        var payload = MakeZip("x");
        var installer = Installer(new CountingHandler([]));
        Assert.Null(installer.LocalPackagePath(Release(payload)));

        File.WriteAllBytes(Path.Combine(_packages.Path, FileName), payload);
        Assert.Equal(Path.Combine(_packages.Path, FileName), installer.LocalPackagePath(Release(payload)));
    }

    private sealed class CountingHandler(byte[] body) : HttpMessageHandler
    {
        public int Calls { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body), RequestMessage = request });
        }
    }
}
