using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using StackR.Core.Config;
using StackR.Core.Manifests;
using StackR.Core.Services;
using StackR.Core.Settings;

namespace StackR.Core.Tests;

public sealed class StackREngineTests : IDisposable
{
    private readonly EngineTestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private string SitesDir => Path.Combine(_env.Paths.Etc, "sites");

    [Fact]
    public void Create_UsesRootFromSettings_AndCreatesTree()
    {
        using var engine = _env.CreateEngine();

        Assert.Equal(_env.Paths.Root, engine.Paths.Root);
        Assert.True(Directory.Exists(_env.Paths.Shims));
        Assert.True(Directory.Exists(_env.Paths.Ssl));
        Assert.NotEmpty(engine.Manifest.Releases);
        Assert.Equal(["mysql", "apache", "mailpit"], engine.Services.Services.Select(s => s.Id).ToArray());
    }

    [Fact]
    public async Task Engine_UsePhp_UpdatesSettingsAndConfig()
    {
        _env.InstallFake("apache", "2.4.71");
        _env.InstallFake("php", "8.3.37");
        var newPhp = _env.InstallFake("php", "8.4.28");
        _env.Activate("apache", "2.4.71");
        _env.Activate("php", "8.3.37");
        using var engine = _env.CreateEngine();

        await engine.UsePhpAsync("8.4.28");

        Assert.Equal("8.4.28", engine.Settings.ActiveVersions["php"]);
        Assert.Equal("8.4.28", SettingsStore.Load(_env.SettingsPath).ActiveVersions["php"]);

        var httpd = File.ReadAllText(Path.Combine(_env.Paths.Etc, "httpd.conf"));
        Assert.Contains($"LoadModule php_module \"{TemplateRenderer.ConfPath(newPhp)}/php8apache2_4.dll\"", httpd);
        Assert.DoesNotContain("8.3.37", httpd);

        var phpIni = File.ReadAllText(Path.Combine(_env.Paths.Etc, "php.ini"));
        Assert.Contains($"extension_dir = \"{TemplateRenderer.ConfPath(newPhp)}/ext\"", phpIni);

        Assert.Contains(@"%D%..\php\8.4.28\php.exe", File.ReadAllText(Path.Combine(_env.Paths.Shims, "php.cmd")));
    }

    [Fact]
    public async Task UseAsync_NotInstalled_Throws_AndKeepsSettings()
    {
        _env.InstallFake("php", "8.3.37");
        _env.Activate("php", "8.3.37");
        using var engine = _env.CreateEngine();

        await Assert.ThrowsAsync<InvalidOperationException>(() => engine.UseAsync(ComponentKind.Php, "8.4.28"));

        Assert.Equal("8.3.37", SettingsStore.Load(_env.SettingsPath).ActiveVersions["php"]);
    }

    [Fact]
    public async Task ApplyConfiguration_WritesVhostsForTwoSites_AndDeletesStale()
    {
        _env.AddSite("alpha");
        _env.AddSite("Beta Site");
        Directory.CreateDirectory(SitesDir);
        File.WriteAllText(Path.Combine(SitesDir, "ancien.conf"), "# site supprimé");
        File.WriteAllText(Path.Combine(SitesDir, "notes.txt"), "à garder");
        using var engine = _env.CreateEngine();

        await engine.ApplyConfigurationAsync();

        Assert.Equal(["alpha.conf", "beta-site.conf", "notes.txt"], Directory.GetFiles(SitesDir).Select(f => Path.GetFileName(f)!).Order().ToArray());
        var alpha = File.ReadAllText(Path.Combine(SitesDir, "alpha.conf"));
        Assert.Contains("ServerName alpha.test", alpha);
        Assert.Contains("SSLEngine on", alpha);
        var cert = Path.Combine(_env.Paths.Ssl, "sites", "alpha.test.crt");
        Assert.Contains($"SSLCertificateFile \"{TemplateRenderer.ConfPath(cert)}\"", alpha);
        Assert.True(File.Exists(cert));
        Assert.Contains("ServerName beta-site.test", File.ReadAllText(Path.Combine(SitesDir, "beta-site.conf")));
    }

    [Fact]
    public async Task ApplyConfiguration_HttpsDisabled_WritesHttpOnlyVhost_WithoutCa()
    {
        _env.Settings = _env.Settings with { HttpsEnabled = false };
        _env.AddSite("alpha");
        using var engine = _env.CreateEngine();

        await engine.ApplyConfigurationAsync();

        Assert.DoesNotContain("SSLEngine", File.ReadAllText(Path.Combine(SitesDir, "alpha.conf")));
        Assert.False(File.Exists(Path.Combine(_env.Paths.Ssl, "ca.crt")));
    }

    [Fact]
    public async Task ApplyConfiguration_HostsDiffer_ElevatesWithComputedNames()
    {
        _env.AddSite("alpha");
        _env.AddSite("Beta Site");
        using var engine = _env.CreateEngine();

        var result = await engine.ApplyConfigurationAsync();

        var call = Assert.Single(_env.ElevatedCalls);
        Assert.Equal(["hosts", "sync", "--names", "alpha.test,beta-site.test"], call);
        Assert.True(result.HostsUpdated);
        Assert.False(result.HostsDeclined);
    }

    [Fact]
    public async Task ApplyConfiguration_NoSitesLeft_ElevatesWithClear()
    {
        File.WriteAllText(_env.HostsPath, "127.0.0.1 localhost\r\n# >>> StackR\r\n127.0.0.1 ancien.test\r\n# <<< StackR\r\n");
        using var engine = _env.CreateEngine();

        await engine.ApplyConfigurationAsync();

        Assert.Equal(["hosts", "sync", "--clear"], Assert.Single(_env.ElevatedCalls));
    }

    [Fact]
    public async Task ApplyConfiguration_UacDeclined_ReturnsDeclined_WithoutThrowing()
    {
        _env.AddSite("alpha");
        _env.ElevatedResult = -1;
        using var engine = _env.CreateEngine();

        var result = await engine.ApplyConfigurationAsync();

        Assert.True(result.HostsDeclined);
        Assert.False(result.HostsUpdated);
        Assert.Contains(result.Warnings, w => w.Contains("hosts", StringComparison.Ordinal));
        Assert.True(File.Exists(Path.Combine(SitesDir, "alpha.conf")));
    }

    [Fact]
    public async Task ApplyConfiguration_HostsUpToDate_DoesNotElevate()
    {
        _env.AddSite("alpha");
        File.WriteAllText(_env.HostsPath, "127.0.0.1 localhost\r\n# >>> StackR\r\n127.0.0.1 alpha.test\r\n# <<< StackR\r\n");
        using var engine = _env.CreateEngine();

        var result = await engine.ApplyConfigurationAsync();

        Assert.Empty(_env.ElevatedCalls);
        Assert.False(result.HostsUpdated);
        Assert.False(result.HostsDeclined);
    }

    [Fact]
    public async Task ApplyConfiguration_AlreadyElevated_WritesHostsDirectly()
    {
        _env.AddSite("alpha");
        _env.Elevated = true;
        using var engine = _env.CreateEngine();

        var result = await engine.ApplyConfigurationAsync();

        Assert.Empty(_env.ElevatedCalls);
        Assert.True(result.HostsUpdated);
        Assert.Contains("127.0.0.1 alpha.test", File.ReadAllText(_env.HostsPath));
    }

    [Fact]
    public async Task ApplyConfiguration_FirstRun_AddsShimsAndInstallDirToUserPath()
    {
        using var engine = _env.CreateEngine();

        await engine.ApplyConfigurationAsync();

        Assert.Equal([_env.Paths.Shims, _env.InstallDir], _env.ReadUserPath()!.Split(';'));
    }

    [Fact]
    public async Task ApplyConfiguration_AfterFirstRun_LeavesUserPathAlone()
    {
        _env.Settings = _env.Settings with { FirstRunCompleted = true };
        using var engine = _env.CreateEngine();

        await engine.ApplyConfigurationAsync();

        Assert.Null(_env.ReadUserPath());
    }

    [Fact]
    public async Task InstallAsync_FirstVersion_IsActivated_SecondIsNot()
    {
        var payload = "<?php // composer"u8.ToArray();
        var sha = Convert.ToHexStringLower(SHA256.HashData(payload));
        ComponentRelease Release(string v) => new(
            ComponentKind.Composer, v, "stable", new Uri($"https://example.org/{v}/composer.phar"), sha, payload.Length,
            PackageType.File, [], null);
        _env.Handler = new EngineTestEnvironment.BytesHandler(payload);
        _env.Manifest = new Manifest([Release("2.8.1"), Release("2.9.0")]);
        using var engine = _env.CreateEngine();

        await engine.InstallAsync(Release("2.8.1"), null, CancellationToken.None);
        Assert.Equal("2.8.1", SettingsStore.Load(_env.SettingsPath).ActiveVersions["composer"]);

        await engine.InstallAsync(Release("2.9.0"), null, CancellationToken.None);
        Assert.Equal("2.8.1", engine.Settings.ActiveVersions["composer"]);
        Assert.True(engine.Store.IsInstalled(ComponentKind.Composer, "2.9.0"));
        Assert.True(File.Exists(Path.Combine(_env.Paths.ComponentDir("composer", "2.9.0"), "composer.phar")));
    }

    // ---- Paramètres (UpdateSettings) ----

    [Fact]
    public void UpdateSettings_Theme_SavesWithoutRestart_AndKeepsActiveVersionsInstance()
    {
        _env.Activate("php", "8.3.37");
        using var engine = _env.CreateEngine();
        var versions = engine.Settings.ActiveVersions;
        var raised = 0;
        engine.SettingsChanged += (_, _) => raised++;

        var restart = engine.UpdateSettings(s => s with { Theme = "Dark", ActiveVersions = new Dictionary<string, string>() });

        Assert.False(restart);
        Assert.False(engine.RestartRequired);
        Assert.Equal(1, raised);
        Assert.Equal("Dark", engine.Settings.Theme);
        Assert.Same(versions, engine.Settings.ActiveVersions);
        Assert.Same(versions, engine.EffectiveSettings.ActiveVersions);
        var saved = SettingsStore.Load(_env.SettingsPath);
        Assert.Equal("Dark", saved.Theme);
        Assert.Equal("8.3.37", saved.ActiveVersions["php"]);
    }

    [Theory]
    [InlineData("root")]
    [InlineData("www")]
    [InlineData("http")]
    [InlineData("https")]
    [InlineData("mysql")]
    [InlineData("mailpit-ui")]
    [InlineData("mailpit-smtp")]
    public void UpdateSettings_RootWwwOrPort_RequiresRestart_AndKeepsStartupValueInEffect(string field)
    {
        using var engine = _env.CreateEngine();
        var startup = engine.Settings;
        var other = Path.Combine(_env.Paths.Root, "ailleurs");

        var restart = engine.UpdateSettings(s => field switch
        {
            "root" => s with { Root = other },
            "www" => s with { WwwPath = other },
            "http" => s with { HttpPort = s.HttpPort + 1 },
            "https" => s with { HttpsPort = s.HttpsPort + 1 },
            "mysql" => s with { MySqlPort = s.MySqlPort + 1 },
            "mailpit-ui" => s with { MailpitUiPort = s.MailpitUiPort + 1 },
            _ => s with { MailpitSmtpPort = s.MailpitSmtpPort + 1 },
        });

        Assert.True(restart);
        Assert.True(engine.RestartRequired);
        Assert.NotEqual(startup, SettingsStore.Load(_env.SettingsPath) with { ActiveVersions = startup.ActiveVersions });
        var effective = engine.EffectiveSettings;
        Assert.Equal(startup.Root, effective.Root);
        Assert.Equal(startup.WwwPath, effective.WwwPath);
        Assert.Equal(startup.HttpPort, effective.HttpPort);
        Assert.Equal(startup.HttpsPort, effective.HttpsPort);
        Assert.Equal(startup.MySqlPort, effective.MySqlPort);
        Assert.Equal(startup.MailpitUiPort, effective.MailpitUiPort);
        Assert.Equal(startup.MailpitSmtpPort, effective.MailpitSmtpPort);
        Assert.Equal(startup.MySqlPort, Service(engine, "mysql").Ports[0]);
    }

    [Fact]
    public void UpdateSettings_BackToStartupValue_ClearsRestartRequired()
    {
        using var engine = _env.CreateEngine();
        var port = engine.Settings.HttpPort;

        Assert.True(engine.UpdateSettings(s => s with { HttpPort = port + 1 }));
        Assert.False(engine.UpdateSettings(s => s with { HttpPort = port }));

        Assert.False(engine.RestartRequired);
    }

    [Fact]
    public async Task UpdateSettings_HttpsOff_AppliesToConfigAndApacheWithoutRestart()
    {
        _env.AddSite("alpha");
        using var engine = _env.CreateEngine();
        var apache = Service(engine, "apache");
        Assert.Equal(2, apache.Ports.Length);

        var restart = engine.UpdateSettings(s => s with { HttpsEnabled = false });
        await engine.ApplyConfigurationAsync();

        Assert.False(restart);
        Assert.Equal([_env.Settings.HttpPort], apache.Ports);
        Assert.DoesNotContain("SSLEngine", File.ReadAllText(Path.Combine(SitesDir, "alpha.conf")));
    }

    [Fact]
    public async Task UpdateSettings_PortChange_ConfigKeepsStartupPortUntilRestart()
    {
        _env.Settings = _env.Settings with { HttpsEnabled = false };
        _env.AddSite("alpha");
        using var engine = _env.CreateEngine();
        var port = engine.Settings.HttpPort;

        engine.UpdateSettings(s => s with { HttpPort = port + 1 });
        await engine.ApplyConfigurationAsync();

        var vhost = File.ReadAllText(Path.Combine(SitesDir, "alpha.conf"));
        Assert.Contains(":" + port.ToString(CultureInfo.InvariantCulture), vhost, StringComparison.Ordinal);
        Assert.DoesNotContain(":" + (port + 1).ToString(CultureInfo.InvariantCulture), vhost, StringComparison.Ordinal);
    }

    // ---- Porte des opérations ----

    [Fact]
    public async Task Operations_InParallel_AreSerialized_WithoutDeadlock()
    {
        _env.InstallFake("php", "8.3.37");
        _env.InstallFake("php", "8.4.28");
        _env.Activate("php", "8.3.37");
        _env.AddSite("alpha");
        var current = 0;
        var max = 0;
        _env.ElevatedHandler = async _ =>
        {
            var now = Interlocked.Increment(ref current);
            InterlockedMax(ref max, now);
            await Task.Delay(20);
            Interlocked.Decrement(ref current);
            return 0;
        };
        using var engine = _env.CreateEngine();

        var operations = new List<Task>();
        for (var i = 0; i < 4; i++)
        {
            operations.Add(engine.UseAsync(ComponentKind.Php, i % 2 == 0 ? "8.4.28" : "8.3.37"));
            operations.Add(engine.ApplyConfigurationAsync());
            operations.Add(engine.StartAllAsync());
            operations.Add(engine.StopAllAsync());
            operations.Add(Task.Run(() => engine.UpdateSettings(s => s with { Theme = "Dark" })));
        }

        await Task.WhenAll(operations).WaitAsync(TimeSpan.FromSeconds(60));

        Assert.Equal(1, max);
        // 4 activations + 4 applications : une relance élevée chacune (le faux hosts n'est jamais écrit) ;
        // StartAllAsync n'applique rien, aucun service n'étant installé.
        Assert.Equal(8, _env.ElevatedCalls.Count);
        Assert.Equal("8.3.37", engine.ActiveVersion(ComponentKind.Php));
        Assert.Equal("8.3.37", SettingsStore.Load(_env.SettingsPath).ActiveVersions["php"]);
        Assert.Contains(@"%D%..\php\8.3.37\php.exe", File.ReadAllText(Path.Combine(_env.Paths.Shims, "php.cmd")));
    }

    [Fact]
    public async Task UseAsync_ConcurrentWithApplyConfiguration_NoExceptionNoDeadlock()
    {
        _env.InstallFake("apache", "2.4.71");
        _env.InstallFake("php", "8.3.37");
        _env.InstallFake("php", "8.4.28");
        _env.Activate("apache", "2.4.71");
        _env.Activate("php", "8.3.37");
        using var engine = _env.CreateEngine();

        var use = engine.UseAsync(ComponentKind.Php, "8.4.28");
        var apply = engine.ApplyConfigurationAsync();
        await Task.WhenAll(use, apply).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.Contains("8.4.28", File.ReadAllText(Path.Combine(_env.Paths.Etc, "httpd.conf")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task InstallAsync_WhileDownloading_DoesNotBlockOtherOperations()
    {
        var payload = "<?php // composer"u8.ToArray();
        var release = ComposerRelease(payload);
        var handler = new BlockingHandler(payload);
        _env.Handler = handler;
        _env.Manifest = new Manifest([release]);
        using var engine = _env.CreateEngine();

        var install = engine.InstallAsync(release, null, CancellationToken.None);
        await handler.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Le téléchargement est bloqué : les autres opérations passent quand même.
        await engine.ApplyConfigurationAsync().WaitAsync(TimeSpan.FromSeconds(10));
        await engine.StopAllAsync().WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(install.IsCompleted);

        handler.Release.SetResult();
        await install.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal("2.8.1", engine.ActiveVersion(ComponentKind.Composer));
    }

    [Fact]
    public async Task InstallAsync_SameReleaseTwiceInParallel_BothSucceed()
    {
        var payload = "<?php // composer"u8.ToArray();
        var release = ComposerRelease(payload);
        _env.Handler = new EngineTestEnvironment.BytesHandler(payload);
        _env.Manifest = new Manifest([release]);
        using var engine = _env.CreateEngine();

        await Task.WhenAll(
            engine.InstallAsync(release, null, CancellationToken.None),
            engine.InstallAsync(release, null, CancellationToken.None)).WaitAsync(TimeSpan.FromSeconds(30));

        Assert.True(engine.Store.IsInstalled(ComponentKind.Composer, "2.8.1"));
        Assert.Equal("2.8.1", engine.ActiveVersion(ComponentKind.Composer));
        Assert.Empty(Directory.GetFileSystemEntries(_env.Paths.Tmp, "install-*"));
    }

    // ---- Démarrage et arrêt ----

    [Fact]
    public async Task StartAllAsync_OnlySomeComponentsInstalled_StartsThoseOnly()
    {
        InstallFakeMailpit();
        _env.AddSite("alpha");
        using var engine = _env.CreateEngine();

        var report = await engine.StartAllAsync();

        try
        {
            Assert.True(report.Success, string.Join(" | ", report.Errors));
            Assert.Equal(["mysql", "apache"], report.NotInstalled);
            Assert.Equal(ServiceStatus.Running, Service(engine, "mailpit").Status);
            Assert.Equal(ServiceStatus.Stopped, Service(engine, "apache").Status);
            Assert.Equal(ServiceStatus.Stopped, Service(engine, "mysql").Status);
            Assert.Equal("mailpit.exe", Path.GetFileName(Assert.Single(_env.Runner.Started).FileName));
            // La configuration a été appliquée avant le démarrage (vhost du site, hosts).
            Assert.True(File.Exists(Path.Combine(SitesDir, "alpha.conf")));
            Assert.Single(_env.ElevatedCalls);
        }
        finally
        {
            await engine.StopAllAsync();
        }

        Assert.Equal(ServiceStatus.Stopped, Service(engine, "mailpit").Status);
    }

    [Fact]
    public async Task StartAllAsync_PortBusy_ReportsOwnerAndStartsNothing()
    {
        InstallFakeMailpit();
        var squatter = new TcpListener(IPAddress.Loopback, _env.Settings.MailpitSmtpPort);
        squatter.Start();
        try
        {
            using var engine = _env.CreateEngine();

            var report = await engine.StartAllAsync();

            Assert.False(report.Success);
            Assert.Contains(report.Errors, e => e.StartsWith(
                $"Port {_env.Settings.MailpitSmtpPort} utilisé par", StringComparison.Ordinal));
            Assert.Empty(_env.Runner.Started);
            Assert.All(engine.Services.Services, s => Assert.Equal(ServiceStatus.Stopped, s.Status));
        }
        finally
        {
            squatter.Stop();
        }
    }

    [Fact]
    public async Task StartAllAsync_NothingInstalled_ReportsWithoutApplying()
    {
        _env.AddSite("alpha");
        using var engine = _env.CreateEngine();

        var report = await engine.StartAllAsync();

        Assert.False(report.Success);
        Assert.Equal(["mysql", "apache", "mailpit"], report.NotInstalled);
        Assert.Contains("Aucun service", Assert.Single(report.Errors), StringComparison.Ordinal);
        Assert.Empty(_env.ElevatedCalls);
        Assert.Empty(_env.Runner.Started);
    }

    [Fact]
    public async Task StartAsync_And_StopAsync_SingleService()
    {
        InstallFakeMailpit();
        using var engine = _env.CreateEngine();

        var notInstalled = await engine.StartAsync("apache");
        Assert.False(notInstalled.Success);
        Assert.Equal(["apache"], notInstalled.NotInstalled);

        var report = await engine.StartAsync("MAILPIT");
        Assert.True(report.Success, string.Join(" | ", report.Errors));
        Assert.Equal(ServiceStatus.Running, Service(engine, "mailpit").Status);

        await engine.StopAsync("mailpit");
        Assert.Equal(ServiceStatus.Stopped, Service(engine, "mailpit").Status);

        await Assert.ThrowsAsync<ArgumentException>(() => engine.StartAsync("nginx"));
        await Assert.ThrowsAsync<ArgumentException>(() => engine.StopAsync("nginx"));
    }

    // ---- Désinstallation ----

    [Fact]
    public async Task UninstallAsync_ActiveVersion_Throws_AndKeepsFolder()
    {
        var dir = _env.InstallFake("php", "8.3.37");
        _env.Activate("php", "8.3.37");
        using var engine = _env.CreateEngine();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.UninstallAsync(ComponentKind.Php, "8.3.37"));

        Assert.Contains("version active", ex.Message, StringComparison.Ordinal);
        Assert.True(Directory.Exists(dir));
        Assert.True(engine.Store.IsInstalled(ComponentKind.Php, "8.3.37"));
    }

    [Fact]
    public async Task UninstallAsync_InactiveVersion_RemovesFolder()
    {
        _env.InstallFake("php", "8.3.37");
        var old = _env.InstallFake("php", "8.2.0");
        _env.Activate("php", "8.3.37");
        using var engine = _env.CreateEngine();

        await engine.UninstallAsync(ComponentKind.Php, "8.2.0");

        Assert.False(Directory.Exists(old));
        Assert.Equal(["8.3.37"], engine.Store.Installed(ComponentKind.Php));
    }

    [Fact]
    public async Task UninstallAsync_ServiceRunning_Throws_ThenSucceedsOnceStopped()
    {
        InstallFakeMailpit();
        var old = _env.InstallFake("mailpit", "1.20.0");
        using var engine = _env.CreateEngine();
        var report = await engine.StartAsync("mailpit");
        Assert.True(report.Success, string.Join(" | ", report.Errors));

        try
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => engine.UninstallAsync(ComponentKind.Mailpit, "1.20.0"));
            Assert.Contains("Mailpit", ex.Message, StringComparison.Ordinal);
            Assert.True(Directory.Exists(old));
        }
        finally
        {
            await engine.StopAsync("mailpit");
        }

        await engine.UninstallAsync(ComponentKind.Mailpit, "1.20.0");
        Assert.False(Directory.Exists(old));
    }

    private static ManagedService Service(StackREngine engine, string id) => engine.Services.Services.Single(s => s.Id == id);

    private static ComponentRelease ComposerRelease(byte[] payload) => new(
        ComponentKind.Composer, "2.8.1", "stable", new Uri("https://example.org/composer.phar"),
        Convert.ToHexStringLower(SHA256.HashData(payload)), payload.Length, PackageType.File, [], null);

    private static void InterlockedMax(ref int target, int value)
    {
        int seen;
        while ((seen = Volatile.Read(ref target)) < value && Interlocked.CompareExchange(ref target, value, seen) != seen)
        {
        }
    }

    private void InstallFakeMailpit()
    {
        var dir = _env.InstallFake("mailpit", "1.34.0");
        File.WriteAllBytes(Path.Combine(dir, "mailpit.exe"), []);
        _env.Activate("mailpit", "1.34.0");
        _env.Runner.ListenOnStart["mailpit.exe"] = _env.Settings.MailpitUiPort;
    }

    // Serveur factice dont la réponse attend un signal : simule un long téléchargement.
    private sealed class BlockingHandler(byte[] payload) : HttpMessageHandler
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK) { RequestMessage = request, Content = new ByteArrayContent(payload) };
        }
    }
}
