using System.Text;
using StackR.Core.Paths;
using StackR.Core.Settings;

namespace StackR.Core.Config;

// Génère les fichiers de configuration sous etc\ à partir des gabarits embarqués.
public sealed class ConfigWriter(StackRPaths paths, StackRSettings settings)
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    public string WriteHttpdConf(string apacheDir, string phpDir)
    {
        var www = string.IsNullOrWhiteSpace(settings.WwwPath) ? paths.Www : settings.WwwPath;
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
        var caBundle = Path.Combine(phpDir, "extras", "ssl", "cacert.pem");
        string caLines;
        if (File.Exists(caBundle))
        {
            var ca = TemplateRenderer.ConfPath(caBundle);
            caLines = $"curl.cainfo = \"{ca}\"\nopenssl.cafile = \"{ca}\"";
        }
        else
        {
            caLines = "; Aucun bundle CA PHP trouvé (curl.cainfo / openssl.cafile non définis)";
        }

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

    // UTF-8 sans BOM, fins de ligne CRLF.
    private string Write(string fileName, string templateName, Dictionary<string, string> values)
    {
        Directory.CreateDirectory(paths.Etc);
        var text = TemplateRenderer.Render(TemplateRenderer.LoadEmbedded(templateName), values);
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\r\n", StringComparison.Ordinal);
        var path = Path.Combine(paths.Etc, fileName);
        File.WriteAllText(path, text, Utf8NoBom);
        return path;
    }
}
