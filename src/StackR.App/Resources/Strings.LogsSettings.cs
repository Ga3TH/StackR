namespace StackR.App.Resources;

/// <summary>Chaînes : Journaux et Paramètres (valeurs dans LogsSettingsStrings.resx et LogsSettingsStrings.en.resx).</summary>
public static partial class Strings
{
    public static string LogsFileLabel => Get(nameof(LogsFileLabel));
    public static string LogsFollow => Get(nameof(LogsFollow));
    public static string LogsClear => Get(nameof(LogsClear));
    public static string LogsCopy => Get(nameof(LogsCopy));
    public static string LogsOpenFolder => Get(nameof(LogsOpenFolder));
    public static string LogsEmptyTitle => Get(nameof(LogsEmptyTitle));
    public static string LogsEmptyBody => Get(nameof(LogsEmptyBody));
    public static string LogsEmptyFile => Get(nameof(LogsEmptyFile));
    public static string LogsApacheError => Get(nameof(LogsApacheError));
    public static string LogsApacheAccess => Get(nameof(LogsApacheAccess));
    public static string LogsApacheOutput => Get(nameof(LogsApacheOutput));
    public static string LogsMySqlError => Get(nameof(LogsMySqlError));
    public static string LogsMySqlOutput => Get(nameof(LogsMySqlOutput));
    public static string LogsMailpit => Get(nameof(LogsMailpit));
    public static string LogsCopied => Get(nameof(LogsCopied));
    public static string LogsCopyFailed => Get(nameof(LogsCopyFailed));
    public static string LogsOpenFailed => Get(nameof(LogsOpenFailed));
    public static string LogsLineCount => Get(nameof(LogsLineCount));
    public static string SetSectionGeneral => Get(nameof(SetSectionGeneral));
    public static string SetSectionFolders => Get(nameof(SetSectionFolders));
    public static string SetSectionHttps => Get(nameof(SetSectionHttps));
    public static string SetSectionPorts => Get(nameof(SetSectionPorts));
    public static string SetSectionAppearance => Get(nameof(SetSectionAppearance));
    public static string SetSectionAbout => Get(nameof(SetSectionAbout));
    public static string SetRestartTitle => Get(nameof(SetRestartTitle));
    public static string SetRestartMessage => Get(nameof(SetRestartMessage));
    public static string SetRestartNow => Get(nameof(SetRestartNow));
    public static string SetRoot => Get(nameof(SetRoot));
    public static string SetRootDesc => Get(nameof(SetRootDesc));
    public static string SetWww => Get(nameof(SetWww));
    public static string SetWwwDesc => Get(nameof(SetWwwDesc));
    public static string SetBrowse => Get(nameof(SetBrowse));
    public static string SetChange => Get(nameof(SetChange));
    public static string SetResetDefault => Get(nameof(SetResetDefault));
    public static string SetPickFolderTitle => Get(nameof(SetPickFolderTitle));
    public static string SetStartWithWindows => Get(nameof(SetStartWithWindows));
    public static string SetStartWithWindowsDesc => Get(nameof(SetStartWithWindowsDesc));
    public static string SetAutoStart => Get(nameof(SetAutoStart));
    public static string SetAutoStartDesc => Get(nameof(SetAutoStartDesc));
    public static string SetHttps => Get(nameof(SetHttps));
    public static string SetHttpsDesc => Get(nameof(SetHttpsDesc));
    public static string SetCaStatus => Get(nameof(SetCaStatus));
    public static string SetCaTrusted => Get(nameof(SetCaTrusted));
    public static string SetCaNotTrusted => Get(nameof(SetCaNotTrusted));
    public static string SetCaChecking => Get(nameof(SetCaChecking));
    public static string SetCaTrust => Get(nameof(SetCaTrust));
    public static string SetCaTrustDesc => Get(nameof(SetCaTrustDesc));
    public static string SetCaTrustDeclined => Get(nameof(SetCaTrustDeclined));
    public static string SetCaTrustDone => Get(nameof(SetCaTrustDone));
    public static string SetHttpsApplied => Get(nameof(SetHttpsApplied));
    public static string SetPortHttp => Get(nameof(SetPortHttp));
    public static string SetPortHttps => Get(nameof(SetPortHttps));
    public static string SetPortMySql => Get(nameof(SetPortMySql));
    public static string SetPortMailpitUi => Get(nameof(SetPortMailpitUi));
    public static string SetPortMailpitSmtp => Get(nameof(SetPortMailpitSmtp));
    public static string SetPortsDesc => Get(nameof(SetPortsDesc));
    public static string SetPortsSave => Get(nameof(SetPortsSave));
    public static string SetPortInvalid => Get(nameof(SetPortInvalid));
    public static string SetPortDuplicate => Get(nameof(SetPortDuplicate));
    public static string SetPortsSaved => Get(nameof(SetPortsSaved));
    public static string SetTheme => Get(nameof(SetTheme));
    public static string SetThemeDesc => Get(nameof(SetThemeDesc));
    public static string SetThemeSystem => Get(nameof(SetThemeSystem));
    public static string SetThemeLight => Get(nameof(SetThemeLight));
    public static string SetThemeDark => Get(nameof(SetThemeDark));
    public static string SetLanguage => Get(nameof(SetLanguage));
    public static string SetLanguageDesc => Get(nameof(SetLanguageDesc));
    public static string SetLanguageFr => Get(nameof(SetLanguageFr));
    public static string SetLanguageEn => Get(nameof(SetLanguageEn));
    public static string SetAboutVersion => Get(nameof(SetAboutVersion));
    public static string SetAboutGitHub => Get(nameof(SetAboutGitHub));
    public static string SetAboutRootFolder => Get(nameof(SetAboutRootFolder));
    public static string SetAboutLogsFolder => Get(nameof(SetAboutLogsFolder));
    public static string SetOpenFailed => Get(nameof(SetOpenFailed));
    public static string SetSaveFailed => Get(nameof(SetSaveFailed));
    public static string SetStartWithWindowsFailed => Get(nameof(SetStartWithWindowsFailed));
    public static string SetApplyWarning => Get(nameof(SetApplyWarning));
    public static string SetApplyFailed => Get(nameof(SetApplyFailed));

    /// <summary>Remplace {0}, {1}... du modèle par les valeurs (sans string.Format, les modèles viennent des ressources).</summary>
    public static string Fill(string template, params object[] values)
    {
        for (var i = 0; i < values.Length; i++)
        {
            template = template.Replace("{" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}",
                Convert.ToString(values[i], System.Globalization.CultureInfo.CurrentCulture), StringComparison.Ordinal);
        }

        return template;
    }
}
