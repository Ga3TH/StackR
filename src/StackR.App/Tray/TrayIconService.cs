using System.IO;
using System.Windows.Controls;
using H.NotifyIcon;
using H.NotifyIcon.Core;
using StackR.App.Resources;
using StackR.App.Services;
using Wpf.Ui.Controls;
using MenuItem = System.Windows.Controls.MenuItem;

namespace StackR.App.Tray;

/// <summary>
/// Icône de la zone de notification (H.NotifyIcon.Wpf : WPF-UI 4.x n'embarque pas de NotifyIcon).
/// Les actions sont relayées vers <see cref="AppShell"/>.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly AppShell _shell;
    private TaskbarIcon? _icon;
    private MenuItem? _sitesMenu;
    private bool _backgroundHintShown;

    public TrayIconService(AppShell shell)
    {
        _shell = shell;
    }

    public void Initialize()
    {
        if (_icon is not null)
        {
            return;
        }

        var icon = LoadIcon();

        _icon = new TaskbarIcon
        {
            Icon = icon,
            ToolTipText = Strings.TrayTooltip,
            NoLeftClickDelay = true,
            ContextMenu = BuildMenu(),
        };
        _icon.TrayLeftMouseUp += (_, _) => _shell.RaiseShowWindow();
        _icon.ForceCreate(enablesEfficiencyMode: false);

        _shell.SitesChanged += (_, _) => RebuildSites();
        _shell.HiddenToTray += (_, _) => ShowBackgroundHint();
    }

    /// <summary>
    /// Icône de l'application : ressource intégrée (adresse complète avec l'assembly, qui ne dépend pas de l'assembly
    /// d'entrée), sinon icône de l'exécutable. Une icône introuvable ne doit jamais empêcher le démarrage.
    /// </summary>
    private static System.Drawing.Icon? LoadIcon()
    {
        try
        {
            var info = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/StackR.App;component/Assets/stackr.ico"));
            if (info is not null)
            {
                using var stream = info.Stream;
                return new System.Drawing.Icon(stream);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            CrashLog.Write("Avertissement : icône de la zone de notification (ressource)", ex);
        }

        try
        {
            if (Environment.ProcessPath is { } exe)
            {
                return System.Drawing.Icon.ExtractAssociatedIcon(exe);
            }
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or UnauthorizedAccessException)
        {
            CrashLog.Write("Avertissement : icône de la zone de notification (exécutable)", ex);
        }

        return null;
    }

    public void Dispose()
    {
        _icon?.Dispose();
        _icon = null;
    }

    private ContextMenu BuildMenu()
    {
        var menu = new ContextMenu();
        menu.Items.Add(Item(Strings.TrayStartAll, SymbolRegular.Play24, _shell.RaiseStartAll));
        menu.Items.Add(Item(Strings.TrayStopAll, SymbolRegular.Stop24, _shell.RaiseStopAll));
        menu.Items.Add(new Separator());

        _sitesMenu = new MenuItem { Header = Strings.TraySites, Icon = new SymbolIcon(SymbolRegular.Globe24) };
        RebuildSites();
        menu.Items.Add(_sitesMenu);
        menu.Items.Add(new Separator());

        menu.Items.Add(Item(Strings.TrayOpen, SymbolRegular.Open24, _shell.RaiseShowWindow));
        menu.Items.Add(Item(Strings.TrayQuit, SymbolRegular.Power24, _shell.RaiseQuit));
        return menu;
    }

    private void RebuildSites()
    {
        if (_sitesMenu is null)
        {
            return;
        }

        _sitesMenu.Items.Clear();
        if (_shell.Sites.Count == 0)
        {
            _sitesMenu.Items.Add(new MenuItem { Header = Strings.TrayNoSites, IsEnabled = false });
            return;
        }

        foreach (var site in _shell.Sites)
        {
            _sitesMenu.Items.Add(Item(site, SymbolRegular.Open24, () => _shell.RaiseOpenSite(site)));
        }
    }

    private static MenuItem Item(string header, SymbolRegular symbol, Action action)
    {
        var item = new MenuItem { Header = header, Icon = new SymbolIcon(symbol) };
        item.Click += (_, _) => action();
        return item;
    }

    /// <summary>Première fermeture de la fenêtre : bulle « StackR continue en arrière-plan ».</summary>
    private void ShowBackgroundHint()
    {
        if (_backgroundHintShown || _icon is null)
        {
            return;
        }

        _backgroundHintShown = true;
        _icon.ShowNotification(Strings.TrayBackgroundTitle, Strings.TrayBackgroundMessage, NotificationIcon.Info);
    }
}
