using System.Security.Cryptography;
using StackR.Core.Config;
using StackR.Core.Manifests;
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

        Assert.Contains(@"%~dp0..\php\8.4.28\php.exe", File.ReadAllText(Path.Combine(_env.Paths.Shims, "php.cmd")));
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
}
