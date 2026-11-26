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

    [Theory]
    [InlineData("Téléchargement")]
    [InlineData("Vérification")]
    [InlineData("Extraction")]
    public async Task Install_Cancelled_LeavesNothingInBinOrTmp(string stage)
    {
        var payload = MakeZip("php-8.4.1");
        var r = Release(ComponentKind.Php, "8.4.1", payload);
        using var cts = new CancellationTokenSource();
        var progress = new ActionProgress(p =>
        {
            if (p.Stage == stage)
            {
                cts.Cancel();
            }
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Installer(payload).InstallAsync(r, progress, cts.Token));

        AssertNothingLeft("php", "8.4.1");
    }

    [Fact]
    public async Task Install_InterruptedDownload_LeavesNothing_AndExplainsInFrench()
    {
        var payload = MakeZip("php-8.4.1");
        var r = Release(ComponentKind.Php, "8.4.1", payload);
        var installer = new ComponentInstaller(new SecureDownloader(new HttpClient(new BrokenStreamHandler(payload.Length))), _paths);

        var ex = await Assert.ThrowsAsync<IOException>(() => installer.InstallAsync(r, null, CancellationToken.None));

        Assert.Contains("Téléchargement interrompu", ex.Message, StringComparison.Ordinal);
        AssertNothingLeft("php", "8.4.1");
    }

    [Fact]
    public async Task Install_LeftoverFolderWithoutMarker_IsReplaced()
    {
        // Reste d'une désinstallation interrompue : dossier sans marqueur, fichier verrouillé à l'époque.
        var dir = _paths.ComponentDir("php", "8.4.1");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "reste.dll"), "ancien");
        var payload = MakeZip("php-8.4.1");

        await Installer(payload).InstallAsync(Release(ComponentKind.Php, "8.4.1", payload), null, CancellationToken.None);

        Assert.True(File.Exists(Path.Combine(dir, "php.exe")));
        Assert.False(File.Exists(Path.Combine(dir, "reste.dll")));
        Assert.True(File.Exists(Path.Combine(dir, ".stackr-installed")));
    }

    [Fact]
    public async Task Install_PublishedMeanwhileByConcurrentInstall_Succeeds()
    {
        var payload = MakeZip("php-8.4.1");
        var dir = _paths.ComponentDir("php", "8.4.1");
        // Juste avant la publication, une autre installation de la même version publie son dossier.
        var progress = new ActionProgress(p =>
        {
            if (p is { Stage: "Extraction", Percent: 100 })
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir, "php.exe"), "gagnant");
                File.WriteAllText(Path.Combine(dir, ".stackr-installed"), "{}");
            }
        });

        await Installer(payload).InstallAsync(Release(ComponentKind.Php, "8.4.1", payload), progress, CancellationToken.None);

        Assert.Equal("gagnant", File.ReadAllText(Path.Combine(dir, "php.exe")));
        Assert.Empty(Directory.GetFileSystemEntries(_paths.Tmp));
    }

    private void AssertNothingLeft(string folder, string version)
    {
        var dir = _paths.ComponentDir(folder, version);
        Assert.False(Directory.Exists(dir));
        Assert.False(Directory.Exists(dir + ".partial"));
        if (Directory.Exists(_paths.Tmp))
        {
            Assert.Empty(Directory.GetFileSystemEntries(_paths.Tmp));
        }
    }

    private sealed class ActionProgress(Action<InstallProgress> report) : IProgress<InstallProgress>
    {
        public void Report(InstallProgress value) => report(value);
    }

    // Le serveur annonce la taille complète mais coupe la connexion à mi-chemin.
    private sealed class BrokenStreamHandler(int length) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var content = new StreamContent(new BrokenStream(length / 2));
            content.Headers.ContentLength = length;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content, RequestMessage = request });
        }
    }

    private sealed class BrokenStream(int bytesBeforeFailure) : Stream
    {
        private int _sent;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => _sent; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_sent >= bytesBeforeFailure)
            {
                throw new IOException("connection reset");
            }

            var n = Math.Min(count, bytesBeforeFailure - _sent);
            Array.Fill(buffer, (byte)1, offset, n);
            _sent += n;
            return n;
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
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

    [Fact]
    public void SweepStaleTemp_RemovesOnlyOldLeftovers()
    {
        Directory.CreateDirectory(_paths.Tmp);
        var oldStage = Directory.CreateDirectory(Path.Combine(_paths.Tmp, "install-old")).FullName;
        File.WriteAllText(Path.Combine(oldStage, "a.bin"), "x");
        var oldDownload = Path.Combine(_paths.Tmp, "abc.download");
        var oldCnf = Path.Combine(_paths.Tmp, "client-abc.cnf");
        var freshStage = Directory.CreateDirectory(Path.Combine(_paths.Tmp, "install-fresh")).FullName;
        var other = Path.Combine(_paths.Tmp, "state.json");
        File.WriteAllText(oldDownload, "x");
        File.WriteAllText(oldCnf, "x");
        File.WriteAllText(other, "{}");
        var past = DateTime.UtcNow.AddHours(-3);
        Directory.SetLastWriteTimeUtc(oldStage, past);
        File.SetLastWriteTimeUtc(oldDownload, past);
        File.SetLastWriteTimeUtc(oldCnf, past);
        File.SetLastWriteTimeUtc(other, past);

        Installer([]).SweepStaleTemp(TimeSpan.FromHours(1));

        Assert.False(Directory.Exists(oldStage));
        Assert.False(File.Exists(oldDownload));
        Assert.False(File.Exists(oldCnf));
        Assert.True(Directory.Exists(freshStage));
        Assert.True(File.Exists(other));
    }

    [Fact]
    public void SweepStaleTemp_MissingTmp_DoesNothing()
    {
        Installer([]).SweepStaleTemp(TimeSpan.Zero);
    }
}
