using StackR.Core.Config;
using StackR.Core.Settings;

namespace StackR.Core.Sites;

// Génère le fichier vhost Apache d'un site (HTTP, plus HTTPS si certificat fourni).
public static class VhostBuilder
{
    public static string Build(Site site, StackRSettings s, string? certPath, string? keyPath)
    {
        ArgumentNullException.ThrowIfNull(site);
        ArgumentNullException.ThrowIfNull(s);

        var values = new Dictionary<string, string>
        {
            ["ServerName"] = site.Name.Host,
            ["DocumentRoot"] = TemplateRenderer.ConfPath(site.DocumentRoot),
            ["HttpPort"] = s.HttpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["HttpsPort"] = s.HttpsPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["CertFile"] = certPath is null ? "" : TemplateRenderer.ConfPath(certPath),
            ["KeyFile"] = keyPath is null ? "" : TemplateRenderer.ConfPath(keyPath),
        };

        var conf = TemplateRenderer.Render(TemplateRenderer.LoadEmbedded("vhost.conf.tpl"), values);
        if (certPath is not null && keyPath is not null && s.HttpsEnabled)
        {
            conf = conf.TrimEnd() + Environment.NewLine + Environment.NewLine
                + TemplateRenderer.Render(TemplateRenderer.LoadEmbedded("vhost-ssl.conf.tpl"), values);
        }

        return conf;
    }
}
