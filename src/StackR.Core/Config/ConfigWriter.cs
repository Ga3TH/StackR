using System.Security.Cryptography.X509Certificates;
using System.Text;
using StackR.Core.IO;
using StackR.Core.Paths;
using StackR.Core.Settings;

namespace StackR.Core.Config;

// Génère les fichiers de configuration sous etc\ à partir des gabarits embarqués.
/// <param name="paths">Arborescence StackR.</param>
/// <param name="settings">Paramètres.</param>
/// <param name="rootCertificates">Source des certificats racines exportés pour PHP (par défaut : magasins Windows Root).</param>
public sealed class ConfigWriter(
    StackRPaths paths, StackRSettings settings, Func<IEnumerable<X509Certificate2>>? rootCertificates = null)
{
    private readonly Func<IEnumerable<X509Certificate2>> _rootCertificates = rootCertificates ?? SystemRootCertificates;

    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public string WriteHttpdConf(string apacheDir, string phpDir)
    {
        var www = settings.EffectiveWww(paths);
        Directory.CreateDirectory(Path.Combine(paths.Etc, "sites"));

        var https = settings.HttpsEnabled;
        var values = new Dictionary<string, string>
        {
            ["ApacheDir"] = TemplateRenderer.ConfPath(apacheDir),
            ["PhpDir"] = TemplateRenderer.ConfPath(phpDir),
            ["EtcDir"] = TemplateRenderer.ConfPath(paths.Etc),
            ["LogsDir"] = TemplateRenderer.ConfPath(paths.Logs),
            ["WwwDir"] = TemplateRenderer.ConfPath(www),
            ["HttpPort"] = settings.HttpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["HttpsPort"] = settings.HttpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["HttpsListen"] = https
                ? "Listen 127.0.0.1:" + settings.HttpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : "# HTTPS désactivé",
            ["SslModules"] = https
                ? "LoadModule ssl_module modules/mod_ssl.so\nLoadModule socache_shmcb_module modules/mod_socache_shmcb.so"
                : "# mod_ssl non chargé (HTTPS désactivé)",
        };
        return Write("httpd.conf", "httpd.conf.tpl", values);
    }

    public string WritePhpIni(string phpDir)
    {
        var caBundle = Path.Combine(paths.Etc, "ssl", "cacert.pem");
        AtomicFile.WriteAllText(caBundle, ExportTrustedRootsPem(), new UTF8Encoding(false));
        var ca = TemplateRenderer.ConfPath(caBundle);
        var caLines = $"curl.cainfo = \"{ca}\"\nopenssl.cafile = \"{ca}\"";

        var values = new Dictionary<string, string>
        {
            ["PhpDir"] = TemplateRenderer.ConfPath(phpDir),
            ["MailpitSmtpPort"] = settings.MailpitSmtpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["CaBundleLines"] = caLines,
        };
        return Write("php.ini", "php.ini.tpl", values);
    }

    public string WriteMyIni(string mysqlDir)
    {
        var values = new Dictionary<string, string>
        {
            ["MySqlDir"] = TemplateRenderer.ConfPath(mysqlDir),
            ["MySqlData"] = TemplateRenderer.ConfPath(paths.MySqlData),
            ["MySqlPort"] = settings.MySqlPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["ErrorLog"] = TemplateRenderer.ConfPath(Path.Combine(paths.Logs, "mysql-error.log")),
        };
        return Write("my.ini", "my.ini.tpl", values);
    }

    /// <summary>Racines de confiance Windows (utilisateur puis machine) ; chaque certificat est libéré après lecture.</summary>
    internal static IEnumerable<X509Certificate2> SystemRootCertificates()
    {
        foreach (var location in new[] { StoreLocation.CurrentUser, StoreLocation.LocalMachine })
        {
            using var store = new X509Store(StoreName.Root, location);
            store.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
            foreach (var cert in store.Certificates)
            {
                try
                {
                    yield return cert;
                }
                finally
                {
                    cert.Dispose();
                }
            }
        }
    }

    // Racines de confiance, dédupliquées par empreinte, au format PEM.
    private string ExportTrustedRootsPem()
    {
        var sb = new StringBuilder();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var cert in _rootCertificates())
        {
            if (seen.Add(cert.Thumbprint))
            {
                sb.Append("# ").Append(cert.Subject.ReplaceLineEndings(" ")).Append('\n');
                sb.Append(cert.ExportCertificatePem()).Append('\n');
            }
        }

        return sb.ToString();
    }

    private void ValidatePorts()
    {
        var ports = new (string Name, int Value)[]
        {
            ("HttpPort", settings.HttpPort), ("HttpsPort", settings.HttpsPort), ("MySqlPort", settings.MySqlPort),
            ("MailpitUiPort", settings.MailpitUiPort), ("MailpitSmtpPort", settings.MailpitSmtpPort),
        };
        foreach (var (name, value) in ports)
        {
            if (value is < 1 or > 65535)
            {
                throw new ArgumentException($"{name} doit être compris entre 1 et 65535 (valeur : {value}).");
            }
        }

        var dup = ports.GroupBy(p => p.Value).FirstOrDefault(g => g.Count() > 1);
        if (dup is not null)
        {
            throw new ArgumentException($"Les ports doivent être distincts : {string.Join(", ", dup.Select(p => p.Name))} = {dup.Key}.");
        }
    }

    // UTF-8 sans BOM, fins de ligne CRLF.
    private string Write(string fileName, string templateName, Dictionary<string, string> values)
    {
        ValidatePorts();
        Directory.CreateDirectory(paths.Etc);
        var text = TemplateRenderer.Render(TemplateRenderer.LoadEmbedded(templateName), values);
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        var path = Path.Combine(paths.Etc, fileName);
        AtomicFile.WriteAllText(path, text, Utf8NoBom);
        return path;
    }
}
