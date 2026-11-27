using System.IO;
using System.Text;
using StackR.Core.Manifests;
using StackR.Core.Settings;

namespace StackR.Screens;

/// <summary>
/// Racine StackR factice dans un dossier temporaire : composants « installés » (marqueurs seulement),
/// sites variés (Laravel, Symfony, WordPress, PHP simple), journaux réalistes, stackr.json dédié.
/// Rien n'est lancé et rien ne touche l'installation réelle.
/// </summary>
internal sealed class FakeEnvironment
{
    private FakeEnvironment(string root, string settingsPath)
    {
        Root = root;
        SettingsPath = settingsPath;
    }

    public string Root { get; }

    public string SettingsPath { get; }

    public static FakeEnvironment Create(string baseDir)
    {
        var root = Path.Combine(baseDir, "StackR");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }

        Directory.CreateDirectory(root);
        var manifest = ManifestLoader.LoadEmbedded();
        var active = new Dictionary<string, string>();

        void Install(ComponentKind kind, string version, bool makeActive, params string[] files)
        {
            var dir = Path.Combine(root, "bin", kind.ToString().ToLowerInvariant(), version);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, ".stackr-installed"), "");
            foreach (var file in files)
            {
                var full = Path.Combine(dir, file);
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, "");
            }

            if (makeActive)
            {
                active[kind.ToString().ToLowerInvariant()] = version;
            }
        }

        string Recommended(ComponentKind kind) => manifest.Recommended(kind)?.Version ?? "1.0.0";

        Install(ComponentKind.Apache, Recommended(ComponentKind.Apache), true);
        var phps = manifest.For(ComponentKind.Php).Select(r => r.Version).ToList();
        Install(ComponentKind.Php, Recommended(ComponentKind.Php), true);
        foreach (var older in phps.Where(v => v != Recommended(ComponentKind.Php)).Take(1))
        {
            Install(ComponentKind.Php, older, false);
        }

        Install(ComponentKind.MySql, Recommended(ComponentKind.MySql), true, @"bin\mysql.exe");
        Install(ComponentKind.Mailpit, Recommended(ComponentKind.Mailpit), true);
        Install(ComponentKind.Composer, Recommended(ComponentKind.Composer), true);
        Install(ComponentKind.HeidiSql, Recommended(ComponentKind.HeidiSql), true, "heidisql.exe");
        Install(ComponentKind.Adminer, Recommended(ComponentKind.Adminer), true);

        var www = Path.Combine(root, "www");
        Site(www, "blog", ["artisan", "composer.json", @"public\index.php", @"app\Models\User.php", ".env"]);
        Site(www, "boutique", ["wp-config.php", "index.php", @"wp-content\themes\index.php"]);
        Site(www, "api-symfony", ["symfony.lock", "composer.json", @"public\index.php", @"bin\console"]);
        Site(www, "portfolio", ["index.php", @"assets\style.css"]);
        Site(www, "tp-sio2", ["index.php"]);

        WriteLogs(Path.Combine(root, "logs"));

        var settings = new StackRSettings
        {
            Root = root,
            ActiveVersions = active,
            FirstRunCompleted = true,
            AutoCheckUpdates = false,
            Theme = "Light",
        };
        var settingsPath = Path.Combine(baseDir, "stackr.json");
        SettingsStore.Save(settingsPath, settings);

        // Secret MySQL factice : la page Base de données le lit pour se dire « prête ».
        return new FakeEnvironment(root, settingsPath);
    }

    private static void Site(string www, string name, string[] files)
    {
        var dir = Path.Combine(www, name);
        foreach (var file in files)
        {
            var full = Path.Combine(dir, file);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "<?php // " + name);
        }
    }

    private static void WriteLogs(string logs)
    {
        Directory.CreateDirectory(logs);
        var error = new StringBuilder();
        var t = new DateTime(2026, 11, 27, 9, 12, 3);
        string Stamp(int add) => t.AddSeconds(add).ToString("ddd MMM dd HH:mm:ss.ffffff yyyy", System.Globalization.CultureInfo.InvariantCulture);
        error.AppendLine($"[{Stamp(0)}] [mpm_winnt:notice] [pid 14820:tid 344] AH00455: Apache/2.4.71 (Win64) OpenSSL/3.5.4 configured -- resuming normal operations");
        error.AppendLine($"[{Stamp(0)}] [mpm_winnt:notice] [pid 14820:tid 344] AH00456: Apache Lounge VS18 Server built: Nov 12 2026 10:12:40");
        error.AppendLine($"[{Stamp(1)}] [core:notice] [pid 14820:tid 344] AH00094: Command line: 'httpd.exe -d C:/StackR/bin/apache/2.4.71'");
        error.AppendLine($"[{Stamp(1)}] [mpm_winnt:notice] [pid 14820:tid 344] AH00418: Parent: Created child process 9120");
        error.AppendLine($"[{Stamp(2)}] [mpm_winnt:notice] [pid 9120:tid 352] AH00354: Child: Starting 64 worker threads.");
        for (var i = 0; i < 18; i++)
        {
            error.AppendLine($"[{Stamp(60 + (i * 37))}] [ssl:info] [pid 9120:tid {1200 + i}] [client 127.0.0.1:{52100 + i}] AH01964: Connection to child {i} established (server blog.test:443)");
        }

        error.AppendLine($"[{Stamp(1800)}] [php:warn] [pid 9120:tid 1204] [client 127.0.0.1:52144] PHP Warning:  Undefined variable $user in C:\\StackR\\www\\blog\\resources\\views\\home.blade.php on line 12");
        error.AppendLine($"[{Stamp(1805)}] [php:notice] [pid 9120:tid 1204] [client 127.0.0.1:52144] PHP Deprecated:  strftime() is deprecated in C:\\StackR\\www\\portfolio\\index.php on line 4");
        error.AppendLine($"[{Stamp(1900)}] [php:error] [pid 9120:tid 1208] [client 127.0.0.1:52190] PHP Fatal error:  Uncaught Error: Call to undefined function wp_get_theme() in C:\\StackR\\www\\boutique\\index.php:8");
        error.AppendLine($"[{Stamp(1900)}] [php:error] [pid 9120:tid 1208] [client 127.0.0.1:52190] PHP Stack trace:");
        error.AppendLine($"[{Stamp(1900)}] [php:error] [pid 9120:tid 1208] [client 127.0.0.1:52190] PHP   #0 {{main}}");
        for (var i = 0; i < 12; i++)
        {
            error.AppendLine($"[{Stamp(2000 + (i * 11))}] [ssl:info] [pid 9120:tid {1300 + i}] [client 127.0.0.1:{53100 + i}] AH01964: Connection to child {i} established (server api-symfony.test:443)");
        }

        error.AppendLine($"[{Stamp(2300)}] [core:warn] [pid 9120:tid 1310] AH00045: child process 4412 still did not exit, sending a SIGTERM");
        error.AppendLine($"[{Stamp(2400)}] [mpm_winnt:notice] [pid 14820:tid 344] AH00424: Parent: Received restart signal -- Restarting the server.");
        File.WriteAllText(Path.Combine(logs, "apache-error.log"), error.ToString());

        var access = new StringBuilder();
        for (var i = 0; i < 40; i++)
        {
            access.AppendLine($"127.0.0.1 - - [27/Nov/2026:10:{i:00}:12 +0100] \"GET /{(i % 3 == 0 ? "" : "api/users/" + i)} HTTP/1.1\" {(i % 9 == 0 ? 404 : 200)} {1200 + (i * 31)}");
        }

        File.WriteAllText(Path.Combine(logs, "apache-access.log"), access.ToString());
        File.WriteAllText(Path.Combine(logs, "mysql-error.log"),
            "2026-11-27T08:12:04.120004Z 0 [System] [MY-010116] [Server] C:\\StackR\\bin\\mysql\\8.4.7\\bin\\mysqld.exe (mysqld 8.4.7) starting as process 7788\n" +
            "2026-11-27T08:12:04.161284Z 1 [System] [MY-013576] [InnoDB] InnoDB initialization has started.\n" +
            "2026-11-27T08:12:04.902137Z 1 [System] [MY-013577] [InnoDB] InnoDB initialization has ended.\n" +
            "2026-11-27T08:12:05.301119Z 0 [Warning] [MY-010068] [Server] CA certificate ca.pem is self signed.\n" +
            "2026-11-27T08:12:05.441090Z 0 [System] [MY-011323] [Server] X Plugin ready for connections. Bind-address: '127.0.0.1' port: 33060\n" +
            "2026-11-27T08:12:05.441200Z 0 [System] [MY-010931] [Server] C:\\StackR\\bin\\mysql\\8.4.7\\bin\\mysqld.exe: ready for connections. Version: '8.4.7'  socket: ''  port: 3306  MySQL Community Server - GPL.\n" +
            "2026-11-27T09:40:11.002211Z 12 [ERROR] [MY-010584] [Repl] Access denied for user 'laravel'@'localhost' (using password: YES)\n");
        File.WriteAllText(Path.Combine(logs, "mailpit.log"),
            "time=\"2026/11/27 09:12:06\" level=info msg=\"[smtpd] starting on 127.0.0.1:1025 (no encryption)\"\n" +
            "time=\"2026/11/27 09:12:06\" level=info msg=\"[http] starting on 127.0.0.1:8025\"\n" +
            "time=\"2026/11/27 10:02:44\" level=info msg=\"[smtpd] message received from noreply@blog.test via 127.0.0.1:61022 (1 KB)\"\n");
    }
}
