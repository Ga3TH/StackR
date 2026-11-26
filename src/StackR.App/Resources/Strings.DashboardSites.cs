namespace StackR.App.Resources;

/// <summary>Chaînes : Tableau de bord et Sites (valeurs dans DashboardSitesStrings.resx et DashboardSitesStrings.en.resx).</summary>
public static partial class Strings
{
    public static string DashOpenFailed => Get(nameof(DashOpenFailed));
    public static string DashStopFailed => Get(nameof(DashStopFailed));
    public static string DashSyncFailed => Get(nameof(DashSyncFailed));
    public static string DashStartFailed => Get(nameof(DashStartFailed));
    public static string DashNothingInstalled => Get(nameof(DashNothingInstalled));
    public static string DashSkippedNotInstalled => Get(nameof(DashSkippedNotInstalled));
    public static string DashPorts => Get(nameof(DashPorts));
    public static string DashStatusRunning => Get(nameof(DashStatusRunning));
    public static string DashStatusStarting => Get(nameof(DashStatusStarting));
    public static string DashStatusStopping => Get(nameof(DashStatusStopping));
    public static string DashStatusError => Get(nameof(DashStatusError));
    public static string DashStatusStopped => Get(nameof(DashStatusStopped));
    public static string DashStart => Get(nameof(DashStart));
    public static string DashStop => Get(nameof(DashStop));
    public static string DashRetry => Get(nameof(DashRetry));
    public static string DashVersion => Get(nameof(DashVersion));
    public static string DashNotInstalled => Get(nameof(DashNotInstalled));
    public static string DashPhpActivated => Get(nameof(DashPhpActivated));
    public static string DashPhpFailed => Get(nameof(DashPhpFailed));
    public static string DashTerminalFailed => Get(nameof(DashTerminalFailed));
    public static string DashFolderFailed => Get(nameof(DashFolderFailed));
    public static string DashLastError => Get(nameof(DashLastError));
    public static string DashNotInstalledHint => Get(nameof(DashNotInstalledHint));
    public static string DashGoToVersions => Get(nameof(DashGoToVersions));
    public static string DashStartAll => Get(nameof(DashStartAll));
    public static string DashStopAll => Get(nameof(DashStopAll));
    public static string DashPhpActive => Get(nameof(DashPhpActive));
    public static string DashPhpHint => Get(nameof(DashPhpHint));
    public static string DashShortcuts => Get(nameof(DashShortcuts));
    public static string DashOpenTerminal => Get(nameof(DashOpenTerminal));
    public static string DashOpenWww => Get(nameof(DashOpenWww));
    public static string DashOpenMailpit => Get(nameof(DashOpenMailpit));
    public static string DashWwwFormat => Get(nameof(DashWwwFormat));
    public static string DashRecentSites => Get(nameof(DashRecentSites));
    public static string DashNoSites => Get(nameof(DashNoSites));
    public static string SitesLoadFailed => Get(nameof(SitesLoadFailed));
    public static string SitesVsCodeFailed => Get(nameof(SitesVsCodeFailed));
    public static string SitesNew => Get(nameof(SitesNew));
    public static string SitesRefresh => Get(nameof(SitesRefresh));
    public static string SitesRootFormat => Get(nameof(SitesRootFormat));
    public static string SitesOpen => Get(nameof(SitesOpen));
    public static string SitesFolder => Get(nameof(SitesFolder));
    public static string SitesVsCode => Get(nameof(SitesVsCode));
    public static string SitesEmptyTitle => Get(nameof(SitesEmptyTitle));
    public static string SitesEmptyBody => Get(nameof(SitesEmptyBody));
    public static string SitesSkippedHeader => Get(nameof(SitesSkippedHeader));
    public static string SitesSkippedHint => Get(nameof(SitesSkippedHint));
    public static string SitesNewTitle => Get(nameof(SitesNewTitle));
    public static string SitesNameLabel => Get(nameof(SitesNameLabel));
    public static string SitesNamePlaceholder => Get(nameof(SitesNamePlaceholder));
    public static string SitesTemplateLabel => Get(nameof(SitesTemplateLabel));
    public static string SitesTemplateEmpty => Get(nameof(SitesTemplateEmpty));
    public static string SitesTemplateEmptyHint => Get(nameof(SitesTemplateEmptyHint));
    public static string SitesTemplateLaravel => Get(nameof(SitesTemplateLaravel));
    public static string SitesTemplateLaravelHint => Get(nameof(SitesTemplateLaravelHint));
    public static string SitesLaravelRunning => Get(nameof(SitesLaravelRunning));
    public static string SitesCreate => Get(nameof(SitesCreate));
    public static string SitesCancelRun => Get(nameof(SitesCancelRun));
    public static string SitesClose => Get(nameof(SitesClose));
    public static string SitesNameChars => Get(nameof(SitesNameChars));
    public static string SitesNameInvalid => Get(nameof(SitesNameInvalid));
    public static string SitesNameExists => Get(nameof(SitesNameExists));
    public static string SitesLaravelNeedsPhp => Get(nameof(SitesLaravelNeedsPhp));
    public static string SitesLaravelNeedsComposer => Get(nameof(SitesLaravelNeedsComposer));
    public static string SitesLaravelIncomplete => Get(nameof(SitesLaravelIncomplete));
    public static string SitesLaravelPreparing => Get(nameof(SitesLaravelPreparing));
    public static string SitesLaravelFailed => Get(nameof(SitesLaravelFailed));
    public static string SitesCreated => Get(nameof(SitesCreated));
    public static string SitesCreatedWarnings => Get(nameof(SitesCreatedWarnings));
    public static string SitesCancelled => Get(nameof(SitesCancelled));
    public static string SitesCreateFailed => Get(nameof(SitesCreateFailed));

    /// <summary>Met en forme un modèle de message (culture de l'interface).</summary>
    public static string Format(string template, params object?[] args) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, template, args);
}
