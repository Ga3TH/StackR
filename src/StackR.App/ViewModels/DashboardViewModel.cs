using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using StackR.App.Services;
using StackR.Core;
using StackR.Core.Manifests;
using StackR.Core.Settings;
using Wpf.Ui.Controls;

namespace StackR.App.ViewModels;

/// <summary>Modèle de vue du tableau de bord : cartes de services, PHP actif, raccourcis, sites récents.</summary>
public sealed partial class DashboardViewModel : PageViewModelBase
{
    private const int RecentSitesCount = 6;

    private static readonly string[] DisplayOrder = ["apache", "mysql", "mailpit"];

    private readonly StackREngine _engine;
    private readonly EngineBridge _bridge;
    private bool _refreshing;

    [ObservableProperty]
    private string? _selectedPhp;

    [ObservableProperty]
    private bool _hasPhp;

    [ObservableProperty]
    private bool _isNoticeOpen;

    [ObservableProperty]
    private string _noticeMessage = "";

    [ObservableProperty]
    private InfoBarSeverity _noticeSeverity = InfoBarSeverity.Informational;

    [ObservableProperty]
    private bool _isPhpBusy;

    public DashboardViewModel(StackREngine engine, EngineBridge bridge)
        : base(Strings.NavDashboard, Strings.DashboardSubtitle)
    {
        _engine = engine;
        _bridge = bridge;

        foreach (var service in engine.Services.Services.OrderBy(s => Array.IndexOf(DisplayOrder, s.Id)))
        {
            Cards.Add(new ServiceCardViewModel(service, engine, bridge, () => NavigateToVersionsRequested?.Invoke(this, EventArgs.Empty)));
        }

        bridge.NoticeRaised += (_, notice) => ShowNotice(notice);
        bridge.BusyChanged += (_, _) =>
        {
            StartAllCommand.NotifyCanExecuteChanged();
            StopAllCommand.NotifyCanExecuteChanged();
        };
        engine.SettingsChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(Refresh);
        engine.Sites.Changed += () => Application.Current?.Dispatcher.BeginInvoke(() => _ = RefreshSitesAsync());

        if (bridge.LastNotice is { } last)
        {
            ShowNotice(last);
        }

        Refresh();
    }

    /// <summary>Levé quand une carte demande la page « Versions » (la page navigue).</summary>
    public event EventHandler? NavigateToVersionsRequested;

    public ObservableCollection<ServiceCardViewModel> Cards { get; } = [];

    public ObservableCollection<string> PhpVersions { get; } = [];

    public ObservableCollection<string> RecentSites { get; } = [];

    public bool HasRecentSites => RecentSites.Count > 0;

    public string WwwPath => _engine.Settings.EffectiveWww(_engine.Paths);

    public string MailpitUrl => string.Create(CultureInfo.InvariantCulture, $"http://127.0.0.1:{_engine.Settings.MailpitUiPort}");

    /// <summary>Relit versions, composants et sites (à l'affichage de la page et après un changement de réglages).</summary>
    public void Refresh()
    {
        foreach (var card in Cards)
        {
            card.Refresh();
        }

        _refreshing = true;
        try
        {
            var installed = _engine.Store.Installed(ComponentKind.Php);
            PhpVersions.Clear();
            foreach (var version in installed)
            {
                PhpVersions.Add(version);
            }

            var active = _engine.ActiveDirectory(ComponentKind.Php) is null ? null : _engine.ActiveVersion(ComponentKind.Php);
            SelectedPhp = active is not null && installed.Contains(active) ? active : null;
            HasPhp = installed.Count > 0;
        }
        finally
        {
            _refreshing = false;
        }

        OnPropertyChanged(nameof(WwwPath));
        OnPropertyChanged(nameof(MailpitUrl));
        _ = RefreshSitesAsync();
    }

    partial void OnSelectedPhpChanged(string? value)
    {
        if (!_refreshing && value is not null)
        {
            _ = UsePhpAsync(value);
        }
    }

    private async Task RefreshSitesAsync()
    {
        try
        {
            var hosts = await Task.Run(() => _engine.Sites.Scan().Select(s => s.Name.Host).Order(StringComparer.Ordinal).Take(RecentSitesCount).ToList());
            RecentSites.Clear();
            foreach (var host in hosts)
            {
                RecentSites.Add(host);
            }

            OnPropertyChanged(nameof(HasRecentSites));
        }
        catch (Exception ex) when (EngineBridge.IsExpected(ex))
        {
            // Dossier www illisible : la liste reste telle quelle.
        }
    }

    private async Task UsePhpAsync(string version)
    {
        IsPhpBusy = true;
        try
        {
            var result = await Task.Run(() => _engine.UsePhpAsync(version));
            ShowNotice(result.Warnings.Count > 0
                ? new BridgeNotice(InfoBarSeverity.Warning, string.Join(Environment.NewLine, result.Warnings))
                : new BridgeNotice(InfoBarSeverity.Success, Strings.Format(Strings.DashPhpActivated, version)));
        }
        catch (Exception ex) when (EngineBridge.IsExpected(ex))
        {
            ShowNotice(new BridgeNotice(InfoBarSeverity.Error, Strings.Format(Strings.DashPhpFailed, version, ex.Message)));
        }
        finally
        {
            IsPhpBusy = false;
            Refresh();
        }
    }

    private void ShowNotice(BridgeNotice notice)
    {
        NoticeSeverity = notice.Severity;
        NoticeMessage = notice.Message;
        IsNoticeOpen = true;
    }

    private bool CanChangeAll() => !_bridge.IsBusy;

    [RelayCommand(CanExecute = nameof(CanChangeAll))]
    private async Task StartAllAsync()
    {
        IsNoticeOpen = false;
        await _bridge.StartAllAsync();
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanChangeAll))]
    private async Task StopAllAsync()
    {
        IsNoticeOpen = false;
        await _bridge.StopAllAsync();
        Refresh();
    }

    [RelayCommand]
    private void OpenTerminal()
    {
        if (!ShellLauncher.OpenTerminal(_engine.Paths, WwwPath))
        {
            ShowNotice(new BridgeNotice(InfoBarSeverity.Error, Strings.DashTerminalFailed));
        }
    }

    [RelayCommand]
    private void OpenWww()
    {
        if (!ShellLauncher.OpenFolder(WwwPath))
        {
            ShowNotice(new BridgeNotice(InfoBarSeverity.Error, Strings.Format(Strings.DashFolderFailed, WwwPath)));
        }
    }

    [RelayCommand]
    private void OpenMailpit()
    {
        if (!ShellLauncher.OpenUrl(MailpitUrl))
        {
            ShowNotice(new BridgeNotice(InfoBarSeverity.Error, Strings.Format(Strings.DashOpenFailed, MailpitUrl)));
        }
    }

    [RelayCommand]
    private void OpenSite(string host) => _bridge.OpenSite(host);
}
