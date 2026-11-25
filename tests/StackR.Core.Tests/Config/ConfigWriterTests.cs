using System.Text;
using StackR.Core.Config;
using StackR.Core.Paths;
using StackR.Core.Settings;

namespace StackR.Core.Tests.Config;

public class ConfigWriterTests
{
    private static string[] Lines(string path) => File.ReadAllText(path).Split("\r\n");

    [Fact]
    public void Httpd_ListensOnLoopbackOnly()
    {
        using var tmp = new TempDirectory();
        var writer = new ConfigWriter(new StackRPaths(tmp.Path), new StackRSettings { HttpsEnabled = true });
        var path = writer.WriteHttpdConf(Path.Combine(tmp.Path, "apache"), Path.Combine(tmp.Path, "php"));

        var listens = Lines(path).Where(l => l.TrimStart().StartsWith("Listen ", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, listens.Count);
        Assert.All(listens, l => Assert.StartsWith("Listen 127.0.0.1:", l.Trim(), StringComparison.Ordinal));
        Assert.Contains("Listen 127.0.0.1:80", listens);
        Assert.Contains("Listen 127.0.0.1:443", listens);
    }

    [Fact]
    public void Httpd_HttpsDisabled_NoSslListenNorModule()
    {
        using var tmp = new TempDirectory();
        var writer = new ConfigWriter(new StackRPaths(tmp.Path), new StackRSettings { HttpsEnabled = false });
        var text = File.ReadAllText(writer.WriteHttpdConf(Path.Combine(tmp.Path, "apache"), Path.Combine(tmp.Path, "php")));

        Assert.DoesNotContain("LoadModule ssl_module", text);
        Assert.DoesNotContain(":443", text);
        Assert.Contains("Listen 127.0.0.1:80", text);
    }

    [Fact]
    public void Httpd_HardeningAndPaths()
    {
        using var tmp = new TempDirectory();
        var paths = new StackRPaths(tmp.Path);
        var writer = new ConfigWriter(paths, new StackRSettings());
        var phpDir = Path.Combine(tmp.Path, "php");
        var path = writer.WriteHttpdConf(Path.Combine(tmp.Path, "apache"), phpDir);
        var text = File.ReadAllText(path);

        Assert.Equal(Path.Combine(paths.Etc, "httpd.conf"), path);
        Assert.Contains("ServerTokens Prod", text);
        Assert.Contains("ServerSignature Off", text);
        Assert.Contains("TraceEnable Off", text);
        Assert.Contains("Options -Indexes", text);
        Assert.Contains("php8apache2_4.dll\"", text);
        Assert.Contains("PHPIniDir", text);
        Assert.Contains("/etc/sites/*.conf\"", text);
        Assert.Contains("LoadModule ssl_module", text);
        foreach (var m in new[] { "authz_core", "dir", "mime", "log_config", "rewrite", "headers", "socache_shmcb", "setenvif", "alias", "env" })
        {
            Assert.Contains($"LoadModule {m}_module", text);
        }

        Assert.DoesNotContain("{{", text);
        Assert.True(Directory.Exists(Path.Combine(paths.Etc, "sites")));
    }

    [Fact]
    public void Httpd_UsesCustomWwwPath_WhenSet()
    {
        using var tmp = new TempDirectory();
        var writer = new ConfigWriter(new StackRPaths(tmp.Path), new StackRSettings { WwwPath = @"D:\Mes Sites é" });
        var text = File.ReadAllText(writer.WriteHttpdConf("a", "p"));
        Assert.Contains("DocumentRoot \"D:/Mes Sites é\"", text);
    }

    [Fact]
    public void PhpIni_SecureDefaults()
    {
        using var tmp = new TempDirectory();
        var phpDir = Path.Combine(tmp.Path, "php");
        var writer = new ConfigWriter(new StackRPaths(tmp.Path), new StackRSettings { MailpitSmtpPort = 2525 });
        var path = writer.WritePhpIni(phpDir);
        var text = File.ReadAllText(path);

        Assert.Equal(Path.Combine(tmp.Path, "etc", "php.ini"), path);
        foreach (var expected in new[]
        {
            "expose_php = Off", "display_errors = On", "allow_url_include = Off",
            "session.cookie_httponly = 1", "SMTP = 127.0.0.1", "smtp_port = 2525",
            "memory_limit = 512M", "upload_max_filesize = 64M", "post_max_size = 64M",
        })
        {
            Assert.Contains(expected, text);
        }

        Assert.Contains("extension_dir = \"" + phpDir.Replace('\\', '/') + "/ext\"", text);
        foreach (var ext in new[] { "pdo_mysql", "mysqli", "mbstring", "openssl", "intl", "curl", "zip", "gd", "fileinfo", "sodium" })
        {
            Assert.Contains("extension=" + ext, text);
        }

    }

    [Fact]
    public void PhpIni_ExportsWindowsRootsAndReferencesThem()
    {
        using var tmp = new TempDirectory();
        var phpDir = Path.Combine(tmp.Path, "php");
        var paths = new StackRPaths(tmp.Path);
        var text = File.ReadAllText(new ConfigWriter(paths, new StackRSettings()).WritePhpIni(phpDir));

        var pem = Path.Combine(paths.Etc, "ssl", "cacert.pem");
        Assert.True(File.Exists(pem));
        Assert.Contains("-----BEGIN CERTIFICATE-----", File.ReadAllText(pem));
        Assert.Empty(Directory.GetFiles(Path.Combine(paths.Etc, "ssl"), "*.tmp"));
        var ca = pem.Replace('\\', '/');
        Assert.Contains($"curl.cainfo = \"{ca}\"", text);
        Assert.Contains($"openssl.cafile = \"{ca}\"", text);
    }

    [Fact]
    public void MyIni_BindsLoopback_AndDisablesX()
    {
        using var tmp = new TempDirectory();
        var paths = new StackRPaths(tmp.Path);
        var writer = new ConfigWriter(paths, new StackRSettings { MySqlPort = 3307 });
        var mysqlDir = Path.Combine(tmp.Path, "mysql");
        var path = writer.WriteMyIni(mysqlDir);
        var text = File.ReadAllText(path);

        Assert.Equal(Path.Combine(paths.Etc, "my.ini"), path);
        Assert.Contains("bind-address = 127.0.0.1", text);
        Assert.Contains("port = 3307", text);
        Assert.Contains("mysqlx = OFF", text);
        Assert.Contains("datadir = \"" + paths.MySqlData.Replace('\\', '/') + "\"", text);
        Assert.Contains("basedir = \"" + mysqlDir.Replace('\\', '/') + "\"", text);
        Assert.Contains("character-set-server = utf8mb4", text);
        Assert.Contains("collation-server = utf8mb4_unicode_ci", text);
        Assert.Contains("log-error", text);
    }

    [Fact]
    public void Writers_ProduceUtf8NoBomAndCrlf()
    {
        using var tmp = new TempDirectory();
        var writer = new ConfigWriter(new StackRPaths(Path.Combine(tmp.Path, "Dossier é")), new StackRSettings());
        foreach (var path in new[] { writer.WriteHttpdConf("a", "p"), writer.WritePhpIni("p"), writer.WriteMyIni("m") })
        {
            var bytes = File.ReadAllBytes(path);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            var text = new UTF8Encoding(false, true).GetString(bytes);
            Assert.Contains("\r\n", text);
            Assert.DoesNotContain("\n", text.Replace("\r\n", string.Empty, StringComparison.Ordinal));
        }
    }

    [Fact]
    public void Httpd_RootWithAccents_PathsForwardSlashQuoted()
    {
        using var tmp = new TempDirectory();
        var root = Path.Combine(tmp.Path, "Dossier é");
        var writer = new ConfigWriter(new StackRPaths(root), new StackRSettings());
        var text = File.ReadAllText(writer.WriteHttpdConf(Path.Combine(root, "bin", "apache"), Path.Combine(root, "bin", "php")));
        Assert.Contains("Dossier é/etc/sites/*.conf\"", text);
        Assert.Contains("ErrorLog \"", text);
    }

    [Fact]
    public void AllConfigs_RootWithSpaceAndAccent_PathDirectivesQuotedForwardSlash()
    {
        using var tmp = new TempDirectory();
        var root = Path.Combine(tmp.Path, "Dossier é", "StackR");
        var paths = new StackRPaths(root);
        var writer = new ConfigWriter(paths, new StackRSettings());
        var files = new[]
        {
            writer.WriteHttpdConf(Path.Combine(root, "bin", "apache"), Path.Combine(root, "bin", "php")),
            writer.WritePhpIni(Path.Combine(root, "bin", "php")),
            writer.WriteMyIni(Path.Combine(root, "bin", "mysql")),
        };
        var count = 0;
        foreach (var file in files)
        {
            foreach (var line in File.ReadAllLines(file))
            {
                if (!line.Contains("Dossier é", StringComparison.Ordinal) || line.TrimStart().StartsWith('#') || line.TrimStart().StartsWith(';'))
                {
                    continue;
                }

                count++;
                Assert.DoesNotContain("\\", line);
                Assert.Matches("\"[^\"]*Dossier é/StackR/[^\"]*\"", line);
            }
        }

        Assert.True(count >= 10, "directives de chemin trouvées : " + count);
    }

    [Theory]
    [InlineData(0, 443)]
    [InlineData(70000, 443)]
    [InlineData(80, 80)]
    public void Writers_InvalidOrDuplicatePorts_Throw(int http, int https)
    {
        using var tmp = new TempDirectory();
        var writer = new ConfigWriter(new StackRPaths(tmp.Path), new StackRSettings { HttpPort = http, HttpsPort = https });
        Assert.Throws<ArgumentException>(() => writer.WriteHttpdConf("a", "p"));
        Assert.Throws<ArgumentException>(() => writer.WriteMyIni("m"));
    }

    [Fact]
    public void Httpd_WwwPathWithQuote_Throws()
    {
        using var tmp = new TempDirectory();
        var writer = new ConfigWriter(new StackRPaths(tmp.Path), new StackRSettings { WwwPath = "D:\\x\"\nListen 0.0.0.0:80" });
        Assert.Throws<ArgumentException>(() => writer.WriteHttpdConf("a", "p"));
    }

    [Fact]
    public void EffectiveWww_UsesWwwPathOrDefault()
    {
        var paths = new StackRPaths(@"C:\StackR");
        Assert.Equal(@"C:\StackR\www", new StackRSettings().EffectiveWww(paths));
        Assert.Equal(@"D:\Sites", new StackRSettings { WwwPath = @"D:\Sites" }.EffectiveWww(paths));
    }
}
