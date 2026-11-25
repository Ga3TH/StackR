using System.Security.Cryptography;
using System.Text;
using StackR.Core.Security;

namespace StackR.Core.Tests.Security;

public sealed class SecretStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "sécrets é");

    public void Dispose()
    {
        var root = Directory.GetParent(_dir)!.FullName;
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }

    [Fact]
    public void SetGet_RoundTrips()
    {
        var store = new SecretStore(_dir);
        store.Set("mysql-root", "pässw0rd-é");
        Assert.Equal("pässw0rd-é", store.Get("mysql-root"));
    }

    [Fact]
    public void SetBytesGetBytes_RoundTrips()
    {
        var store = new SecretStore(_dir);
        byte[] data = [0, 1, 2, 255, 128];
        store.SetBytes("ca-key", data);
        Assert.Equal(data, store.GetBytes("ca-key"));
    }

    [Fact]
    public void Get_Missing_ReturnsNull()
    {
        var store = new SecretStore(_dir);
        Assert.Null(store.Get("absent"));
        Assert.Null(store.GetBytes("absent"));
    }

    [Fact]
    public void Delete_RemovesSecret_AndMissingIsNoop()
    {
        var store = new SecretStore(_dir);
        store.Set("a", "x");
        store.Delete("a");
        Assert.Null(store.Get("a"));
        store.Delete("a");
    }

    [Fact]
    public void File_IsNotPlaintext()
    {
        var store = new SecretStore(_dir);
        store.Set("mysql-root", "SuperSecretValue123");
        var raw = File.ReadAllBytes(Path.Combine(_dir, "mysql-root.bin"));
        Assert.DoesNotContain("SuperSecretValue123", Encoding.UTF8.GetString(raw), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    [Fact]
    public void Set_Overwrites()
    {
        var store = new SecretStore(_dir);
        store.Set("k", "one");
        store.Set("k", "two");
        Assert.Equal("two", store.Get("k"));
    }

    [Fact]
    public void Get_CorruptFile_ThrowsCryptographicException()
    {
        var store = new SecretStore(_dir);
        Directory.CreateDirectory(_dir);
        File.WriteAllBytes(Path.Combine(_dir, "bad.bin"), [1, 2, 3, 4]);
        Assert.ThrowsAny<CryptographicException>(() => store.Get("bad"));
    }

    [Fact]
    public void Get_FileProtectedWithOtherName_Throws()
    {
        var store = new SecretStore(_dir);
        store.Set("one", "v");
        Directory.CreateDirectory(_dir);
        File.Copy(Path.Combine(_dir, "one.bin"), Path.Combine(_dir, "two.bin"));
        Assert.ThrowsAny<CryptographicException>(() => store.Get("two"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("UPPER")]
    [InlineData("a b")]
    [InlineData("../x")]
    [InlineData("a.b")]
    [InlineData("a\b")]
    public void InvalidName_Throws(string name)
    {
        var store = new SecretStore(_dir);
        Assert.Throws<ArgumentException>(() => store.Set(name, "v"));
        Assert.Throws<ArgumentException>(() => store.Get(name));
        Assert.Throws<ArgumentException>(() => store.Delete(name));
    }

    [Fact]
    public void Delete_FolderMissing_IsNoOp()
    {
        var store = new SecretStore(Path.Combine(_dir, "nope"));
        store.Delete("mysql-root");
    }
}
