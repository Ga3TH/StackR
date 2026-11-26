using System.Security.Cryptography;
using StackR.Core.Components;
using StackR.Core.Manifests;
using StackR.Core.Settings;

namespace StackR.Core.Tests.Components;

public sealed class FirstRunInstallTests : IDisposable
{
    private readonly EngineTestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [Fact]
    public async Task InstallWithoutApply_ActivatesFirstVersion_WithoutTouchingHostsOrConfig()
    {
        var payload = "<?php // composer"u8.ToArray();
        var sha = Convert.ToHexStringLower(SHA256.HashData(payload));
        ComponentRelease Release(string v) => new(
            ComponentKind.Composer, v, "stable", new Uri($"https://example.org/{v}/composer.phar"), sha, payload.Length,
            PackageType.File, [], null);
        _env.Handler = new EngineTestEnvironment.BytesHandler(payload);
        _env.Manifest = new Manifest([Release("2.8.1"), Release("2.9.0")]);
        _env.AddSite("blog");
        using var engine = _env.CreateEngine();

        await engine.InstallWithoutApplyAsync(Release("2.8.1"), null, CancellationToken.None);
        await engine.InstallWithoutApplyAsync(Release("2.9.0"), null, CancellationToken.None);

        Assert.Equal("2.8.1", SettingsStore.Load(_env.SettingsPath).ActiveVersions["composer"]);
        Assert.True(engine.Store.IsInstalled(ComponentKind.Composer, "2.9.0"));
        // Aucune application de configuration : ni élévation pour le fichier hosts, ni vhost généré.
        Assert.Empty(_env.ElevatedCalls);
        Assert.False(Directory.Exists(Path.Combine(_env.Paths.Etc, "sites")));
    }

    [Fact]
    public async Task InstallWithoutApply_Cancelled_LeavesSettingsUnchanged()
    {
        var payload = "<?php // composer"u8.ToArray();
        var release = new ComponentRelease(
            ComponentKind.Composer, "2.8.1", "stable", new Uri("https://example.org/composer.phar"),
            Convert.ToHexStringLower(SHA256.HashData(payload)), payload.Length, PackageType.File, [], null);
        _env.Handler = new EngineTestEnvironment.BytesHandler(payload);
        using var engine = _env.CreateEngine();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => engine.InstallWithoutApplyAsync(release, null, cts.Token));

        Assert.False(engine.Settings.ActiveVersions.ContainsKey("composer"));
        Assert.False(engine.Store.IsInstalled(ComponentKind.Composer, "2.8.1"));
    }
}
