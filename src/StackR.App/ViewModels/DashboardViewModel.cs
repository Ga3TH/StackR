using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using StackR.App.Services;
using StackR.Core;
using StackR.Core.Manifests;
using StackR.Core.Services;
using StackR.Core.Settings;
using StackR.Core.Sites;
using Wpf.Ui.Controls;

namespace StackR.App.ViewModels;

/// <summary>Un site de la liste rapide du tableau de bord.</summary>
public sealed record DashboardSite(string Name, string Host, string Framework);

/// <summary>Modèle de vue du tableau de bord : cartes de services, PHP actif, raccourcis, sites récents.</summary>
public sealed partial class DashboardViewModel : PageViewModelBase, IRefreshable
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

    [ObservableProperty]
    private string _summary = "";

    [ObservableProperty]
    private bool _hasPortTip;

    [ObservableProperty]
    private string _portTipTitle = "";

    [ObservableProperty]
    private string _portTipBody = "";

    public DashboardViewModel(StackREngine engine, EngineBridge bridge, DatabaseViewModel database)
        : base(Strings.NavDashboard, Strings.DashboardSubtitle)
    {
        _engine = engine;
        _bridge = bridge;
        Database = database;

        foreach (var service in engine.Services.Services.OrderBy(s => Array.IndexOf(DisplayOrder, s.Id)))
        {
            var card = new ServiceCardViewModel(service, engine, bridge, () => NavigateToVersionsRequested?.Invoke(this, EventArgs.Empty))
            {
                OpenLogsCommand = new RelayCommand(() => NavigateRequested?.Invoke(this, "logs")),
            };
            card.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ServiceCardViewModel.Status))
                {
                    UpdateSummary();
                }
            };
            Cards.Add(card);
        }

        bridge.NoticeRaised += (_, notice) => ShowNotice(notice);
        bridge.ComponentsInstalled += (_, _) => Refresh();
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

    public ObservableCollection<DashboardSite> RecentSites { get; } = [];

    /// <summary>Page Base de données (HeidiSQL, Adminer depuis les outils rapides).</summary>
    public DatabaseViewModel Database { get; }

    /// <summary>« Bonjour » le jour, « Bonsoir » à partir de 18 h.</summary>
#pragma warning disable CA1822 // Lié en XAML (propriété d'instance notifiée).
    public string Greeting => DateTime.Now.Hour is >= 18 or < 5 ? Strings.UiHelloEvening : Strings.UiHelloMorning;
#pragma warning restore CA1822

    /// <summary>Demande de navigation vers une page (« sites », « logs »…) ; la page navigue.</summary>
    public event EventHandler<string>? NavigateRequested;

    private int _siteCount;

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
        OnPropertyChanged(nameof(Greeting));
        UpdateSummary();
        _ = RefreshSitesAsync();
        _ = RefreshPortTipAsync();
    }

    /// <summary>Relit mémoire et durée de fonctionnement des services (minuteur de la page).</summary>
    public async Task RefreshMetricsAsync()
    {
        foreach (var card in Cards)
        {
            await card.RefreshMetricsAsync();
        }
    }

    private void UpdateSummary()
    {
        var running = Cards.Count(c => c.IsRunning);
        var php = SelectedPhp is { } v ? "PHP " + v : Strings.UiSummaryNoPhp;
        Summary = string.Join("  ·  ", Strings.Format(Strings.UiSummaryServices, running, Cards.Count), Strings.Format(Strings.UiSummarySites, _siteCount), php);
    }

    /// <summary>Conseil si le port HTTP est pris par un autre programme (souvent Laragon) alors qu'Apache est arrêté.</summary>
    /// <summary>Outil de captures : pas de sonde de port réelle.</summary>
    internal bool IsPreview { get; set; }

    private async Task RefreshPortTipAsync()
    {
        if (IsPreview)
        {
            return;
        }

        var apache = _engine.Services.Services.FirstOrDefault(s => s.Id == "apache");
        if (apache is null || apache.Status is ServiceStatus.Running or ServiceStatus.Starting or ServiceStatus.Stopping)
        {
            HasPortTip = false;
            return;
        }

        var port = _engine.EffectiveSettings.HttpPort;
        var owner = await Task.Run(() => PortProbe.FindOwner(port));
        ShowPortTip(port, owner);
    }

    /// <summary>Affiche (ou masque, sans propriétaire) le conseil « port déjà utilisé ».</summary>
    internal void ShowPortTip(int port, PortOwner? owner)
    {
        HasPortTip = owner is not null;
        if (owner is not null)
        {
            PortTipTitle = Strings.Format(Strings.UiTipPortTitle, port);
            PortTipBody = Strings.Format(Strings.UiTipPortBody, port, owner.ProcessName, owner.Pid);
        }
    }

    internal static string FrameworkName(SiteFramework framework) => framework switch
    {
        SiteFramework.Laravel => Strings.UiFwLaravel,
        SiteFramework.Symfony => Strings.UiFwSymfony,
        SiteFramework.WordPress => Strings.UiFwWordPress,
        SiteFramework.Static => Strings.UiFwStatic,
        _ => Strings.UiFwPhp,
    };

    [RelayCommand]
    private void GoTo(string page) => NavigateRequested?.Invoke(this, page);

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
            var (sites, count) = await Task.Run(() =>
            {
                var all = _engine.Sites.Scan();
                var recent = all
                    .Select(s => (Site: s, Details: SiteInspector.Inspect(s.FolderPath)))
                    .OrderByDescending(x => x.Details.LastModified ?? DateTimeOffset.MinValue)
                    .Take(RecentSitesCount)
                    .Select(x => new DashboardSite(x.Site.Name.Value, x.Site.Name.Host, FrameworkName(x.Details.Framework)))
                    .ToList();
                return (recent, all.Count);
            });
            RecentSites.Clear();
            foreach (var site in sites)
            {
                RecentSites.Add(site);
            }

            _siteCount = count;
            UpdateSummary();

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

    /// <inheritdoc />
    public Task RefreshAsync()
    {
        Refresh();
        return RefreshMetricsAsync();
    }

    private void ShowNotice(BridgeNotice notice)
    {
        if (notice.Severity == InfoBarSeverity.Success && Toasts.Success(notice.Message))
        {
            return;
        }

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
