using System.Collections.ObjectModel;
using System.IO;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Security;
using System.Security.Cryptography;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using StackR.App.Resources;
using StackR.App.Services;
using StackR.Core;
using StackR.Core.Settings;
using Wpf.Ui.Controls;

namespace StackR.App.ViewModels;

/// <summary>Choix d'une liste déroulante : identifiant stocké dans les paramètres et libellé affiché.</summary>
public sealed record ChoiceOption(string Id, string Name);

/// <summary>Page « Paramètres » : chaque réglage est enregistré tout de suite via <see cref="StackREngine.UpdateSettings"/>.</summary>
public sealed partial class SettingsViewModel : PageViewModelBase
{
    public const string GitHubUrl = "https://github.com/Ga3TH/StackR";

    private readonly StackREngine _engine;
    private readonly AppShell _shell;
    private readonly string _initialLanguage;
    private bool _loading = true;
    private bool _syncingTheme;

    public SettingsViewModel(StackREngine engine, AppShell shell)
        : base(Strings.NavSettings, Strings.SettingsSubtitle)
    {
        _engine = engine;
        _shell = shell;

        Themes =
        [
            new("System", Strings.SetThemeSystem),
            new("Light", Strings.SetThemeLight),
            new("Dark", Strings.SetThemeDark),
        ];
        Languages =
        [
            new("fr", Strings.SetLanguageFr),
            new("en", Strings.SetLanguageEn),
        ];

        var s = engine.Settings;
        _initialLanguage = s.Language;
        RootPath = s.Root;
        WwwPath = WwwDisplay(s);
        StartWithWindows = s.StartWithWindows;
        AutoStartServices = s.AutoStartServices;
        HttpsEnabled = s.HttpsEnabled;
        HttpPort = s.HttpPort;
        HttpsPort = s.HttpsPort;
        MySqlPort = s.MySqlPort;
        MailpitUiPort = s.MailpitUiPort;
        MailpitSmtpPort = s.MailpitSmtpPort;
        SelectedTheme = Themes.FirstOrDefault(t => t.Id == s.Theme) ?? Themes[0];
        SelectedLanguage = Languages.FirstOrDefault(l => l.Id == s.Language) ?? Languages[0];
        FollowSystemTheme = SelectedTheme.Id == "System";
        IsDarkMode = ThemeApplier.IsDarkNow();
        NeedsRestart = engine.RestartRequired;
        CaStatusText = Strings.SetCaChecking;
        _loading = false;
    }

    public ObservableCollection<ChoiceOption> Themes { get; }

    public ObservableCollection<ChoiceOption> Languages { get; }

    public string VersionText { get; } = ReadVersion();

    public string LogsPath => _engine.Paths.Logs;

    [ObservableProperty]
    public partial string RootPath { get; set; }

    [ObservableProperty]
    public partial string WwwPath { get; set; }

    [ObservableProperty]
    public partial bool StartWithWindows { get; set; }

    [ObservableProperty]
    public partial bool AutoStartServices { get; set; }

    [ObservableProperty]
    public partial bool HttpsEnabled { get; set; }

    [ObservableProperty]
    public partial double? HttpPort { get; set; }

    [ObservableProperty]
    public partial double? HttpsPort { get; set; }

    [ObservableProperty]
    public partial double? MySqlPort { get; set; }

    [ObservableProperty]
    public partial double? MailpitUiPort { get; set; }

    [ObservableProperty]
    public partial double? MailpitSmtpPort { get; set; }

    [ObservableProperty]
    public partial ChoiceOption SelectedTheme { get; set; }

    [ObservableProperty]
    public partial bool IsDarkMode { get; set; }

    [ObservableProperty]
    public partial bool FollowSystemTheme { get; set; }

    [ObservableProperty]
    public partial ChoiceOption SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool NeedsRestart { get; set; }

    [ObservableProperty]
    public partial string CaStatusText { get; set; }

    [ObservableProperty]
    public partial bool CaTrusted { get; set; }

    [ObservableProperty]
    public partial bool CaBusy { get; set; }

    [ObservableProperty]
    public partial string Message { get; set; } = "";

    [ObservableProperty]
    public partial InfoBarSeverity MessageSeverity { get; set; } = InfoBarSeverity.Informational;

    [ObservableProperty]
    public partial bool IsMessageOpen { get; set; }

    public bool CanTrustCa => !CaTrusted && !CaBusy;

    /// <summary>Rafraîchit l'état de confiance de la CA (appelé à l'affichage de la page).</summary>
    public async Task RefreshAsync()
    {
        try
        {
            CaTrusted = await Task.Run(_engine.Ca.IsTrusted);
            CaStatusText = CaTrusted ? Strings.SetCaTrusted : Strings.SetCaNotTrusted;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException)
        {
            CaTrusted = false;
            CaStatusText = Strings.SetCaNotTrusted;
        }

        NeedsRestart = _engine.RestartRequired || SelectedLanguage.Id != _initialLanguage;
    }

    partial void OnCaTrustedChanged(bool value) => OnPropertyChanged(nameof(CanTrustCa));

    partial void OnCaBusyChanged(bool value) => OnPropertyChanged(nameof(CanTrustCa));

    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_loading)
        {
            return;
        }

        var exe = Environment.ProcessPath;
        try
        {
            if (string.IsNullOrEmpty(exe))
            {
                throw new InvalidOperationException("exécutable introuvable");
            }

            // SetStartWithWindows écrit « "exe" --minimized » : l'application démarre réduite.
            _engine.Shell.SetStartWithWindows(value, exe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException or InvalidOperationException)
        {
            RevertWithoutSaving(() => StartWithWindows = !value);
            ShowError(Strings.SetStartWithWindowsFailed, ex.Message);
            return;
        }

        Save(s => s with { StartWithWindows = value });
    }

    partial void OnAutoStartServicesChanged(bool value)
    {
        if (!_loading)
        {
            Save(s => s with { AutoStartServices = value });
        }
    }

    partial void OnHttpsEnabledChanged(bool value)
    {
        if (!_loading)
        {
            _ = ApplyHttpsAsync(value);
        }
    }

    partial void OnSelectedThemeChanged(ChoiceOption value)
    {
        if (_loading || value is null)
        {
            return;
        }

        Save(s => s with { Theme = value.Id });
        ThemeApplier.Apply(value.Id);
        SyncThemeToggles();
    }

    /// <summary>Interrupteur « Mode sombre » : actif = Sombre, inactif = Clair (désactive « Suivre Windows »).</summary>
    partial void OnIsDarkModeChanged(bool value)
    {
        if (_loading || _syncingTheme)
        {
            return;
        }

        SelectedTheme = Themes.First(t => t.Id == (value ? "Dark" : "Light"));
    }

    /// <summary>Case « Suivre Windows » : cochée = Système ; décochée = thème actuellement affiché.</summary>
    partial void OnFollowSystemThemeChanged(bool value)
    {
        if (_loading || _syncingTheme)
        {
            return;
        }

        var id = value ? "System" : (ThemeApplier.IsDarkNow() ? "Dark" : "Light");
        SelectedTheme = Themes.First(t => t.Id == id);
    }

    private void SyncThemeToggles()
    {
        _syncingTheme = true;
        try
        {
            FollowSystemTheme = SelectedTheme.Id == "System";
            IsDarkMode = ThemeApplier.IsDarkNow();
        }
        finally
        {
            _syncingTheme = false;
        }
    }

    partial void OnSelectedLanguageChanged(ChoiceOption value)
    {
        if (_loading || value is null)
        {
            return;
        }

        Save(s => s with { Language = value.Id });
        NeedsRestart = _engine.RestartRequired || value.Id != _initialLanguage;
    }

    private async Task ApplyHttpsAsync(bool enabled)
    {
        if (!Save(s => s with { HttpsEnabled = enabled }))
        {
            return;
        }

        try
        {
            var result = await _engine.ApplyConfigurationAsync();
            if (result.Warnings.Count > 0)
            {
                ShowMessage(InfoBarSeverity.Warning, Strings.Fill(Strings.SetApplyWarning, string.Join(" ", result.Warnings)));
            }
            else
            {
                ShowMessage(InfoBarSeverity.Success, Strings.SetHttpsApplied);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or CryptographicException)
        {
            ShowError(Strings.SetApplyFailed, ex.Message);
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private async Task TrustCaAsync()
    {
        CaBusy = true;
        try
        {
            await Task.Run(_engine.TrustCertificateAuthority);
            ShowMessage(InfoBarSeverity.Success, Strings.SetCaTrustDone);
        }
        catch (CryptographicException)
        {
            // Refus dans la boîte de dialogue de Windows : pas une erreur.
            ShowMessage(InfoBarSeverity.Warning, Strings.SetCaTrustDeclined);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ShowError(Strings.SetApplyFailed, ex.Message);
        }
        finally
        {
            CaBusy = false;
        }

        await RefreshAsync();
    }

    [RelayCommand]
    private void SavePorts()
    {
        var entries = new (string Name, double? Value)[]
        {
            (Strings.SetPortHttp, HttpPort),
            (Strings.SetPortHttps, HttpsPort),
            (Strings.SetPortMySql, MySqlPort),
            (Strings.SetPortMailpitUi, MailpitUiPort),
            (Strings.SetPortMailpitSmtp, MailpitSmtpPort),
        };

        var ports = new List<int>();
        foreach (var (name, value) in entries)
        {
            if (value is not { } v || double.IsNaN(v) || v != Math.Floor(v) || v < PortRules.MinPort || v > PortRules.MaxPort)
            {
                ShowMessage(InfoBarSeverity.Error, Strings.Fill(Strings.SetPortInvalid, name));
                return;
            }

            ports.Add((int)v);
        }

        if (PortRules.FindDuplicate(ports) is { } duplicate)
        {
            ShowMessage(InfoBarSeverity.Error, Strings.Fill(Strings.SetPortDuplicate, duplicate));
            return;
        }

        if (Save(s => s with
        {
            HttpPort = ports[0],
            HttpsPort = ports[1],
            MySqlPort = ports[2],
            MailpitUiPort = ports[3],
            MailpitSmtpPort = ports[4],
        }))
        {
            ShowMessage(InfoBarSeverity.Success, Strings.SetPortsSaved);
        }
    }

    [RelayCommand]
    private void ChooseRoot()
    {
        if (PickFolder(RootPath) is { } folder && Save(s => s with { Root = folder }))
        {
            RootPath = folder;
            WwwPath = WwwDisplay(_engine.Settings);
        }
    }

    [RelayCommand]
    private void ChooseWww()
    {
        if (PickFolder(WwwPath) is { } folder && Save(s => s with { WwwPath = folder }))
        {
            WwwPath = folder;
        }
    }

    [RelayCommand]
    private void ResetWww()
    {
        if (Save(s => s with { WwwPath = "" }))
        {
            WwwPath = WwwDisplay(_engine.Settings);
        }
    }

    [RelayCommand]
    private void RestartNow() => _shell.RaiseRestart();

    [RelayCommand]
    private void OpenRoot() => Open(RootPath, createDirectory: true);

    [RelayCommand]
    private void OpenLogs() => Open(LogsPath, createDirectory: true);

    [RelayCommand]
    private void OpenGitHub() => Open(GitHubUrl);

    [RelayCommand]
    private void DismissMessage() => IsMessageOpen = false;

    private static string? PickFolder(string initial)
    {
        var dialog = new OpenFolderDialog
        {
            Title = Strings.SetPickFolderTitle,
            InitialDirectory = Directory.Exists(initial) ? initial : "",
        };
        return dialog.ShowDialog() == true ? dialog.FolderName : null;
    }

    private static string WwwDisplay(StackRSettings s) =>
        string.IsNullOrWhiteSpace(s.WwwPath) ? Path.Combine(s.Root, "www") : s.WwwPath;

    private static string ReadVersion()
    {
        var version = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
            ?? "?";
        var plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus > 0 ? version[..plus] : version;
    }

    /// <summary>Enregistre un changement ; vrai si réussi. Met à jour la bannière de redémarrage.</summary>
    private bool Save(Func<StackRSettings, StackRSettings> change)
    {
        try
        {
            _engine.UpdateSettings(change);
            NeedsRestart = _engine.RestartRequired || SelectedLanguage.Id != _initialLanguage;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowError(Strings.SetSaveFailed, ex.Message);
            return false;
        }
    }

    private void RevertWithoutSaving(Action revert)
    {
        _loading = true;
        try
        {
            revert();
        }
        finally
        {
            _loading = false;
        }
    }

    private void Open(string target, bool createDirectory = false)
    {
        try
        {
            if (createDirectory)
            {
                Directory.CreateDirectory(target);
            }

            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Win32Exception)
        {
            ShowMessage(InfoBarSeverity.Error, Strings.Fill(Strings.SetOpenFailed, target, ex.Message));
        }
    }

    private void ShowError(string format, string detail) =>
        ShowMessage(InfoBarSeverity.Error, Strings.Fill(format, detail));

    private void ShowMessage(InfoBarSeverity severity, string text)
    {
        MessageSeverity = severity;
        Message = text;
        IsMessageOpen = true;
    }
}
