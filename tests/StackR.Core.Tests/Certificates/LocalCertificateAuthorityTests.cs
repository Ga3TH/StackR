using System.Formats.Asn1;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using StackR.Core.Certificates;
using StackR.Core.Paths;
using StackR.Core.Security;

namespace StackR.Core.Tests.Certificates;

public sealed class LocalCertificateAuthorityTests : IDisposable
{
    private const string NameConstraintsOid = "2.5.29.30";
    private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";

    private readonly string _base;
    private readonly StackRPaths _paths;
    private readonly SecretStore _secrets;

    public LocalCertificateAuthorityTests()
    {
        _base = Path.Combine(Path.GetTempPath(), "stackr-ca-" + Guid.NewGuid().ToString("N"));
        _paths = new StackRPaths(Path.Combine(_base, "Dossier é", "StackR"));
        _secrets = new SecretStore(_paths.Secrets);
    }

    public void Dispose()
    {
        if (Directory.Exists(_base))
        {
            Directory.Delete(_base, recursive: true);
        }
    }

    private LocalCertificateAuthority NewCa() => new(_paths, _secrets);

    private string CaCertPath => Path.Combine(_paths.Ssl, "ca.crt");

    private string SitesDir => Path.Combine(_paths.Ssl, "sites");

    // ---------- CA racine ----------

    [Fact]
    public void RootCa_HasCriticalNameConstraints()
    {
        using var root = NewCa().EnsureRootCa();

        var ext = Assert.Single(root.Extensions.Cast<X509Extension>(), e => e.Oid?.Value == NameConstraintsOid);
        Assert.True(ext.Critical);

        // NameConstraints ::= SEQUENCE { permittedSubtrees [0] GeneralSubtrees OPTIONAL, excludedSubtrees [1] ... }
        var reader = new AsnReader(ext.RawData, AsnEncodingRules.DER);
        var nc = reader.ReadSequence();
        reader.ThrowIfNotEmpty();

        var permitted = nc.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true));
        nc.ThrowIfNotEmpty(); // pas d'excludedSubtrees

        var dnsNames = new List<string>();
        var ipRanges = new List<string>();
        while (permitted.HasData)
        {
            var subtree = permitted.ReadSequence();
            var tag = subtree.PeekTag();
            Assert.Equal(TagClass.ContextSpecific, tag.TagClass);
            switch (tag.TagValue)
            {
                case 2:
                    dnsNames.Add(subtree.ReadCharacterString(UniversalTagNumber.IA5String, new Asn1Tag(TagClass.ContextSpecific, 2)));
                    break;
                case 7:
                    ipRanges.Add(Convert.ToHexString(subtree.ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 7))));
                    break;
                default:
                    Assert.Fail($"Type de GeneralName inattendu : [{tag.TagValue}]");
                    break;
            }

            // minimum (défaut 0) et maximum absents, conformément à RFC 5280.
            subtree.ThrowIfNotEmpty();
        }

        Assert.Equal(["test", "localhost"], dnsNames);
        Assert.Equal(["7F000000FF000000"], ipRanges);
    }

    [Fact]
    public void RootCa_HasExpectedShape()
    {
        using var root = NewCa().EnsureRootCa();

        Assert.Equal($"CN=StackR Local CA ({Environment.MachineName})", root.Subject);
        Assert.Equal(root.Subject, root.Issuer);
        Assert.False(root.HasPrivateKey);

        using var pub = root.GetECDsaPublicKey();
        Assert.NotNull(pub);
        Assert.Equal(256, pub.KeySize);

        var bc = Assert.Single(root.Extensions.OfType<X509BasicConstraintsExtension>());
        Assert.True(bc.Critical);
        Assert.True(bc.CertificateAuthority);
        Assert.True(bc.HasPathLengthConstraint);
        Assert.Equal(0, bc.PathLengthConstraint);

        var ku = Assert.Single(root.Extensions.OfType<X509KeyUsageExtension>());
        Assert.True(ku.Critical);
        Assert.Equal(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, ku.KeyUsages);

        Assert.Single(root.Extensions.OfType<X509SubjectKeyIdentifierExtension>());

        var now = DateTime.UtcNow;
        Assert.InRange(root.NotBefore.ToUniversalTime(), now.AddDays(-1).AddMinutes(-5), now.AddDays(-1).AddMinutes(5));
        Assert.InRange(root.NotAfter.ToUniversalTime(), now.AddYears(10).AddDays(-2), now.AddYears(10).AddDays(1));

        AssertRandomSerial(root);
    }

    [Fact]
    public void RootCa_FilesAreWrittenPublicCertOnly()
    {
        using var root = NewCa().EnsureRootCa();

        var pem = File.ReadAllText(CaCertPath);
        Assert.StartsWith("-----BEGIN CERTIFICATE-----", pem, StringComparison.Ordinal);
        Assert.DoesNotContain("PRIVATE KEY", pem, StringComparison.Ordinal);

        // La clé privée n'existe que chiffrée (DPAPI) dans le magasin de secrets.
        Assert.True(File.Exists(Path.Combine(_paths.Secrets, "ca-key.bin")));
        Assert.Empty(Directory.EnumerateFiles(_paths.Ssl, "*.key", SearchOption.AllDirectories));
        var pkcs8 = _secrets.GetBytes("ca-key");
        Assert.NotNull(pkcs8);
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(pkcs8, out _);
        Assert.Equal(root.PublicKey.ExportSubjectPublicKeyInfo(), key.ExportSubjectPublicKeyInfo());
        Assert.NotEqual(pkcs8, File.ReadAllBytes(Path.Combine(_paths.Secrets, "ca-key.bin")));
    }

    [Fact]
    public void RootCa_IsReusedOnSecondCall()
    {
        using var first = NewCa().EnsureRootCa();
        using var second = NewCa().EnsureRootCa();

        Assert.Equal(first.Thumbprint, second.Thumbprint);
    }

    [Fact]
    public void RootCa_KeyMissing_RegeneratesAndDeletesSiteCerts()
    {
        var ca = NewCa();
        using var first = ca.EnsureRootCa();
        var (crt, key) = ca.IssueSiteCertificate("monsite.test");

        _secrets.Delete("ca-key");
        using var second = ca.EnsureRootCa();

        Assert.NotEqual(first.Thumbprint, second.Thumbprint);
        Assert.False(File.Exists(crt));
        Assert.False(File.Exists(key));
    }

    [Fact]
    public void RootCa_KeyMismatch_Regenerates()
    {
        var ca = NewCa();
        using var first = ca.EnsureRootCa();

        using (var other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            _secrets.SetBytes("ca-key", other.ExportPkcs8PrivateKey());
        }

        using var second = ca.EnsureRootCa();

        Assert.NotEqual(first.Thumbprint, second.Thumbprint);
        using var key = ECDsa.Create();
        key.ImportPkcs8PrivateKey(_secrets.GetBytes("ca-key"), out _);
        Assert.Equal(second.PublicKey.ExportSubjectPublicKeyInfo(), key.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void RootCa_CertMissing_Regenerates()
    {
        var ca = NewCa();
        using var first = ca.EnsureRootCa();
        File.Delete(CaCertPath);

        using var second = ca.EnsureRootCa();

        Assert.NotEqual(first.Thumbprint, second.Thumbprint);
        Assert.True(File.Exists(CaCertPath));
    }

    [Fact]
    public void RootCa_CorruptCertFile_Regenerates()
    {
        var ca = NewCa();
        using var first = ca.EnsureRootCa();
        File.WriteAllText(CaCertPath, "pas un certificat");

        using var second = ca.EnsureRootCa();

        Assert.NotEqual(first.Thumbprint, second.Thumbprint);
    }

    [Fact]
    public void IsTrusted_NoCa_ReturnsFalse()
    {
        Assert.False(NewCa().IsTrusted());
        Assert.False(File.Exists(CaCertPath)); // IsTrusted ne crée pas de CA
    }

    // ---------- Certificats de site ----------

    [Theory]
    [InlineData("monsite.test")]
    [InlineData("localhost")]
    public void SiteCert_ChainsToRoot(string host)
    {
        var ca = NewCa();
        using var root = ca.EnsureRootCa();
        var (crtPath, _) = ca.IssueSiteCertificate(host);
        using var leaf = LoadPem(crtPath);

        using var chain = NewChain(root);
        var ok = chain.Build(leaf);

        Assert.True(ok, string.Join(", ", chain.ChainStatus.Select(s => s.Status)));
        Assert.Equal(2, chain.ChainElements.Count);
        Assert.Equal(root.Thumbprint, chain.ChainElements[1].Certificate.Thumbprint);
    }

    [Fact]
    public void SiteCert_HasSanAndServerAuth()
    {
        var ca = NewCa();
        using var root = ca.EnsureRootCa();
        var (crtPath, keyPath) = ca.IssueSiteCertificate("mon-projet.test");

        Assert.Equal(Path.Combine(SitesDir, "mon-projet.test.crt"), crtPath);
        Assert.Equal(Path.Combine(SitesDir, "mon-projet.test.key"), keyPath);

        using var leaf = LoadPem(crtPath);

        var san = Assert.Single(leaf.Extensions.OfType<X509SubjectAlternativeNameExtension>());
        Assert.Equal(["mon-projet.test"], san.EnumerateDnsNames());
        Assert.Empty(san.EnumerateIPAddresses());

        var eku = Assert.Single(leaf.Extensions.OfType<X509EnhancedKeyUsageExtension>());
        Assert.Equal([ServerAuthOid], eku.EnhancedKeyUsages.Cast<Oid>().Select(o => o.Value));

        var bc = Assert.Single(leaf.Extensions.OfType<X509BasicConstraintsExtension>());
        Assert.True(bc.Critical);
        Assert.False(bc.CertificateAuthority);

        var ku = Assert.Single(leaf.Extensions.OfType<X509KeyUsageExtension>());
        Assert.True(ku.Critical);
        Assert.Equal(X509KeyUsageFlags.DigitalSignature, ku.KeyUsages);

        var ski = Assert.Single(leaf.Extensions.OfType<X509SubjectKeyIdentifierExtension>());
        var aki = Assert.Single(leaf.Extensions.OfType<X509AuthorityKeyIdentifierExtension>());
        var rootSki = Assert.Single(root.Extensions.OfType<X509SubjectKeyIdentifierExtension>());
        Assert.Equal(rootSki.SubjectKeyIdentifierBytes.ToArray(), aki.KeyIdentifier!.Value.ToArray());
        Assert.NotEqual(rootSki.SubjectKeyIdentifierBytes.ToArray(), ski.SubjectKeyIdentifierBytes.ToArray());

        Assert.Equal("CN=mon-projet.test", leaf.Subject);
        Assert.Equal(root.Subject, leaf.Issuer);

        using var pub = leaf.GetECDsaPublicKey();
        Assert.NotNull(pub);
        Assert.Equal(256, pub.KeySize);

        var now = DateTime.UtcNow;
        Assert.InRange(leaf.NotBefore.ToUniversalTime(), now.AddDays(-1).AddMinutes(-5), now.AddDays(-1).AddMinutes(5));
        Assert.True(leaf.NotAfter - leaf.NotBefore <= TimeSpan.FromDays(825));
        Assert.True(leaf.NotAfter.ToUniversalTime() > now.AddDays(820));

        AssertRandomSerial(leaf);
    }

    [Fact]
    public void SiteCert_KeyFileMatchesCertificate()
    {
        var ca = NewCa();
        ca.EnsureRootCa().Dispose();
        var (crtPath, keyPath) = ca.IssueSiteCertificate("monsite.test");

        var keyPem = File.ReadAllText(keyPath);
        Assert.StartsWith("-----BEGIN PRIVATE KEY-----", keyPem, StringComparison.Ordinal);
        using var leaf = X509Certificate2.CreateFromPem(File.ReadAllText(crtPath), keyPem);
        Assert.True(leaf.HasPrivateKey);
    }

    [Theory]
    [InlineData("google.com")]
    [InlineData("")]
    [InlineData("test")]
    [InlineData(".test")]
    [InlineData("Monsite.test")]
    [InlineData("a.test.google.com")]
    [InlineData("sub.monsite.test")]
    [InlineData("..\\evil.test")]
    [InlineData("monsite.test\n")]
    [InlineData("localhost.")]
    [InlineData("127.0.0.1")]
    [InlineData("*.test")]
    public void Issue_ForeignDomain_Throws(string host)
    {
        var ca = NewCa();

        Assert.Throws<ArgumentException>(() => ca.IssueSiteCertificate(host));
        Assert.False(Directory.Exists(SitesDir) && Directory.EnumerateFileSystemEntries(SitesDir).Any());
    }

    [Fact]
    public void Issue_Null_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() => NewCa().IssueSiteCertificate(null!));
    }

    [Fact]
    public void KeyFile_NotReadableByEveryone()
    {
        var ca = NewCa();
        var (_, keyPath) = ca.IssueSiteCertificate("monsite.test");

        AssertKeyAclRestricted(keyPath);
    }

    [Fact]
    public void SiteCert_IsReusedWhenValid()
    {
        var ca = NewCa();
        var (crt1, _) = ca.IssueSiteCertificate("monsite.test");
        var thumb1 = Thumbprint(crt1);

        var (crt2, key2) = NewCa().IssueSiteCertificate("monsite.test");

        Assert.Equal(thumb1, Thumbprint(crt2));
        AssertKeyAclRestricted(key2);
    }

    [Fact]
    public void SiteCert_ExpiringSoon_IsReissued()
    {
        var ca = NewCa();
        ca.EnsureRootCa().Dispose();
        var (crtPath, keyPath) = ca.IssueSiteCertificate("monsite.test");

        // Remplace par un certificat valide signé par la CA mais expirant dans 10 jours.
        using (var material = ca.OpenCa())
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            using var shortLived = SignLeaf(material, key, "monsite.test", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(10));
            File.WriteAllText(crtPath, shortLived.ExportCertificatePem());
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        }

        var oldThumb = Thumbprint(crtPath);
        ca.IssueSiteCertificate("monsite.test");

        using var leaf = LoadPem(crtPath);
        Assert.NotEqual(oldThumb, leaf.Thumbprint);
        Assert.True(leaf.NotAfter.ToUniversalTime() > DateTime.UtcNow.AddDays(820));
        AssertKeyAclRestricted(keyPath);
    }

    [Fact]
    public void SiteCert_FromOtherCa_IsReissued()
    {
        var ca = NewCa();
        ca.EnsureRootCa().Dispose();
        var (crtPath, keyPath) = ca.IssueSiteCertificate("monsite.test");

        // Certificat auto-signé étranger déposé à la place.
        using (var key = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            var req = new CertificateRequest("CN=monsite.test", key, HashAlgorithmName.SHA256);
            using var self = req.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(300));
            File.WriteAllText(crtPath, self.ExportCertificatePem());
            File.WriteAllText(keyPath, key.ExportPkcs8PrivateKeyPem());
        }

        var foreignThumb = Thumbprint(crtPath);
        ca.IssueSiteCertificate("monsite.test");

        Assert.NotEqual(foreignThumb, Thumbprint(crtPath));
        using var root = ca.EnsureRootCa();
        using var leaf = LoadPem(crtPath);
        using var chain = NewChain(root);
        Assert.True(chain.Build(leaf));
    }

    [Fact]
    public void SiteCert_KeyMismatch_IsReissued()
    {
        var ca = NewCa();
        var (crtPath, keyPath) = ca.IssueSiteCertificate("monsite.test");
        using (var other = ECDsa.Create(ECCurve.NamedCurves.nistP256))
        {
            File.WriteAllText(keyPath, other.ExportPkcs8PrivateKeyPem());
        }

        var oldThumb = Thumbprint(crtPath);
        ca.IssueSiteCertificate("monsite.test");

        Assert.NotEqual(oldThumb, Thumbprint(crtPath));
        using var leaf = X509Certificate2.CreateFromPem(File.ReadAllText(crtPath), File.ReadAllText(keyPath));
        Assert.True(leaf.HasPrivateKey);
    }

    [Fact]
    public void SiteCert_AfterCaRegeneration_ChainsToNewRoot()
    {
        var ca = NewCa();
        ca.IssueSiteCertificate("monsite.test");
        _secrets.Delete("ca-key");

        var (crtPath, _) = ca.IssueSiteCertificate("monsite.test");

        using var root = ca.EnsureRootCa();
        using var leaf = LoadPem(crtPath);
        using var chain = NewChain(root);
        Assert.True(chain.Build(leaf), string.Join(", ", chain.ChainStatus.Select(s => s.Status)));
    }

    // ---------- Contraintes de nom effectivement appliquées par Windows ----------

    [Theory]
    [InlineData("google.com")]
    [InlineData("eviltest")]
    [InlineData("test.evil.com")]
    [InlineData("localhost.evil.com")]
    public void ForgedCert_OutsideNameConstraints_FailsChain(string dnsName)
    {
        var ca = NewCa();
        using var root = ca.EnsureRootCa();
        using var material = ca.OpenCa();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var forged = SignLeaf(material, key, dnsName, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        using var chain = NewChain(root);
        var ok = chain.Build(forged);

        Assert.False(ok);
        Assert.Contains(chain.ChainStatus, s => s.Status.HasFlag(X509ChainStatusFlags.HasNotPermittedNameConstraint));
    }

    [Fact]
    public void ForgedCert_ForeignIp_FailsChain()
    {
        var ca = NewCa();
        using var root = ca.EnsureRootCa();
        using var material = ca.OpenCa();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var forged = SignLeaf(material, key, "monsite.test", DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), System.Net.IPAddress.Parse("10.0.0.1"));

        using var chain = NewChain(root);

        Assert.False(chain.Build(forged));
        Assert.Contains(chain.ChainStatus, s => s.Status.HasFlag(X509ChainStatusFlags.HasNotPermittedNameConstraint));
    }

    [Theory]
    [InlineData("deep.sub.monsite.test")]
    [InlineData("localhost")]
    public void ForgedCert_InsideNameConstraints_Chains(string dnsName)
    {
        var ca = NewCa();
        using var root = ca.EnsureRootCa();
        using var material = ca.OpenCa();
        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var leaf = SignLeaf(material, key, dnsName, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30), System.Net.IPAddress.Loopback);

        using var chain = NewChain(root);

        Assert.True(chain.Build(leaf), string.Join(", ", chain.ChainStatus.Select(s => s.Status)));
    }

    // ---------- Aides ----------

    private static X509Chain NewChain(X509Certificate2 root)
    {
        var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ServerAuthOid));
        return chain;
    }

    private static X509Certificate2 SignLeaf(
        LocalCertificateAuthority.CaMaterial material,
        ECDsa key,
        string dnsName,
        DateTimeOffset notBefore,
        DateTimeOffset notAfter,
        System.Net.IPAddress? ip = null)
    {
        var req = new CertificateRequest("CN=" + dnsName, key, HashAlgorithmName.SHA256);
        req.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        req.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        req.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthOid)], false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(dnsName);
        if (ip is not null)
        {
            san.AddIpAddress(ip);
        }

        req.CertificateExtensions.Add(san.Build());
        req.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(material.Certificate, true, false));
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        return req.Create(material.Certificate.SubjectName, X509SignatureGenerator.CreateForECDsa(material.Key), notBefore, notAfter, serial);
    }

    private static X509Certificate2 LoadPem(string path) => X509Certificate2.CreateFromPem(File.ReadAllText(path));

    private static string Thumbprint(string path)
    {
        using var cert = LoadPem(path);
        return cert.Thumbprint;
    }

    private static void AssertRandomSerial(X509Certificate2 cert)
    {
        var serial = cert.SerialNumberBytes.ToArray(); // grand-boutiste
        Assert.Equal(16, serial.Length);
        Assert.True(serial[0] < 0x80, "Le numéro de série doit être positif.");
    }

    private static void AssertKeyAclRestricted(string keyPath)
    {
        var security = new FileInfo(keyPath).GetAccessControl();
        Assert.True(security.AreAccessRulesProtected, "L'héritage doit être coupé.");

        var me = WindowsIdentity.GetCurrent().User!;
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();

        Assert.NotEmpty(rules);
        var forbidden = new[]
        {
            new SecurityIdentifier(WellKnownSidType.WorldSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null),
            new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid, null),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null),
        };
        foreach (var rule in rules)
        {
            Assert.DoesNotContain((SecurityIdentifier)rule.IdentityReference, forbidden);
            Assert.False(rule.IsInherited);
            Assert.Equal(me, rule.IdentityReference);
            Assert.Equal(AccessControlType.Allow, rule.AccessControlType);
        }

        Assert.Equal(me, security.GetOwner(typeof(SecurityIdentifier)));
    }
}
