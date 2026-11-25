using System.Text;
using StackR.Core.Hosts;

namespace StackR.Core.Tests.Hosts;

public sealed class HostsFileTests : IDisposable
{
    private const string BlockA = "# >>> StackR\r\n127.0.0.1 a.test\r\n# <<< StackR\r\n";

    private readonly string _dir;
    private readonly string _hosts;

    public HostsFileTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "stackr-hosts-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _hosts = Path.Combine(_dir, "hosts");
    }

    public void Dispose()
    {
        foreach (var f in Directory.EnumerateFiles(_dir))
        {
            File.SetAttributes(f, FileAttributes.Normal);
        }

        Directory.Delete(_dir, recursive: true);
    }

    private string Backup => _hosts + ".stackr.bak";

    [Fact]
    public void DefaultPath_PointsToWindowsEtcHosts()
    {
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        Assert.Equal(Path.Combine(windows, "System32", "drivers", "etc", "hosts"), HostsFile.DefaultPath, ignoreCase: true);
    }

    [Fact]
    public void NeedsUpdate_BlockMissing_ReturnsTrue()
    {
        File.WriteAllText(_hosts, "127.0.0.1 localhost\r\n");

        Assert.True(HostsFile.NeedsUpdate(["a.test"], _hosts));
    }

    [Fact]
    public void NeedsUpdate_UpToDate_ReturnsFalse()
    {
        File.WriteAllText(_hosts, "127.0.0.1 localhost\r\n" + BlockA);

        Assert.False(HostsFile.NeedsUpdate(["A.test"], _hosts));
    }

    [Fact]
    public void NeedsUpdate_DifferentHosts_ReturnsTrue()
    {
        File.WriteAllText(_hosts, BlockA);

        Assert.True(HostsFile.NeedsUpdate(["b.test"], _hosts));
    }

    [Fact]
    public void NeedsUpdate_InvalidHost_Throws()
    {
        File.WriteAllText(_hosts, "");

        Assert.Throws<ArgumentException>(() => HostsFile.NeedsUpdate(["evil.com"], _hosts));
    }

    [Fact]
    public void Write_AddsBlockAndPreservesContent()
    {
        File.WriteAllText(_hosts, "127.0.0.1 localhost\r\n");

        HostsFile.Write(["b.test", "a.test"], _hosts);

        Assert.Equal(
            "127.0.0.1 localhost\r\n# >>> StackR\r\n127.0.0.1 a.test\r\n127.0.0.1 b.test\r\n# <<< StackR\r\n",
            File.ReadAllText(_hosts));
        Assert.False(HostsFile.NeedsUpdate(["a.test", "b.test"], _hosts));
    }

    [Fact]
    public void Write_CreatesBackupOnlyOnce()
    {
        var original = "# original\r\n127.0.0.1 localhost\r\n"u8.ToArray();
        File.WriteAllBytes(_hosts, original);

        HostsFile.Write(["a.test"], _hosts);
        HostsFile.Write(["b.test"], _hosts);

        Assert.Equal(original, File.ReadAllBytes(Backup));
        Assert.Contains("127.0.0.1 b.test", File.ReadAllText(_hosts), StringComparison.Ordinal);
    }

    [Fact]
    public void Write_NoChange_DoesNotTouchFileNorCreateBackup()
    {
        File.WriteAllText(_hosts, BlockA);
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(_hosts, stamp);

        HostsFile.Write(["a.test"], _hosts);

        Assert.Equal(stamp, File.GetLastWriteTimeUtc(_hosts));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void Write_InvalidHost_LeavesFileUntouched()
    {
        var original = "127.0.0.1 localhost\r\n"u8.ToArray();
        File.WriteAllBytes(_hosts, original);

        Assert.Throws<ArgumentException>(() => HostsFile.Write(["a.test", "a.test\r\n1.2.3.4 bank.com"], _hosts));

        Assert.Equal(original, File.ReadAllBytes(_hosts));
        Assert.Equal([_hosts], Directory.GetFiles(_dir));
    }

    [Fact]
    public void Write_LeavesNoTemporaryFiles()
    {
        File.WriteAllText(_hosts, "");

        HostsFile.Write(["a.test"], _hosts);
        HostsFile.Write([], _hosts);

        Assert.Equal(
            new[] { _hosts, Backup }.Order(StringComparer.OrdinalIgnoreCase),
            Directory.GetFiles(_dir).Order(StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public void Write_AnsiBytes_PreservedExactly()
    {
        // 0xE9 = « é » en Windows-1252 : UTF-8 invalide, doit être conservé à l'octet près.
        byte[] prefix = [.. "# caf"u8, 0xE9, .. "\r\n127.0.0.1 localhost\r\n"u8];
        File.WriteAllBytes(_hosts, prefix);

        HostsFile.Write(["a.test"], _hosts);

        Assert.Equal([.. prefix, .. Encoding.ASCII.GetBytes(BlockA)], File.ReadAllBytes(_hosts));
    }

    [Fact]
    public void Write_Utf8WithoutBom_PreservedExactly()
    {
        var prefix = "# Hôtes — été\r\n"u8.ToArray();
        File.WriteAllBytes(_hosts, prefix);

        HostsFile.Write(["a.test"], _hosts);

        Assert.Equal([.. prefix, .. Encoding.ASCII.GetBytes(BlockA)], File.ReadAllBytes(_hosts));
    }

    [Fact]
    public void Write_Utf8Bom_PreservesBom()
    {
        byte[] original = [0xEF, 0xBB, 0xBF, .. "# été\r\n"u8];
        File.WriteAllBytes(_hosts, original);

        HostsFile.Write(["a.test"], _hosts);

        Assert.Equal([.. original, .. Encoding.ASCII.GetBytes(BlockA)], File.ReadAllBytes(_hosts));
    }

    [Fact]
    public void Write_Utf16LeBom_PreservesEncoding()
    {
        var utf16 = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
        const string text = "# été\r\n127.0.0.1 localhost\r\n";
        File.WriteAllBytes(_hosts, [.. utf16.GetPreamble(), .. utf16.GetBytes(text)]);

        HostsFile.Write(["a.test"], _hosts);

        Assert.Equal([.. utf16.GetPreamble(), .. utf16.GetBytes(text + BlockA)], File.ReadAllBytes(_hosts));
    }

    [Fact]
    public void Write_Utf16BeBom_PreservesEncoding()
    {
        var utf16 = new UnicodeEncoding(bigEndian: true, byteOrderMark: true);
        const string text = "# été\r\n";
        File.WriteAllBytes(_hosts, [.. utf16.GetPreamble(), .. utf16.GetBytes(text)]);

        HostsFile.Write(["a.test"], _hosts);

        Assert.Equal([.. utf16.GetPreamble(), .. utf16.GetBytes(text + BlockA)], File.ReadAllBytes(_hosts));
    }

    [Fact]
    public void Write_Utf32Bom_RefusesAndLeavesFileUntouched()
    {
        byte[] original = [0xFF, 0xFE, 0x00, 0x00, (byte)'#', 0, 0, 0];
        File.WriteAllBytes(_hosts, original);

        Assert.Throws<InvalidDataException>(() => HostsFile.Write(["a.test"], _hosts));

        Assert.Equal(original, File.ReadAllBytes(_hosts));
        Assert.Equal([_hosts], Directory.GetFiles(_dir));
    }

    [Fact]
    public void Write_ReadOnlyFile_ThrowsAndLeavesFileUntouched()
    {
        var original = "127.0.0.1 localhost\r\n"u8.ToArray();
        File.WriteAllBytes(_hosts, original);
        File.SetAttributes(_hosts, FileAttributes.ReadOnly);

        Assert.Throws<UnauthorizedAccessException>(() => HostsFile.Write(["a.test"], _hosts));

        Assert.Equal(original, File.ReadAllBytes(_hosts));
        Assert.Equal([_hosts], Directory.GetFiles(_dir));
    }

    [Fact]
    public void Write_MissingFile_CreatesFileWithBlock()
    {
        HostsFile.Write(["a.test"], _hosts);

        Assert.Equal(Encoding.ASCII.GetBytes(BlockA), File.ReadAllBytes(_hosts));
        Assert.False(File.Exists(Backup));
    }

    [Fact]
    public void Write_EmptyList_RemovesBlock()
    {
        File.WriteAllText(_hosts, "127.0.0.1 localhost\r\n" + BlockA);

        HostsFile.Write([], _hosts);

        Assert.Equal("127.0.0.1 localhost\r\n", File.ReadAllText(_hosts));
    }
}
