using System.Globalization;
using System.Resources;

namespace StackR.App.Resources;

/// <summary>
/// Accès typé aux chaînes localisées (français par défaut, anglais en satellite).
/// </summary>
public static partial class Strings
{
    // Une table par zone de l'interface (Strings = coquille ; les autres = pages), cherchées dans l'ordre.
    private static readonly ResourceManager[] Managers =
    [
        new("StackR.App.Resources.Strings", typeof(Strings).Assembly),
        new("StackR.App.Resources.DashboardSitesStrings", typeof(Strings).Assembly),
        new("StackR.App.Resources.VersionsDatabaseStrings", typeof(Strings).Assembly),
        new("StackR.App.Resources.LogsSettingsStrings", typeof(Strings).Assembly),
        new("StackR.App.Resources.FirstRunStrings", typeof(Strings).Assembly),
    ];

    /// <summary>Retourne la chaîne localisée, ou la clé si elle est absente.</summary>
    public static string Get(string name)
    {
        foreach (var manager in Managers)
        {
            try
            {
                if (manager.GetString(name, CultureInfo.CurrentUICulture) is { } value)
                {
                    return value;
                }
            }
            catch (MissingManifestResourceException)
            {
                // Table absente : on passe à la suivante.
            }
        }

        return name;
    }

    public static string AppName => Get(nameof(AppName));
    public static string AppTagline => Get(nameof(AppTagline));
    public static string NavDashboard => Get(nameof(NavDashboard));
    public static string NavSites => Get(nameof(NavSites));
    public static string NavVersions => Get(nameof(NavVersions));
    public static string NavDatabase => Get(nameof(NavDatabase));
    public static string NavLogs => Get(nameof(NavLogs));
    public static string NavSettings => Get(nameof(NavSettings));
    public static string DashboardSubtitle => Get(nameof(DashboardSubtitle));
    public static string SitesSubtitle => Get(nameof(SitesSubtitle));
    public static string VersionsSubtitle => Get(nameof(VersionsSubtitle));
    public static string DatabaseSubtitle => Get(nameof(DatabaseSubtitle));
    public static string LogsSubtitle => Get(nameof(LogsSubtitle));
    public static string SettingsSubtitle => Get(nameof(SettingsSubtitle));
    public static string PlaceholderTitle => Get(nameof(PlaceholderTitle));
    public static string PlaceholderBody => Get(nameof(PlaceholderBody));
    public static string TrayTooltip => Get(nameof(TrayTooltip));
    public static string TrayStartAll => Get(nameof(TrayStartAll));
    public static string TrayStopAll => Get(nameof(TrayStopAll));
    public static string TraySites => Get(nameof(TraySites));
    public static string TrayNoSites => Get(nameof(TrayNoSites));
    public static string TrayOpen => Get(nameof(TrayOpen));
    public static string TrayQuit => Get(nameof(TrayQuit));
    public static string TrayBackgroundTitle => Get(nameof(TrayBackgroundTitle));
    public static string TrayBackgroundMessage => Get(nameof(TrayBackgroundMessage));
    public static string QuitTitle => Get(nameof(QuitTitle));
    public static string QuitMessage => Get(nameof(QuitMessage));
    public static string QuitYes => Get(nameof(QuitYes));
    public static string QuitNo => Get(nameof(QuitNo));
    public static string QuitCancel => Get(nameof(QuitCancel));
}
