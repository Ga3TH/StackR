using StackR.Core.Components;
using StackR.Core.Manifests;
using StackR.Core.Services;
using StackR.Core.Settings;

namespace StackR.Core.Tests;

// Les changements de configuration atteignent les services déjà lancés (Apache ne relit pas httpd.conf).
public sealed class EngineConfigRestartTests : IDisposable
{
    private readonly EngineTestEnvironment _env = new();

    public EngineConfigRestartTests()
    {
        _env.Settings = _env.Settings with { HttpsEnabled = false };
        var apache = _env.InstallFake("apache", "2.4.71");
        Directory.CreateDirectory(Path.Combine(apache, "bin"));
        File.WriteAllBytes(Path.Combine(apache, "bin", "httpd.exe"), []);
        _env.InstallFake("php", "8.3.37");
        _env.Activate("apache", "2.4.71");
        _env.Activate("php", "8.3.37");
        _env.Runner.ListenOnStart["httpd.exe"] = _env.Settings.HttpPort;

        // Le port du faux Apache est libéré quand la validation précède l'arrêt : le redémarrage peut le rouvrir.
        _env.Runner.OnRun = (_, args) =>
        {
            if (args.StartsWith("-t", StringComparison.Ordinal) && _env.Runner.Started.Count > 0)
            {
                _env.Runner.StopListeners();
            }

            return TestExitCode;
        };
    }

    private int TestExitCode { get; set; }

    public void Dispose() => _env.Dispose();

    private static ManagedService Apache(StackREngine engine) => engine.Services.Services.Single(s => s.Id == "apache");

    private int HttpdStarts => _env.Runner.Started.Count(p => Path.GetFileName(p.FileName) == "httpd.exe");

    [Fact]
    public async Task ApplyConfiguration_VhostChangedWhileApacheRunning_RestartsApache()
    {
        using var engine = _env.CreateEngine();
        var report = await engine.StartAsync("apache");
        Assert.True(report.Success, string.Join(" | ", report.Errors));
        try
        {
            Assert.Equal(1, HttpdStarts);

            _env.AddSite("alpha");
            var result = await engine.ApplyConfigurationAsync();

            Assert.Equal(["Apache"], result.RestartedServices);
            Assert.Equal(2, HttpdStarts);
            Assert.Equal(ServiceStatus.Running, Apache(engine).Status);
            Assert.Empty(result.Warnings);
        }
        finally
        {
            await engine.StopAllAsync();
        }
    }

    [Fact]
    public async Task ApplyConfiguration_NothingChangedWhileApacheRunning_DoesNotRestart()
    {
        using var engine = _env.CreateEngine();
        var report = await engine.StartAsync("apache");
        Assert.True(report.Success, string.Join(" | ", report.Errors));
        try
        {
            var runsBefore = _env.Runner.Runs.Count;

            var result = await engine.ApplyConfigurationAsync();

            Assert.Empty(result.RestartedServices);
            Assert.Equal(1, HttpdStarts);
            Assert.Equal(runsBefore, _env.Runner.Runs.Count);
            Assert.Equal(ServiceStatus.Running, Apache(engine).Status);
        }
        finally
        {
            await engine.StopAllAsync();
        }
    }

    [Fact]
    public async Task ApplyConfiguration_ConfigChangedButApacheStopped_DoesNotStartIt()
    {
        using var engine = _env.CreateEngine();
        await engine.ApplyConfigurationAsync();
        _env.AddSite("alpha");

        var result = await engine.ApplyConfigurationAsync();

        Assert.Empty(result.RestartedServices);
        Assert.Equal(0, HttpdStarts);
        Assert.Equal(ServiceStatus.Stopped, Apache(engine).Status);
        Assert.True(File.Exists(Path.Combine(_env.Paths.Etc, "sites", "alpha.conf")));
    }

    [Fact]
    public async Task ApplyConfiguration_InvalidNewConfig_KeepsRunningApacheAndWarnsInFrench()
    {
        using var engine = _env.CreateEngine();
        var report = await engine.StartAsync("apache");
        Assert.True(report.Success, string.Join(" | ", report.Errors));
        try
        {
            TestExitCode = 1;
            _env.AddSite("alpha");

            var result = await engine.ApplyConfigurationAsync();

            Assert.Empty(result.RestartedServices);
            Assert.Equal(1, HttpdStarts);
            Assert.Equal(ServiceStatus.Running, Apache(engine).Status);
            Assert.Contains("invalide", Assert.Single(result.Warnings), StringComparison.Ordinal);
        }
        finally
        {
            await engine.StopAllAsync();
        }
    }

    [Fact]
    public async Task ApplyConfiguration_MySqlStopped_WritesMyIniWithoutStartingIt()
    {
        var mysql = _env.InstallFake("mysql", "8.4.9");
        _env.Activate("mysql", "8.4.9");
        Assert.True(Directory.Exists(mysql));
        using var engine = _env.CreateEngine();
        await engine.ApplyConfigurationAsync();

        var result = await engine.ApplyConfigurationAsync();

        Assert.Empty(result.RestartedServices);
        Assert.DoesNotContain(_env.Runner.Started, p => Path.GetFileName(p.FileName) == "mysqld.exe");
        Assert.True(File.Exists(Path.Combine(_env.Paths.Etc, "my.ini")));
    }
}

// Installation automatique des composants par défaut.
public sealed class EngineDefaultComponentsTests : IDisposable
{
    private readonly EngineTestEnvironment _env = new();
    private readonly byte[] _payload = "<?php // fichier"u8.ToArray();

    public void Dispose() => _env.Dispose();

    private ComponentRelease Release(ComponentKind kind, string version) => new(
        kind, version, "stable", new Uri($"https://example.org/{kind}/{version}/{kind}.bin"),
        Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(_payload)), _payload.Length,
        PackageType.File, [], null);

    private StackREngine CreateEngine(params ComponentRelease[] releases)
    {
        _env.Handler = new EngineTestEnvironment.BytesHandler(_payload);
        _env.Manifest = new Manifest(releases);
        return _env.CreateEngine();
    }

    [Fact]
    public void MissingDefaultComponents_NothingInstalled_ListsRecommendedOfEachDefault()
    {
        using var engine = CreateEngine(
            Release(ComponentKind.Adminer, "6.3.0"),
            Release(ComponentKind.Adminer, "6.3.1"),
            Release(ComponentKind.Composer, "2.8.1"),
            Release(ComponentKind.Node, "24.0.0"));

        var missing = engine.MissingDefaultComponents();

        Assert.Equal([("Composer", "2.8.1"), ("Adminer", "6.3.1")], missing.Select(r => (r.Component.ToString(), r.Version)).ToArray());
    }

    [Fact]
    public void MissingDefaultComponents_SomeInstalled_SkipsThem()
    {
        _env.InstallFake("composer", "2.7.0");
        using var engine = CreateEngine(Release(ComponentKind.Adminer, "6.3.1"), Release(ComponentKind.Composer, "2.8.1"));

        var missing = engine.MissingDefaultComponents();

        Assert.Equal(ComponentKind.Adminer, Assert.Single(missing).Component);
    }

    [Fact]
    public async Task EnsureDefaultComponentsAsync_InstallsActivatesAndReportsProgress()
    {
        using var engine = CreateEngine(Release(ComponentKind.Adminer, "6.3.1"), Release(ComponentKind.Composer, "2.8.1"));
        var seen = new List<DefaultInstallProgress>();

        var report = await engine.EnsureDefaultComponentsAsync(new SyncProgress(seen.Add), CancellationToken.None);

        Assert.True(report.Success, string.Join(" | ", report.Errors));
        Assert.Equal(2, report.Installed.Count);
        Assert.Equal("6.3.1", engine.ActiveVersion(ComponentKind.Adminer));
        Assert.Equal("2.8.1", SettingsStore.Load(_env.SettingsPath).ActiveVersions["composer"]);
        Assert.NotNull(engine.ActiveDirectory(ComponentKind.Composer));
        Assert.Contains(seen, p => p.Release.Component == ComponentKind.Adminer && p.Index == 2 && p.Count == 2);
        Assert.Empty(engine.MissingDefaultComponents());
    }

    [Fact]
    public async Task EnsureDefaultComponentsAsync_NothingMissing_DoesNothing()
    {
        _env.InstallFake("composer", "2.7.0");
        using var engine = CreateEngine(Release(ComponentKind.Composer, "2.8.1"));

        var report = await engine.EnsureDefaultComponentsAsync(null, CancellationToken.None);

        Assert.True(report.Success);
        Assert.Empty(report.Installed);
        Assert.Equal(["2.7.0"], engine.Store.Installed(ComponentKind.Composer));
    }

    [Fact]
    public async Task EnsureDefaultComponentsAsync_BadChecksum_ReportsErrorAndContinues()
    {
        var bad = Release(ComponentKind.Adminer, "6.3.1") with { Sha256 = new string('0', 64) };
        using var engine = CreateEngine(bad, Release(ComponentKind.Composer, "2.8.1"));

        var report = await engine.EnsureDefaultComponentsAsync(null, CancellationToken.None);

        Assert.False(report.Success);
        Assert.Contains("Adminer 6.3.1", Assert.Single(report.Errors), StringComparison.Ordinal);
        Assert.Equal(ComponentKind.Composer, Assert.Single(report.Installed).Component);
        Assert.Empty(engine.Store.Installed(ComponentKind.Adminer));
    }

    private sealed class SyncProgress(Action<DefaultInstallProgress> handler) : IProgress<DefaultInstallProgress>
    {
        public void Report(DefaultInstallProgress value) => handler(value);
    }
}
