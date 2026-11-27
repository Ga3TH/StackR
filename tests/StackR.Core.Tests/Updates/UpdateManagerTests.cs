using StackR.Core.Settings;
using StackR.Core.Updates;

namespace StackR.Core.Tests.Updates;

/// <summary>Logique du bandeau de mise à jour (visibilité, masquage par version, portable/installé).</summary>
public sealed class UpdateManagerTests
{
    private static readonly UpdateInfo Installable = new(
        new Version(0, 1, 5), "Notes", new Uri("https://example.org/s.exe"), new string('b', 64), 10, new Uri("https://example.org/r"));

    private static readonly UpdateInfo NoInstaller = Installable with { InstallerUrl = null, Sha256 = null };

    private static (UpdateManager Manager, Func<StackRSettings> Settings) Create(UpdateCheckResult result, bool installed, StackRSettings? initial = null)
    {
        var settings = initial ?? new StackRSettings();
        var manager = new UpdateManager(
            _ => Task.FromResult(result),
            () => settings,
            change => settings = change(settings),
            installed);
        return (manager, () => settings);
    }

    [Fact]
    public async Task Check_Available_Installed_ShowsBannerAndCanInstall()
    {
        var (m, _) = Create(new(UpdateCheckStatus.Available, Installable), installed: true);
        var changed = 0;
        m.Changed += (_, _) => changed++;

        var status = await m.CheckAsync(manual: false);

        Assert.Equal(UpdateCheckStatus.Available, status);
        Assert.True(m.IsBannerVisible);
        Assert.True(m.CanInstallHere);
        Assert.Equal(1, changed);
    }

    [Fact]
    public async Task Check_Available_Portable_CannotInstallHere()
    {
        var (m, _) = Create(new(UpdateCheckStatus.Available, Installable), installed: false);

        await m.CheckAsync(manual: false);

        Assert.True(m.IsBannerVisible);
        Assert.False(m.CanInstallHere);
    }

    [Fact]
    public async Task Check_Available_NoInstaller_CannotInstallHere()
    {
        var (m, _) = Create(new(UpdateCheckStatus.Available, NoInstaller), installed: true);

        await m.CheckAsync(manual: false);

        Assert.True(m.IsBannerVisible);
        Assert.False(m.CanInstallHere);
    }

    [Fact]
    public async Task Dismiss_HidesThatVersion_AndPersists()
    {
        var (m, settings) = Create(new(UpdateCheckStatus.Available, Installable), installed: true);
        await m.CheckAsync(manual: false);

        m.Dismiss();

        Assert.False(m.IsBannerVisible);
        Assert.Equal("0.1.5", settings().DismissedUpdateVersion);
    }

    [Fact]
    public async Task DismissedVersion_StaysHiddenAfterRestart_ButNewerVersionShows()
    {
        var (m, _) = Create(new(UpdateCheckStatus.Available, Installable), installed: true, new StackRSettings { DismissedUpdateVersion = "0.1.5" });
        await m.CheckAsync(manual: false);
        Assert.False(m.IsBannerVisible);

        var (m2, _) = Create(new(UpdateCheckStatus.Available, Installable with { Version = new Version(0, 1, 6) }), installed: true, new StackRSettings { DismissedUpdateVersion = "0.1.5" });
        await m2.CheckAsync(manual: false);
        Assert.True(m2.IsBannerVisible);
    }

    [Fact]
    public async Task ManualCheck_RevealsDismissedBanner()
    {
        var (m, settings) = Create(new(UpdateCheckStatus.Available, Installable), installed: true, new StackRSettings { DismissedUpdateVersion = "0.1.5" });

        await m.CheckAsync(manual: true);

        Assert.True(m.IsBannerVisible);
        Assert.Equal("", settings().DismissedUpdateVersion);
    }

    [Fact]
    public async Task AutomaticCheck_DisabledSetting_DoesNothing()
    {
        var called = false;
        var settings = new StackRSettings { AutoCheckUpdates = false };
        var m = new UpdateManager(_ => { called = true; return Task.FromResult(new UpdateCheckResult(UpdateCheckStatus.Available, Installable)); }, () => settings, _ => { }, true);

        await m.CheckAsync(manual: false);
        Assert.False(called);
        Assert.False(m.IsBannerVisible);

        await m.CheckAsync(manual: true);
        Assert.True(called);
        Assert.True(m.IsBannerVisible);
    }

    [Fact]
    public async Task UpToDate_ClearsBanner_FailureKeepsIt()
    {
        var result = new UpdateCheckResult(UpdateCheckStatus.Available, Installable);
        var m = new UpdateManager(_ => Task.FromResult(result), () => new StackRSettings(), _ => { }, true);
        await m.CheckAsync(manual: false);
        Assert.True(m.IsBannerVisible);

        result = new UpdateCheckResult(UpdateCheckStatus.Failed, null);
        await m.CheckAsync(manual: false);
        Assert.True(m.IsBannerVisible);

        result = new UpdateCheckResult(UpdateCheckStatus.UpToDate, null);
        Assert.Equal(UpdateCheckStatus.UpToDate, await m.CheckAsync(manual: false));
        Assert.False(m.IsBannerVisible);
        Assert.Null(m.Available);
    }
}
