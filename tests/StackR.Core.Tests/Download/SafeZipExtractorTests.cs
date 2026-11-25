using System.IO.Compression;
using System.Text;
using StackR.Core.Download;

namespace StackR.Core.Tests.Download;

public sealed class SafeZipExtractorTests : IDisposable
{
    // Espaces et accents volontaires : la racine StackR peut en contenir.
    private readonly string _root = Path.Combine(Path.GetTempPath(), "stackr tests é " + Guid.NewGuid().ToString("N"));

    public SafeZipExtractorTests()
    {
        Directory.CreateDirectory(_root);
    }

    private string ZipPath => Path.Combine(_root, "archive.zip");

    private string Dest => Path.Combine(_root, "bin", "php", "8.4.1");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void Extract_NoArchiveRoot_ExtractsEverything()
    {
        CreateZip(("php.exe", "exe"), ("ext/", null), ("ext/php_curl.dll", "curl"));

        SafeZipExtractor.Extract(ZipPath, Dest, null);

        Assert.Equal("exe", File.ReadAllText(Path.Combine(Dest, "php.exe")));
        Assert.Equal("curl", File.ReadAllText(Path.Combine(Dest, "ext", "php_curl.dll")));
        Assert.False(Directory.Exists(Dest + ".partial"));
    }

    [Theory]
    [InlineData("php-8.4.1-Win32")]
    [InlineData("PHP-8.4.1-WIN32")]
    [InlineData("php-8.4.1-Win32/")]
    [InlineData("php-8.4.1-Win32\\")]
    public void Extract_ArchiveRoot_Flattens(string archiveRoot)
    {
        CreateZip(
            ("php-8.4.1-Win32/", null),
            ("php-8.4.1-Win32/php.exe", "exe"),
            ("php-8.4.1-Win32/ext/php_curl.dll", "curl"),
            ("php-8.4.1-Win32-extra/other.txt", "autre racine"),
            ("README.txt", "hors racine"));

        SafeZipExtractor.Extract(ZipPath, Dest, archiveRoot);

        Assert.Equal("exe", File.ReadAllText(Path.Combine(Dest, "php.exe")));
        Assert.Equal("curl", File.ReadAllText(Path.Combine(Dest, "ext", "php_curl.dll")));
        Assert.False(File.Exists(Path.Combine(Dest, "README.txt")));
        Assert.False(File.Exists(Path.Combine(Dest, "other.txt")));
        Assert.False(Directory.Exists(Path.Combine(Dest, "php-8.4.1-Win32")));
        Assert.Equal(["ext", "php.exe"], Directory.EnumerateFileSystemEntries(Dest).Select(Path.GetFileName).Order(StringComparer.Ordinal));
        Assert.False(Directory.Exists(Dest + ".partial"));
    }

    [Fact]
    public void Extract_ArchiveRootMissing_Throws_AndLeavesNoDest()
    {
        CreateZip(("other/php.exe", "exe"));

        Assert.Throws<IntegrityException>(() => SafeZipExtractor.Extract(ZipPath, Dest, "php-8.4.1-Win32"));

        AssertNoDest();
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("..\\evil.txt")]
    [InlineData("sub/../../evil.txt")]
    [InlineData("sub/..")]
    [InlineData("/evil.txt")]
    [InlineData("\\evil.txt")]
    [InlineData("C:/evil.txt")]
    [InlineData("C:evil.txt")]
    [InlineData("php.exe:hidden")]
    [InlineData("//server/share/evil.txt")]
    public void Extract_ZipSlip_Throws_AndLeavesNoDest(string maliciousName)
    {
        CreateZip(("good.txt", "ok"), (maliciousName, "pwned"), ("after.txt", "ok"));

        Assert.Throws<IntegrityException>(() => SafeZipExtractor.Extract(ZipPath, Dest, null));

        AssertNoDest();
        Assert.False(File.Exists(Path.Combine(_root, "bin", "php", "evil.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "bin", "evil.txt")));
    }

    [Theory]
    [InlineData("root/../../evil.txt")]
    [InlineData("root/../evil.txt")]
    [InlineData("../evil.txt")]
    public void Extract_ZipSlipWithArchiveRoot_Throws_AndLeavesNoDest(string maliciousName)
    {
        CreateZip(("root/good.txt", "ok"), (maliciousName, "pwned"));

        Assert.Throws<IntegrityException>(() => SafeZipExtractor.Extract(ZipPath, Dest, "root"));

        AssertNoDest();
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../root")]
    [InlineData("C:/root")]
    [InlineData("/")]
    public void Extract_InvalidArchiveRoot_Throws(string archiveRoot)
    {
        CreateZip(("root/good.txt", "ok"));

        Assert.ThrowsAny<ArgumentException>(() => SafeZipExtractor.Extract(ZipPath, Dest, archiveRoot));

        AssertNoDest();
    }

    [Fact]
    public void Extract_DuplicateEntryDifferingByCase_Throws_AndLeavesNoDest()
    {
        CreateZip(("php.ini", "original"), ("PHP.INI", "écrase"));

        Assert.Throws<IntegrityException>(() => SafeZipExtractor.Extract(ZipPath, Dest, null));

        AssertNoDest();
    }

    [Fact]
    public void Extract_DestExists_Throws_AndKeepsExistingContent()
    {
        CreateZip(("php.exe", "nouveau"));
        Directory.CreateDirectory(Dest);
        File.WriteAllText(Path.Combine(Dest, "php.exe"), "ancien");

        Assert.Throws<IOException>(() => SafeZipExtractor.Extract(ZipPath, Dest, null));

        Assert.Equal("ancien", File.ReadAllText(Path.Combine(Dest, "php.exe")));
        Assert.False(Directory.Exists(Dest + ".partial"));
    }

    [Fact]
    public void Extract_LeftoverPartial_IsReplaced()
    {
        CreateZip(("php.exe", "exe"));
        Directory.CreateDirectory(Dest + ".partial");
        File.WriteAllText(Path.Combine(Dest + ".partial", "stale.txt"), "reste d'une tentative interrompue");

        SafeZipExtractor.Extract(ZipPath, Dest, null);

        Assert.Equal("exe", File.ReadAllText(Path.Combine(Dest, "php.exe")));
        Assert.False(File.Exists(Path.Combine(Dest, "stale.txt")));
        Assert.False(Directory.Exists(Dest + ".partial"));
    }

    [Fact]
    public void Extract_CorruptZip_Throws_AndLeavesNoDest()
    {
        File.WriteAllText(ZipPath, "ceci n'est pas une archive zip");

        Assert.ThrowsAny<InvalidDataException>(() => SafeZipExtractor.Extract(ZipPath, Dest, null));

        AssertNoDest();
    }

    private void AssertNoDest()
    {
        Assert.False(Directory.Exists(Dest), "Le dossier de destination ne doit pas exister.");
        Assert.False(Directory.Exists(Dest + ".partial"), "Le dossier .partial doit être supprimé.");
    }

    private void CreateZip(params (string Name, string? Content)[] entries)
    {
        using var zip = ZipFile.Open(ZipPath, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = zip.CreateEntry(name);
            if (content is not null)
            {
                using var stream = entry.Open();
                stream.Write(Encoding.UTF8.GetBytes(content));
            }
        }
    }
}
