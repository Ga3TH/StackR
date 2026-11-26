using System.Net;
using System.Security.Cryptography;
using StackR.Core.Download;

namespace StackR.Core.Tests.Download;

public sealed class SecureDownloaderTests : IDisposable
{
    private static readonly Uri HttpsUrl = new("https://example.org/php-8.4.zip");

    private readonly string _tmpDir = Path.Combine(Path.GetTempPath(), "stackr-tests-" + Guid.NewGuid().ToString("N"));
    private readonly byte[] _payload = CreatePayload(300_000);

    public void Dispose()
    {
        if (Directory.Exists(_tmpDir))
        {
            Directory.Delete(_tmpDir, recursive: true);
        }
    }

    [Fact]
    public async Task Download_CorrectHash_ReturnsPath()
    {
        using var handler = new FakeHandler(_ => Ok(_payload));
        using var client = new HttpClient(handler);
        var progress = new SyncProgress();

        var path = await new SecureDownloader(client)
            .DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload), _tmpDir, progress, CancellationToken.None);

        Assert.True(File.Exists(path));
        Assert.Equal(
            Path.GetFullPath(_tmpDir),
            Path.GetDirectoryName(Path.GetFullPath(path)),
            ignoreCase: true);
        Assert.Equal(_payload, await File.ReadAllBytesAsync(path));
        Assert.Single(Directory.GetFiles(_tmpDir));
        Assert.NotEmpty(progress.Reports);
        Assert.Equal(_payload.Length, progress.Reports[^1].BytesReceived);
        Assert.Equal(_payload.Length, progress.Reports[^1].TotalBytes);
    }

    [Fact]
    public async Task Download_SendsStackRUserAgent()
    {
        using var handler = new FakeHandler(_ => Ok(_payload));
        using var client = new HttpClient(handler);

        await new SecureDownloader(client)
            .DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload), _tmpDir, null, CancellationToken.None);

        var request = Assert.Single(handler.Requests);
        var product = Assert.Single(request.Headers.UserAgent).Product;
        Assert.NotNull(product);
        Assert.Equal("StackR", product.Name);
        Assert.False(string.IsNullOrWhiteSpace(product.Version));
    }

    [Fact]
    public async Task Download_UppercaseHash_IsAccepted()
    {
        using var handler = new FakeHandler(_ => Ok(_payload));
        using var client = new HttpClient(handler);

        var path = await new SecureDownloader(client)
            .DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload).ToUpperInvariant(), _tmpDir, null, CancellationToken.None);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Mismatch_DeletesFile()
    {
        using var handler = new FakeHandler(_ => Ok(_payload));
        using var client = new HttpClient(handler);
        var wrongHash = Sha256Hex([1, 2, 3]);

        await Assert.ThrowsAsync<IntegrityException>(() => new SecureDownloader(client)
            .DownloadVerifiedAsync(HttpsUrl, wrongHash, _tmpDir, null, CancellationToken.None));

        Assert.Single(handler.Requests);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_tmpDir));
    }

    [Fact]
    public async Task Download_Cancelled_DeletesFile()
    {
        using var cts = new CancellationTokenSource();
        using var handler = new FakeHandler(_ => Ok(_payload));
        using var client = new HttpClient(handler);
        // Annule dès le premier bloc reçu : le fichier temporaire existe déjà à ce moment.
        var progress = new SyncProgress(_ => cts.Cancel());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SecureDownloader(client)
            .DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload), _tmpDir, progress, cts.Token));

        Assert.NotEmpty(progress.Reports);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_tmpDir));
    }

    [Fact]
    public async Task Download_HttpUrl_Throws()
    {
        using var handler = new FakeHandler(_ => Ok(_payload));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<ArgumentException>(() => new SecureDownloader(client)
            .DownloadVerifiedAsync(new Uri("http://example.org/php.zip"), Sha256Hex(_payload), _tmpDir, null, CancellationToken.None));

        Assert.Empty(handler.Requests);
        Assert.False(Directory.Exists(_tmpDir) && Directory.EnumerateFileSystemEntries(_tmpDir).Any());
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("zz00000000000000000000000000000000000000000000000000000000000000")]
    [InlineData(" 0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task Download_MalformedHash_ThrowsBeforeRequest(string hash)
    {
        using var handler = new FakeHandler(_ => Ok(_payload));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => new SecureDownloader(client)
            .DownloadVerifiedAsync(HttpsUrl, hash, _tmpDir, null, CancellationToken.None));

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Download_RedirectedToHttp_ThrowsAndWritesNothing()
    {
        using var handler = new FakeHandler(_ =>
        {
            var response = Ok(_payload);
            // Simule une redirection finale vers une URL en clair.
            response.RequestMessage = new HttpRequestMessage(HttpMethod.Get, new Uri("http://mirror.example.org/php.zip"));
            return response;
        });
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<IntegrityException>(() => new SecureDownloader(client)
            .DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload), _tmpDir, null, CancellationToken.None));

        Assert.False(Directory.Exists(_tmpDir) && Directory.EnumerateFileSystemEntries(_tmpDir).Any());
    }

    [Fact]
    public async Task Download_HttpError_ThrowsAndWritesNothing()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => new SecureDownloader(client)
            .DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload), _tmpDir, null, CancellationToken.None));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Contains("Téléchargement impossible", ex.Message, StringComparison.Ordinal);
        Assert.Contains("404", ex.Message, StringComparison.Ordinal);
        Assert.False(Directory.Exists(_tmpDir) && Directory.EnumerateFileSystemEntries(_tmpDir).Any());
    }

    [Fact]
    public async Task Download_HostUnreachable_ExplainsInFrench()
    {
        using var handler = new FakeHandler(_ => throw new HttpRequestException("No such host is known."));
        using var client = new HttpClient(handler);

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() => new SecureDownloader(client)
            .DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload), _tmpDir, null, CancellationToken.None));

        Assert.Contains("injoignable", ex.Message, StringComparison.Ordinal);
        Assert.IsType<HttpRequestException>(ex.InnerException);
    }

    [Fact]
    public async Task Download_TwoCalls_UseDistinctFiles()
    {
        using var handler = new FakeHandler(_ => Ok(_payload));
        using var client = new HttpClient(handler);
        var downloader = new SecureDownloader(client);

        var first = await downloader.DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload), _tmpDir, null, CancellationToken.None);
        var second = await downloader.DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload), _tmpDir, null, CancellationToken.None);

        Assert.NotEqual(first, second, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(2, Directory.GetFiles(_tmpDir).Length);
    }

    private static byte[] CreatePayload(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < bytes.Length; i++)
        {
            bytes[i] = (byte)(i * 31 % 251);
        }

        return bytes;
    }

    private static string Sha256Hex(byte[] data) => Convert.ToHexStringLower(SHA256.HashData(data));

    private static HttpResponseMessage Ok(byte[] body) => new(HttpStatusCode.OK)
    {
        Content = new ByteArrayContent(body),
    };

    private sealed class FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(respond(request));
        }
    }

    /// <summary>Progression synchrone (contrairement à <see cref="Progress{T}"/>), pour des assertions déterministes.</summary>
    private sealed class SyncProgress(Action<DownloadProgress>? onReport = null) : IProgress<DownloadProgress>
    {
        public List<DownloadProgress> Reports { get; } = [];

        public void Report(DownloadProgress value)
        {
            Reports.Add(value);
            onReport?.Invoke(value);
        }
    }

    [Fact]
    public async Task Download_ExceedsMaxBytes_ThrowsAndDeletesFile()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new NonSeekableStream(_payload)),
        });
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<IntegrityException>(() => new SecureDownloader(client)
            .DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload), _tmpDir, null, CancellationToken.None, maxBytes: 100_000));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_tmpDir));
    }

    [Fact]
    public async Task Download_DeclaredLengthExceedsMaxBytes_Throws()
    {
        using var handler = new FakeHandler(_ => Ok(_payload));
        using var client = new HttpClient(handler);

        await Assert.ThrowsAsync<IntegrityException>(() => new SecureDownloader(client)
            .DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload), _tmpDir, null, CancellationToken.None, maxBytes: 10));
    }

    [Fact]
    public async Task Download_AtMaxBytes_Succeeds()
    {
        using var handler = new FakeHandler(_ => Ok(_payload));
        using var client = new HttpClient(handler);

        var path = await new SecureDownloader(client)
            .DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload), _tmpDir, null, CancellationToken.None, maxBytes: _payload.Length);

        Assert.True(File.Exists(path));
    }

    [Fact]
    public async Task Download_StalledBody_TimesOutAndDeletesFile()
    {
        using var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new StallingStream(_payload.AsMemory(0, 1000).ToArray())),
        });
        using var client = new HttpClient(handler);
        var downloader = new SecureDownloader(client, TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAsync<TimeoutException>(() => downloader
            .DownloadVerifiedAsync(HttpsUrl, Sha256Hex(_payload), _tmpDir, null, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_tmpDir));
    }

    // Renvoie quelques octets puis ne répond plus (jusqu'à annulation).
    private sealed class StallingStream(byte[] first) : Stream
    {
        private bool _served;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_served)
            {
                _served = true;
                first.CopyTo(buffer);
                return first.Length;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }
    }

    // Flux sans longueur connue : pas de Content-Length, le plafond doit jouer pendant la lecture.
    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
