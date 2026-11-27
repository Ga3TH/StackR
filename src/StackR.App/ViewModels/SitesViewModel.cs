using StackR.Core.Settings;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using StackR.App.Services;
using StackR.App.Views;
using System.IO;
using StackR.Core;
using StackR.Core.Formatting;
using StackR.Core.Manifests;
using StackR.Core.Sites;
using Wpf.Ui.Controls;

namespace StackR.App.ViewModels;

/// <summary>Un site de la liste (nom, adresse, racine web, dossier du projet, détails).</summary>
public sealed record SiteItem(string Name, string Host, string Url, string DocumentRoot, string Folder)
{
    public string Framework { get; init; } = "";

    public string ModifiedText { get; init; } = "";

    public string PhpText { get; init; } = "";

    public bool IsHttps { get; init; }

    public bool HasGit { get; init; }

    public bool HasComposer { get; init; }

    public bool HasNode { get; init; }

    public string RelativeRoot { get; init; } = "";
}

/// <summary>Modèle de vue de la page « Sites » : liste, actions et création d'un site.</summary>
public sealed partial class SitesViewModel : PageViewModelBase, IRefreshable
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

    private List<SiteItem> _all = [];

    [ObservableProperty]
    private string _search = "";

    [ObservableProperty]
    private string _countText = "";

    [ObservableProperty]
    private bool _hasNoMatch;

    [ObservableProperty]
    private string _noMatchText = "";

    partial void OnSearchChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var term = Search.Trim();
        Items.Clear();
        foreach (var item in _all.Where(i => term.Length == 0
                     || i.Name.Contains(term, StringComparison.OrdinalIgnoreCase)
                     || i.Framework.Contains(term, StringComparison.OrdinalIgnoreCase)))
        {
            Items.Add(item);
        }

        HasNoMatch = _all.Count > 0 && Items.Count == 0;
        NoMatchText = Strings.Format(Strings.UiSitesNoMatch, term);
        CountText = Strings.Format(Strings.UiSitesCount, _all.Count, WwwPath);
        OnPropertyChanged(nameof(HasItems));
    }

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
                var scanned = _engine.Sites.Scan().Select(s => (Site: s, Details: SiteInspector.Inspect(s.FolderPath))).ToList();
                return (scanned, _engine.Sites.Skipped, ShellLauncher.FindVsCode() is not null);
            });

            if (version != _refreshVersion)
            {
                return; // Une lecture plus récente est en cours.
            }

            var settings = _engine.Settings;
            var php = _engine.ActiveVersion(ComponentKind.Php);
            var now = DateTimeOffset.Now;
            var culture = CultureInfo.CurrentUICulture;
            _all = [.. sites.OrderBy(s => s.Site.Name.Value, StringComparer.Ordinal).Select(x => new SiteItem(
                x.Site.Name.Value, x.Site.Name.Host, EngineBridge.BuildUrl(settings, x.Site.Name.Host), x.Site.DocumentRoot, x.Site.FolderPath)
            {
                Framework = DashboardViewModel.FrameworkName(x.Details.Framework),
                ModifiedText = x.Details.LastModified is { } m ? Strings.Format(Strings.UiModified, HumanFormat.Ago(m, now, culture)) : "",
                PhpText = php is null ? "" : "PHP " + php,
                IsHttps = settings.HttpsEnabled,
                HasGit = x.Details.HasGit,
                HasComposer = x.Details.HasComposer,
                HasNode = x.Details.HasNode,
                RelativeRoot = RelativeTo(x.Site.FolderPath, x.Site.DocumentRoot),
            })];
            ApplyFilter();

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

    private static string RelativeTo(string folder, string root)
    {
        var relative = System.IO.Path.GetRelativePath(folder, root);
        return relative == "." ? "/" : "/" + relative.Replace(Path.DirectorySeparatorChar, '/') + "/";
    }

    [RelayCommand]
    private void OpenTerminal(SiteItem? site)
    {
        if (site is not null && !ShellLauncher.OpenTerminal(_engine.Paths, site.Folder))
        {
            ShowNotice(InfoBarSeverity.Error, Strings.DashTerminalFailed);
        }
    }

    [RelayCommand]
    private async Task RemoveAsync(SiteItem? site)
    {
        if (site is null)
        {
            return;
        }

        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = Strings.Format(Strings.UiRemoveTitle, site.Name),
            Content = Strings.Format(Strings.UiRemoveBody, site.Folder),
            PrimaryButtonText = Strings.UiRemoveConfirm,
            CloseButtonText = Strings.UiCancel,
            PrimaryButtonAppearance = ControlAppearance.Danger,
        };
        if (await dialog.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        var www = WwwPath;
        try
        {
            await Task.Run(() => SiteRemoval.MoveToRecycleBin(www, site.Folder));
            if (!Toasts.Success(Strings.Format(Strings.UiRemoved, site.Name)))
            {
                ShowNotice(InfoBarSeverity.Success, Strings.Format(Strings.UiRemoved, site.Name));
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            ShowNotice(InfoBarSeverity.Error, Strings.Format(Strings.UiRemoveFailed, ex.Message));
        }

        await RefreshAsync();
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
