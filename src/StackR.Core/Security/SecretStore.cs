using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace StackR.Core.Security;

/// <summary>Stockage de secrets chiffrés par DPAPI (CurrentUser), un fichier <c>&lt;nom&gt;.bin</c> par secret.</summary>
public sealed partial class SecretStore(string secretsDir)
{
    [GeneratedRegex("^[a-z0-9-]+$")]
    private static partial Regex NamePattern();

    public void Set(string name, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        try
        {
            SetBytes(name, bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public string? Get(string name)
    {
        var bytes = GetBytes(name);
        if (bytes is null)
        {
            return null;
        }

        try
        {
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(bytes);
        }
    }

    public void SetBytes(string name, byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var path = PathFor(name);
        Directory.CreateDirectory(secretsDir);
        var protectedBytes = ProtectedData.Protect(value, Entropy(name), DataProtectionScope.CurrentUser);
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllBytes(tmp, protectedBytes);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }

    public byte[]? GetBytes(string name)
    {
        var path = PathFor(name);
        if (!File.Exists(path))
        {
            return null;
        }

        // Fichier corrompu ou d'un autre utilisateur : CryptographicException propagée.
        return ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy(name), DataProtectionScope.CurrentUser);
    }

    public void Delete(string name)
    {
        var path = PathFor(name);
        File.Delete(path);
    }

    private static byte[] Entropy(string name) => Encoding.UTF8.GetBytes("StackR:" + name);

    private string PathFor(string name)
    {
        if (string.IsNullOrEmpty(name) || !NamePattern().IsMatch(name))
        {
            throw new ArgumentException("Nom de secret invalide (attendu : a-z, 0-9, '-').", nameof(name));
        }

        return Path.Combine(secretsDir, name + ".bin");
    }
}
