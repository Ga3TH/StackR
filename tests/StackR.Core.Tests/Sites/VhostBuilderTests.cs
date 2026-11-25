using StackR.Core.Settings;
using StackR.Core.Sites;

namespace StackR.Core.Tests.Sites;

public class VhostBuilderTests
{
    private static Site MakeSite()
    {
        SiteName.TryFromFolder("Mon App", out var name);
        return new Site(name, @"C:\Dossier é\www\Mon App", @"C:\Dossier é\www\Mon App\public");
    }

    [Fact]
    public void Vhost_WithCert_HasSslBlock()
    {
        var conf = VhostBuilder.Build(MakeSite(), new StackRSettings(), @"C:\s\mon-app.test.pem", @"C:\s\mon-app.test-key.pem");

        Assert.Contains("<VirtualHost 127.0.0.1:80>", conf);
        Assert.Contains("<VirtualHost 127.0.0.1:443>", conf);
        Assert.Contains("SSLEngine on", conf);
        Assert.Contains("\"C:/s/mon-app.test.pem\"", conf);
        Assert.Contains("ServerName mon-app.test", conf);
        Assert.Contains("\"C:/Dossier é/www/Mon App/public\"", conf);
    }

    [Fact]
    public void Vhost_WithoutCert_HasNoSslBlock()
    {
        var conf = VhostBuilder.Build(MakeSite(), new StackRSettings(), null, null);
        Assert.DoesNotContain("SSLEngine", conf);
    }

    [Fact]
    public void Vhost_HttpsDisabled_HasNoSslBlock()
    {
        var conf = VhostBuilder.Build(MakeSite(), new StackRSettings { HttpsEnabled = false }, "a.pem", "b.pem");
        Assert.DoesNotContain("SSLEngine", conf);
    }

    [Fact]
    public void Vhost_ListensLoopback()
    {
        var conf = VhostBuilder.Build(MakeSite(), new StackRSettings { HttpPort = 8080, HttpsPort = 8443 }, "a.pem", "b.pem");

        Assert.Contains("<VirtualHost 127.0.0.1:8080>", conf);
        Assert.Contains("<VirtualHost 127.0.0.1:8443>", conf);
        Assert.Contains("AllowOverride All", conf);
        Assert.Contains("Require all granted", conf);
        Assert.DoesNotContain("0.0.0.0", conf);
        Assert.DoesNotContain("*:", conf);
    }
}
