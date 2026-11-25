using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using StackR.Core.Components;
using StackR.Core.Download;
using StackR.Core.Manifests;
using StackR.Core.Paths;

namespace StackR.Core.Tests.Components;

public sealed class ComponentInstallerTests : IDisposable
{
    private readonly TempDirectory _root = new();
    private readonly StackRPaths _paths;

    public ComponentInstallerTests() => _paths = new StackRPaths(_root.Path);

    public void Dispose() => _root.Dispose();

    private static byte[] MakeZip(string root)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var e = zip.CreateEntry($"{root}/php.exe");
            using var w = new StreamWriter(e.Open());
            w.Write("binaire");
        }

        return ms.ToArray();
    }

    private static string Sha(byte[] b) => Convert.ToHexStringLower(SHA256.HashData(b));

    private static ComponentRelease Release(
        ComponentKind k,
        string version,
        byte[] payload,
        string? sha = null,
        PackageType t = PackageType.Zip,
        string? root = "php-8.4.1",
        string file = "php.zip") =>
        new(k, version, "stable", new Uri("https://example.org/" + file), sha ?? Sha(payload), payload.Length, t, [], root);

    private ComponentInstaller Installer(byte[] payload) =>
        new(new SecureDownloader(new HttpClient(new FakeHandler(payload))), _paths);

    [Fact]
    public async Task Install_Success_WritesMarker()
    {
        var payload = MakeZip("php-8.4.1");
        var r = Release(ComponentKind.Php, "8.4.1", payload);
        var steps = new List<string>();

        await Installer(payload).InstallAsync(r, new SyncProgress(steps), CancellationToken.None);

        var dir = _paths.ComponentDir("php", "8.4.1");
        Assert.True(File.Exists(Path.Combine(dir, "php.exe")));
        var marker = Path.Combine(dir, ".stackr-installed");
        using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(marker));
        Assert.Equal("8.4.1", doc.RootElement.GetProperty("version").GetString());
        Assert.Equal(r.Sha256, doc.RootElement.GetProperty("sha256").GetString());
        Assert.True(doc.RootElement.TryGetProperty("date", out _));
        Assert.Equal("Terminé", steps[^1]);
        Assert.Contains("Téléchargement", steps);
        Assert.Contains("Extraction", steps);
        Assert.Empty(Directory.GetFileSystemEntries(_paths.Tmp));
        Assert.True(new ComponentStore(_paths, _ => null).IsInstalled(ComponentKind.Php, "8.4.1"));
    }

    [Fact]
    public async Task Install_Failure_LeavesNoPartialFolder()
    {
        var payload = MakeZip("php-8.4.1");
        var r = Release(ComponentKind.Php, "8.4.1", payload, sha: new string('0', 64));

        await Assert.ThrowsAsync<IntegrityException>(() =>
            Installer(payload).InstallAsync(r, null, CancellationToken.None));

        var dir = _paths.ComponentDir("php", "8.4.1");
        Assert.False(Directory.Exists(dir));
        Assert.False(Directory.Exists(dir + ".partial"));
        if (Directory.Exists(_paths.Tmp))
        {
            Assert.Empty(Directory.GetFileSystemEntries(_paths.Tmp));
        }
    }

    [Fact]
    public async Task Install_AlreadyInstalled_IsNoOp()
    {
        var payload = MakeZip("php-8.4.1");
        var r = Release(ComponentKind.Php, "8.4.1", payload);
        var inst = Installer(payload);
        await inst.InstallAsync(r, null, CancellationToken.None);

        // Aucun téléchargement : une empreinte fausse ne lève rien.
        await inst.InstallAsync(r with { Sha256 = new string('0', 64) }, null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(_paths.ComponentDir("php", "8.4.1"), ".stackr-installed")));
    }

    [Fact]
    public async Task Install_FilePackage_CopiesFile()
    {
        byte[] payload = [1, 2, 3, 4];
        var r = Release(ComponentKind.Composer, "2.8.0", payload, t: PackageType.File, root: null, file: "composer.phar");

        await Installer(payload).InstallAsync(r, null, CancellationToken.None);

        var dir = _paths.ComponentDir("composer", "2.8.0");
        Assert.Equal(payload, await File.ReadAllBytesAsync(Path.Combine(dir, "composer.phar")));
        Assert.True(File.Exists(Path.Combine(dir, ".stackr-installed")));
    }

    private sealed class SyncProgress(List<string> steps) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value)
        {
            if (steps.Count == 0 || steps[^1] != value.Stage)
            {
                steps.Add(value.Stage);
            }
        }
    }

    private sealed class FakeHandler(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body), RequestMessage = request });
    }
}
