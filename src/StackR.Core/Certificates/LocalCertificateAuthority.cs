using System.Formats.Asn1;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Security.Principal;
using System.Text;
using StackR.Core.Hosts;
using StackR.Core.Paths;
using StackR.Core.Security;

namespace StackR.Core.Certificates;

/// <summary>
/// Autorité de certification locale propre à la machine : CA racine ECDSA P-256 limitée par une extension
/// <c>NameConstraints</c> critique à <c>.test</c>, <c>localhost</c> et 127.0.0.0/8, et émission des certificats
/// des sites (<c>Ssl\sites\&lt;hôte&gt;.crt|.key</c>).
/// </summary>
/// <remarks>
/// Fichiers : certificat public de la CA dans <c>Ssl\ca.crt</c> (PEM) ; clé privée de la CA en PKCS#8,
/// uniquement chiffrée par DPAPI via <see cref="SecretStore"/> (secret <c>ca-key</c>), jamais écrite en clair.
/// La clé d'un site est en clair (Apache doit la lire) mais son ACL est réduite à l'utilisateur courant.
/// </remarks>
public sealed class LocalCertificateAuthority(StackRPaths paths, SecretStore secrets)
{
    internal const string CaKeySecretName = "ca-key";
    internal const string NameConstraintsOid = "2.5.29.30";
    private const string ServerAuthOid = "1.3.6.1.5.5.7.3.1";
    private const string LocalhostName = "localhost";
    private const int CaValidityYears = 10;
    private const int SiteValidityDays = 825;
    private const int RenewalMarginDays = 30;
    private const int SerialLength = 16;

    private readonly StackRPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly SecretStore _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));

    private string CaCertPath => Path.Combine(_paths.Ssl, "ca.crt");

    private string SitesDir => Path.Combine(_paths.Ssl, "sites");

    /// <summary>
    /// Renvoie le certificat public de la CA racine, en la générant si nécessaire.
    /// La CA existante est réutilisée si <c>ca.crt</c> et le secret <c>ca-key</c> existent et correspondent ;
    /// sinon les deux sont régénérés et les certificats de site existants (devenus orphelins) sont supprimés.
    /// </summary>
    /// <returns>Certificat sans clé privée ; à libérer par l'appelant.</returns>
    public X509Certificate2 EnsureRootCa()
    {
        using var ca = OpenCa();
        return X509CertificateLoader.LoadCertificate(ca.Certificate.RawData);
    }

    /// <summary>
    /// Émet (ou réutilise s'il est encore valide plus de 30 jours et signé par la CA actuelle) le certificat
    /// du site <paramref name="host"/> et renvoie les chemins des fichiers PEM.
    /// </summary>
    /// <exception cref="ArgumentException"><paramref name="host"/> n'est ni un nom <c>.test</c> valide ni <c>localhost</c>.</exception>
    public (string CertPath, string KeyPath) IssueSiteCertificate(string host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (!IsAllowedHost(host))
        {
            throw new ArgumentException(
                "Nom d'hôte refusé pour un certificat : seuls « <nom>.test » (a-z, 0-9, tirets) et « localhost » sont autorisés.",
                nameof(host));
        }

        using var ca = OpenCa();
        Directory.CreateDirectory(SitesDir);

        // Le nom est validé ci-dessus : il ne contient ni séparateur ni « .. ».
        var certPath = Path.Combine(SitesDir, host + ".crt");
        var keyPath = Path.Combine(SitesDir, host + ".key");

        if (IsReusableSiteCertificate(ca.Certificate, host, certPath, keyPath))
        {
            return (certPath, keyPath);
        }

        using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest(BuildCommonName(host), key, HashAlgorithmName.SHA256);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, critical: true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, critical: true));
        request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(ServerAuthOid)], critical: false));
        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName(host);
        request.CertificateExtensions.Add(san.Build(critical: false));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));
        request.CertificateExtensions.Add(
            X509AuthorityKeyIdentifierExtension.CreateFromCertificate(ca.Certificate, includeKeyIdentifier: true, includeIssuerAndSerial: false));

        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = notBefore.AddDays(SiteValidityDays);
        var caNotAfter = new DateTimeOffset(ca.Certificate.NotAfter.ToUniversalTime(), TimeSpan.Zero);
        if (notAfter > caNotAfter)
        {
            notAfter = caNotAfter;
        }

        using var certificate = request.Create(
            ca.Certificate.SubjectName,
            X509SignatureGenerator.CreateForECDsa(ca.Key),
            notBefore,
            notAfter,
            NewSerialNumber());

        // Clé d'abord : si l'écriture du certificat échoue, la paire incohérente sera réémise au prochain appel.
        WritePrivateKeyPem(keyPath, key);
        WriteFileAtomic(certPath, Encoding.ASCII.GetBytes(certificate.ExportCertificatePem()));
        return (certPath, keyPath);
    }

    /// <summary>Indique si le certificat actuel de la CA (<c>ca.crt</c>) est présent dans le magasin racine de l'utilisateur.</summary>
    public bool IsTrusted()
    {
        using var caCert = TryLoadCertificatePem(CaCertPath);
        if (caCert is null)
        {
            return false;
        }

        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
        var found = store.Certificates.Find(X509FindType.FindByThumbprint, caCert.Thumbprint, validOnly: false);
        try
        {
            return found.Any(c => c.RawDataMemory.Span.SequenceEqual(caCert.RawDataMemory.Span));
        }
        finally
        {
            foreach (var c in found)
            {
                c.Dispose();
            }
        }
    }

    /// <summary>
    /// Ajoute le certificat public de la CA au magasin racine de l'utilisateur courant.
    /// Windows affiche sa propre demande de confirmation ; un refus lève une <see cref="CryptographicException"/>.
    /// </summary>
    public void Trust()
    {
        using var root = EnsureRootCa();
        using var store = new X509Store(StoreName.Root, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadWrite);
        store.Add(root);
    }

    /// <summary>Charge la CA (certificat + clé privée), en la régénérant si elle est absente, invalide ou incohérente.</summary>
    internal CaMaterial OpenCa() => TryLoadCa() ?? CreateCa();

    internal static bool IsAllowedHost(string host) =>
        string.Equals(host, LocalhostName, StringComparison.Ordinal) || HostsBlock.IsValidHost(host);

    /// <summary>
    /// Extension NameConstraints (RFC 5280 §4.2.1.10), critique, encodée en DER :
    /// permittedSubtrees = { dNSName "test", dNSName "localhost", iPAddress 127.0.0.0/8 }.
    /// </summary>
    /// <remarks>
    /// Un dNSName sans point initial autorise le nom lui-même et tous ses sous-domaines (« x.test »),
    /// à frontière de label (« eviltest » est refusé). Le point initial est la forme URI/courriel, pas DNS.
    /// Les types de nom non listés (URI, courriel, DN…) ne servent pas à l'authentification serveur TLS.
    /// </remarks>
    internal static X509Extension BuildNameConstraintsExtension()
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence())
        {
            // permittedSubtrees [0] IMPLICIT GeneralSubtrees
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            {
                WriteDnsSubtree(writer, "test");
                WriteDnsSubtree(writer, LocalhostName);

                // GeneralSubtree { base iPAddress [7] IMPLICIT OCTET STRING (adresse || masque) }
                using (writer.PushSequence())
                {
                    writer.WriteOctetString([0x7F, 0x00, 0x00, 0x00, 0xFF, 0x00, 0x00, 0x00], new Asn1Tag(TagClass.ContextSpecific, 7));
                }
            }
        }

        return new X509Extension(NameConstraintsOid, writer.Encode(), critical: true);
    }

    private static void WriteDnsSubtree(AsnWriter writer, string dnsName)
    {
        // GeneralSubtree { base dNSName [2] IMPLICIT IA5String } ; minimum (0 par défaut) et maximum omis.
        using (writer.PushSequence())
        {
            writer.WriteCharacterString(UniversalTagNumber.IA5String, dnsName, new Asn1Tag(TagClass.ContextSpecific, 2));
        }
    }

    private CaMaterial? TryLoadCa()
    {
        if (!File.Exists(CaCertPath))
        {
            return null;
        }

        byte[]? pkcs8 = null;
        X509Certificate2? cert = null;
        ECDsa? key = null;
        try
        {
            pkcs8 = _secrets.GetBytes(CaKeySecretName);
            if (pkcs8 is null)
            {
                return null;
            }

            cert = TryLoadCertificatePem(CaCertPath);
            if (cert is null || !IsUsableCaCertificate(cert))
            {
                return null;
            }

            key = ECDsa.Create();
            key.ImportPkcs8PrivateKey(pkcs8, out var bytesRead);
            if (bytesRead != pkcs8.Length || !PublicKeysMatch(cert, key))
            {
                return null;
            }

            var material = new CaMaterial(cert, key);
            cert = null;
            key = null;
            return material;
        }
        catch (CryptographicException)
        {
            // Secret illisible (corrompu, autre utilisateur) ou clé invalide : la CA sera régénérée.
            return null;
        }
        finally
        {
            if (pkcs8 is not null)
            {
                CryptographicOperations.ZeroMemory(pkcs8);
            }

            cert?.Dispose();
            key?.Dispose();
        }
    }

    private static bool IsUsableCaCertificate(X509Certificate2 cert)
    {
        if (!cert.SubjectName.RawData.AsSpan().SequenceEqual(cert.IssuerName.RawData))
        {
            return false;
        }

        var now = DateTime.UtcNow;
        if (cert.NotBefore.ToUniversalTime() > now || cert.NotAfter.ToUniversalTime() <= now.AddDays(RenewalMarginDays))
        {
            return false;
        }

        var basic = cert.Extensions.OfType<X509BasicConstraintsExtension>().SingleOrDefault();
        if (basic is null || !basic.CertificateAuthority)
        {
            return false;
        }

        // La CA doit porter exactement nos contraintes de nom (une version antérieure ou altérée est régénérée).
        var expected = BuildNameConstraintsExtension();
        var actual = cert.Extensions[NameConstraintsOid];
        return actual is not null && actual.Critical && actual.RawData.AsSpan().SequenceEqual(expected.RawData);
    }

    private CaMaterial CreateCa()
    {
        // Les certificats de site signés par l'ancienne CA ne pourraient plus être validés.
        DeleteSiteCertificates();

        var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        try
        {
            var subject = BuildCommonName($"StackR Local CA ({Environment.MachineName})");
            var request = new CertificateRequest(subject, key, HashAlgorithmName.SHA256);
            request.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, true, 0, critical: true));
            request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, critical: true));
            request.CertificateExtensions.Add(BuildNameConstraintsExtension());
            request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, critical: false));

            var now = DateTimeOffset.UtcNow;
            var cert = request.Create(subject, X509SignatureGenerator.CreateForECDsa(key), now.AddDays(-1), now.AddYears(CaValidityYears), NewSerialNumber());
            try
            {
                // Secret d'abord : si l'écriture de ca.crt échoue, l'incohérence entraîne une régénération au prochain appel.
                var pkcs8 = key.ExportPkcs8PrivateKey();
                try
                {
                    _secrets.SetBytes(CaKeySecretName, pkcs8);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(pkcs8);
                }

                Directory.CreateDirectory(_paths.Ssl);
                WriteFileAtomic(CaCertPath, Encoding.ASCII.GetBytes(cert.ExportCertificatePem()));
                return new CaMaterial(cert, key);
            }
            catch
            {
                cert.Dispose();
                throw;
            }
        }
        catch
        {
            key.Dispose();
            throw;
        }
    }

    private void DeleteSiteCertificates()
    {
        if (!Directory.Exists(SitesDir))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(SitesDir))
        {
            var ext = Path.GetExtension(file);
            if (ext.Equals(".crt", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".key", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(file);
            }
        }
    }

    private static bool IsReusableSiteCertificate(X509Certificate2 caCert, string host, string certPath, string keyPath)
    {
        if (!File.Exists(certPath) || !File.Exists(keyPath))
        {
            return false;
        }

        try
        {
            using var cert = TryLoadCertificatePem(certPath);
            if (cert is null)
            {
                return false;
            }

            var now = DateTime.UtcNow;
            if (cert.NotBefore.ToUniversalTime() > now || cert.NotAfter.ToUniversalTime() <= now.AddDays(RenewalMarginDays))
            {
                return false;
            }

            var san = cert.Extensions.OfType<X509SubjectAlternativeNameExtension>().SingleOrDefault();
            if (san is null || !san.EnumerateDnsNames().SequenceEqual([host], StringComparer.Ordinal) || san.EnumerateIPAddresses().Any())
            {
                return false;
            }

            if (!ChainsTo(cert, caCert))
            {
                return false;
            }

            // Une clé dont l'ACL a été élargie a pu fuiter : on en génère une nouvelle.
            if (!IsKeyFileRestricted(keyPath))
            {
                return false;
            }

            using var key = ECDsa.Create();
            key.ImportFromPem(File.ReadAllText(keyPath));
            return PublicKeysMatch(cert, key);
        }
        catch (Exception ex) when (ex is CryptographicException or ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool ChainsTo(X509Certificate2 leaf, X509Certificate2 caCert)
    {
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(caCert);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        chain.ChainPolicy.DisableCertificateDownloads = true;
        chain.ChainPolicy.ApplicationPolicy.Add(new Oid(ServerAuthOid));
        try
        {
            return chain.Build(leaf)
                && chain.ChainElements.Count == 2
                && chain.ChainElements[1].Certificate.RawDataMemory.Span.SequenceEqual(caCert.RawDataMemory.Span);
        }
        finally
        {
            foreach (var element in chain.ChainElements)
            {
                element.Certificate.Dispose();
            }
        }
    }

    private static bool PublicKeysMatch(X509Certificate2 cert, ECDsa key) =>
        cert.PublicKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(key.ExportSubjectPublicKeyInfo());

    private static X500DistinguishedName BuildCommonName(string commonName)
    {
        var builder = new X500DistinguishedNameBuilder();
        builder.AddCommonName(commonName);
        return builder.Build();
    }

    // Numéro de série aléatoire de 16 octets, positif et d'encodage minimal (premier octet dans 0x40..0x7F).
    private static byte[] NewSerialNumber()
    {
        var serial = RandomNumberGenerator.GetBytes(SerialLength);
        serial[0] = (byte)((serial[0] & 0x3F) | 0x40);
        return serial;
    }

    private static X509Certificate2? TryLoadCertificatePem(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        var text = File.ReadAllText(path);
        if (!PemEncoding.TryFind(text, out var fields) || !text.AsSpan()[fields.Label].SequenceEqual("CERTIFICATE"))
        {
            return null;
        }

        try
        {
            var der = Convert.FromBase64String(text[fields.Base64Data]);
            return X509CertificateLoader.LoadCertificate(der);
        }
        catch (Exception ex) when (ex is CryptographicException or FormatException)
        {
            return null;
        }
    }

    private static void WritePrivateKeyPem(string path, ECDsa key)
    {
        var pkcs8 = key.ExportPkcs8PrivateKey();
        char[]? pem = null;
        byte[]? bytes = null;
        try
        {
            pem = PemEncoding.Write("PRIVATE KEY", pkcs8);
            bytes = new byte[Encoding.ASCII.GetByteCount(pem) + 1];
            Encoding.ASCII.GetBytes(pem, bytes);
            bytes[^1] = (byte)'\n';
            WriteFileAtomic(path, bytes, restricted: true);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs8);
            if (pem is not null)
            {
                Array.Clear(pem);
            }

            if (bytes is not null)
            {
                CryptographicOperations.ZeroMemory(bytes);
            }
        }
    }

    /// <summary>
    /// Écrit via un fichier temporaire puis renommage. En mode <paramref name="restricted"/>, le fichier temporaire
    /// est créé directement avec l'ACL restreinte (pas de fenêtre où il hériterait des droits du dossier) ;
    /// le renommage sur le même volume conserve ce descripteur de sécurité.
    /// </summary>
    private static void WriteFileAtomic(string path, byte[] content, bool restricted = false)
    {
        var tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            var security = restricted ? CreateOwnerOnlySecurity() : null;
            using (var stream = new FileInfo(tmp).Create(FileMode.CreateNew, FileSystemRights.FullControl, FileShare.None, 4096, FileOptions.None, security))
            {
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

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

    // Héritage coupé, propriétaire = utilisateur courant, seule entrée : utilisateur courant en contrôle total.
    private static FileSecurity CreateOwnerOnlySecurity()
    {
        var me = CurrentUserSid();
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.SetOwner(me);
        security.AddAccessRule(new FileSystemAccessRule(me, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }

    private static bool IsKeyFileRestricted(string keyPath)
    {
        var me = CurrentUserSid();
        var security = new FileInfo(keyPath).GetAccessControl();
        if (!security.AreAccessRulesProtected)
        {
            return false;
        }

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.IsInherited || !me.Equals(rule.IdentityReference))
            {
                return false;
            }
        }

        return rules.Count > 0;
    }

    private static SecurityIdentifier CurrentUserSid()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return identity.User ?? throw new InvalidOperationException("Impossible de déterminer l'utilisateur Windows courant.");
    }

    /// <summary>Certificat de la CA et sa clé privée (usage interne et tests).</summary>
    internal sealed class CaMaterial(X509Certificate2 certificate, ECDsa key) : IDisposable
    {
        public X509Certificate2 Certificate { get; } = certificate;

        public ECDsa Key { get; } = key;

        public void Dispose()
        {
            Certificate.Dispose();
            Key.Dispose();
        }
    }
}
