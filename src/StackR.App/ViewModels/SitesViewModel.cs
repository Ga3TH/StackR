using StackR.Core.Settings;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using StackR.App.Services;
using StackR.App.Views;
using StackR.Core;
using Wpf.Ui.Controls;

namespace StackR.App.ViewModels;

/// <summary>Un site de la liste (nom, adresse, racine web, dossier du projet).</summary>
public sealed record SiteItem(string Name, string Host, string Url, string DocumentRoot, string Folder);

/// <summary>Modèle de vue de la page « Sites » : liste, actions et création d'un site.</summary>
public sealed partial class SitesViewModel : PageViewModelBase
{
    private readonly StackREngine _engine;
    private readonly EngineBridge _bridge;
    private int _refreshVersion;

    [ObservableProperty]
    private bool _hasVsCode;

    [ObservableProperty]
    private bool _isNoticeOpen;

    [ObservableProperty]
    private string _noticeMessage = "";

    [ObservableProperty]
    private InfoBarSeverity _noticeSeverity = InfoBarSeverity.Error;

    public SitesViewModel(StackREngine engine, EngineBridge bridge)
        : base(Strings.NavSites, Strings.SitesSubtitle)
    {
        _engine = engine;
        _bridge = bridge;

        // Le scanner lève Changed depuis un minuteur : retour au thread de l'interface.
        engine.Sites.Changed += () => Application.Current?.Dispatcher.BeginInvoke(() => _ = RefreshAsync());
        engine.SettingsChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => _ = RefreshAsync());
        _ = RefreshAsync();
    }

    public ObservableCollection<SiteItem> Items { get; } = [];

    public ObservableCollection<string> Skipped { get; } = [];

    public bool HasItems => Items.Count > 0;

    public bool HasSkipped => Skipped.Count > 0;

    public string WwwPath => _engine.Settings.EffectiveWww(_engine.Paths);

    /// <summary>Relit les sites (dossier www) sans bloquer l'interface.</summary>
    [RelayCommand]
    public async Task RefreshAsync()
    {
        var version = ++_refreshVersion;
        try
        {
            var (sites, skipped, vsCode) = await Task.Run(() =>
            {
                var scanned = _engine.Sites.Scan();
                return (scanned, _engine.Sites.Skipped, ShellLauncher.FindVsCode() is not null);
            });

            if (version != _refreshVersion)
            {
                return; // Une lecture plus récente est en cours.
            }

            var settings = _engine.Settings;
            Items.Clear();
            foreach (var site in sites.OrderBy(s => s.Name.Value, StringComparer.Ordinal))
            {
                Items.Add(new SiteItem(
                    site.Name.Value, site.Name.Host, EngineBridge.BuildUrl(settings, site.Name.Host), site.DocumentRoot, site.FolderPath));
            }

            Skipped.Clear();
            foreach (var entry in skipped)
            {
                Skipped.Add(entry);
            }

            HasVsCode = vsCode;
            OnPropertyChanged(nameof(HasItems));
            OnPropertyChanged(nameof(HasSkipped));
            OnPropertyChanged(nameof(WwwPath));
        }
        catch (Exception ex) when (EngineBridge.IsExpected(ex))
        {
            ShowNotice(InfoBarSeverity.Error, Strings.Format(Strings.SitesLoadFailed, ex.Message));
        }
    }

    [RelayCommand]
    private void OpenSite(SiteItem? site)
    {
        if (site is not null)
        {
            _bridge.OpenSite(site.Host);
        }
    }

    [RelayCommand]
    private void OpenFolder(SiteItem? site)
    {
        if (site is not null && !ShellLauncher.OpenFolder(site.Folder))
        {
            ShowNotice(InfoBarSeverity.Error, Strings.Format(Strings.DashFolderFailed, site.Folder));
        }
    }

    [RelayCommand]
    private void OpenWww()
    {
        if (!ShellLauncher.OpenFolder(WwwPath))
        {
            ShowNotice(InfoBarSeverity.Error, Strings.Format(Strings.DashFolderFailed, WwwPath));
        }
    }

    [RelayCommand]
    private void OpenVsCode(SiteItem? site)
    {
        if (site is not null && !ShellLauncher.OpenVsCode(site.Folder))
        {
            ShowNotice(InfoBarSeverity.Error, Strings.SitesVsCodeFailed);
        }
    }

    [RelayCommand]
    private async Task NewSiteAsync()
    {
        IsNoticeOpen = false;
        var viewModel = new NewSiteViewModel(_engine, _bridge);
        var dialog = new NewSiteDialog(viewModel) { Owner = Application.Current?.MainWindow };
        dialog.ShowDialog();
        await RefreshAsync();
    }

    private void ShowNotice(InfoBarSeverity severity, string message)
    {
        NoticeSeverity = severity;
        NoticeMessage = message;
        IsNoticeOpen = true;
    }
}
