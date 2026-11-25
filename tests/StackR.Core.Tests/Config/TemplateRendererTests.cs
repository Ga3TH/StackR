using StackR.Core.Config;

namespace StackR.Core.Tests.Config;

public class TemplateRendererTests
{
    [Fact]
    public void Render_MissingKey_Throws()
    {
        var values = new Dictionary<string, string> { ["A"] = "1" };
        Assert.Throws<KeyNotFoundException>(() => TemplateRenderer.Render("{{A}} {{B}}", values));
    }

    [Fact]
    public void Render_ReplacesAllOccurrences()
    {
        var values = new Dictionary<string, string> { ["A"] = "1" };
        Assert.Equal("1-1", TemplateRenderer.Render("{{A}}-{{A}}", values));
    }

    [Fact]
    public void Render_PathWithSpacesAndAccents_IsQuotedForwardSlash()
    {
        var values = new Dictionary<string, string>
        {
            ["Dir"] = TemplateRenderer.ConfPath(@"C:\Dossier é\StackR\etc"),
        };
        var result = TemplateRenderer.Render("Include \"{{Dir}}/sites/*.conf\"", values);
        Assert.Equal("Include \"C:/Dossier é/StackR/etc/sites/*.conf\"", result);
        Assert.DoesNotContain('\\', result);
    }

    [Theory]
    [InlineData("httpd.conf.tpl")]
    [InlineData("php.ini.tpl")]
    [InlineData("my.ini.tpl")]
    [InlineData("vhost.conf.tpl")]
    [InlineData("vhost-ssl.conf.tpl")]
    public void LoadEmbedded_Template_Exists(string name)
    {
        Assert.False(string.IsNullOrWhiteSpace(TemplateRenderer.LoadEmbedded(name)));
    }

    [Fact]
    public void Vhost_Templates_RenderWithAllKeys()
    {
        var values = new Dictionary<string, string>
        {
            ["ServerName"] = "app.test",
            ["DocumentRoot"] = "C:/StackR/www/app",
            ["HttpPort"] = "80",
            ["HttpsPort"] = "443",
            ["CertFile"] = "C:/StackR/data/ssl/app.test.pem",
            ["KeyFile"] = "C:/StackR/data/ssl/app.test-key.pem",
        };
        foreach (var name in new[] { "vhost.conf.tpl", "vhost-ssl.conf.tpl" })
        {
            var rendered = TemplateRenderer.Render(TemplateRenderer.LoadEmbedded(name), values);
            Assert.DoesNotContain("{{", rendered);
            Assert.Contains("app.test", rendered);
        }
    }
}
