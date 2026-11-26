using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Globalization;
using System.Security.Cryptography;
using System.Windows;
using StackR.App.Resources;
using StackR.Core;
using StackR.Core.Manifests;
using StackR.Core.Services;
using StackR.Core.Settings;
using Wpf.Ui.Controls;

namespace StackR.App.Services;

/// <summary>Message destiné à l'utilisateur, affiché par le tableau de bord.</summary>
public sealed record BridgeNotice(InfoBarSeverity Severity, string Message);

/// <summary>
/// Branche la coquille (<see cref="AppShell"/> : zone de notification, fenêtre) sur le moteur au démarrage,
/// indépendamment des pages : tout démarrer / arrêter, sites du menu, reprise des services, démarrage auto.
/// Les pages réutilisent ses opérations (démarrage, arrêt, synchronisation des sites).
/// </summary>
#pragma warning disable CA1001 // Singleton de l'application : vit autant que le processus.
public sealed class EngineBridge
{
    private readonly AppShell _shell;
    private readonly StackREngine _engine;
    private readonly SemaphoreSlim _siteGate = new(1, 1);
    private readonly Lock _stateGate = new();
    private IReadOnlyList<string> _knownHosts = [];
    private CancellationTokenSource? _debounce;
    private int _busy;
    private int _siteSyncPauses;

    public EngineBridge(AppShell shell, StackREngine engine)
    {
        _shell = shell;
        _engine = engine;
    }

    /// <summary>Levé (sur le thread de l'interface) quand une opération a un message à montrer.</summary>
    public event EventHandler<BridgeNotice>? NoticeRaised;

    /// <summary>Levé (sur le thread de l'interface) quand <see cref="IsBusy"/> change.</summary>
    public event EventHandler? BusyChanged;

    /// <summary>Dernier message, pour les pages créées après son émission.</summary>
    public BridgeNotice? LastNotice { get; private set; }

    /// <summary>Un démarrage ou un arrêt est en cours.</summary>
    public bool IsBusy => Volatile.Read(ref _busy) > 0;

    /// <summary>Suspend la synchronisation automatique des sites (création d'un site en cours) jusqu'à la libération.</summary>
    public IDisposable PauseSiteSync()
    {
        Interlocked.Increment(ref _siteSyncPauses);
        return new Resume(this);
    }

    /// <summary>Appelé une fois, sur le thread de l'interface, après l'assistant de premier lancement.</summary>
    public void Initialize()
    {
        _shell.AnyServiceRunning = () => _engine.Services.Services.Any(s => s.Status is ServiceStatus.Running or ServiceStatus.Starting);
        _shell.StartAllRequested += async (_, _) => await RunFromShellAsync(StartAllAsync).ConfigureAwait(true);
        _shell.StopAllRequested += async (_, _) => await RunFromShellAsync(StopAllAsync).ConfigureAwait(true);
        _shell.SiteOpenRequested += (_, host) => OpenSite(host);
        _engine.Sites.Changed += OnSitesChanged;

        _ = InitializeCoreAsync();
    }

    /// <summary>Adresse d'un site : https (ou http si HTTPS est désactivé), avec le port s'il n'est pas standard.</summary>
    public static string BuildUrl(StackRSettings settings, string host)
    {
        var https = settings.HttpsEnabled;
        var port = https ? settings.HttpsPort : settings.HttpPort;
        var standard = https ? 443 : 80;
        var suffix = port == standard ? "" : ":" + port.ToString(CultureInfo.InvariantCulture);
        return (https ? "https://" : "http://") + host + suffix + "/";
    }

    public static ComponentKind ComponentOf(string serviceId) => serviceId switch
    {
        "apache" => ComponentKind.Apache,
        "mysql" => ComponentKind.MySql,
        "mailpit" => ComponentKind.Mailpit,
        _ => throw new ArgumentOutOfRangeException(nameof(serviceId), serviceId, null),
    };

    public bool IsInstalled(ManagedService service) => _engine.ActiveDirectory(ComponentOf(service.Id)) is not null;

    public void OpenSite(string host)
    {
        if (!ShellLauncher.OpenUrl(BuildUrl(_engine.Settings, host)))
        {
            Notify(InfoBarSeverity.Error, Strings.Format(Strings.DashOpenFailed, host));
        }
    }

    /// <summary>Démarre tous les services dont le composant est installé (configuration appliquée avant).</summary>
    public Task StartAllAsync() => StartAsync(_engine.Services.Services);

    /// <summary>Démarre un seul service (mêmes règles que « tout démarrer »).</summary>
    public Task StartServiceAsync(ManagedService service) => StartAsync([service]);

    public async Task StopAllAsync()
    {
        using var busy = EnterBusy();
        try
        {
            await Task.Run(() => _engine.StopAllAsync()).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Notify(InfoBarSeverity.Error, Strings.Format(Strings.DashStopFailed, ex.Message));
        }
    }

    public async Task StopServiceAsync(ManagedService service)
    {
        using var busy = EnterBusy();
        try
        {
            await Task.Run(() => _engine.StopAsync(service.Id)).ConfigureAwait(true);
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Notify(InfoBarSeverity.Error, Strings.Format(Strings.DashStopFailed, ex.Message));
        }
    }

    /// <summary>
    /// Écrit vhosts, certificats et fichier hosts pour les sites actuels (UAC possible pour hosts), puis
    /// redémarre Apache s'il tournait. Sans <paramref name="force"/>, ne fait rien si les noms d'hôtes n'ont pas changé.
    /// </summary>
    /// <returns>Le résultat, ou null si rien n'a été fait.</returns>
    public async Task<ApplyResult?> SyncSitesAsync(bool force)
    {
        await _siteGate.WaitAsync().ConfigureAwait(true);
        try
        {
            var hosts = await Task.Run(CurrentHosts).ConfigureAwait(true);
            bool changed;
            lock (_stateGate)
            {
                changed = !hosts.SequenceEqual(_knownHosts, StringComparer.Ordinal);
                _knownHosts = hosts;
            }

            _shell.SetSites(hosts);
            if (!changed && !force)
            {
                return null;
            }

            var result = await Task.Run(() => _engine.ApplyConfigurationAsync()).ConfigureAwait(true);
            ReportWarnings(result);

            var apache = _engine.Services.Services.FirstOrDefault(s => s.Id == "apache");
            if (apache is { Status: ServiceStatus.Running })
            {
                // Apache ne relit ses vhosts qu'au démarrage.
                await Task.Run(async () =>
                {
                    await _engine.StopAsync(apache.Id).ConfigureAwait(false);
                    await _engine.StartAsync(apache.Id).ConfigureAwait(false);
                }).ConfigureAwait(true);
            }

            return result;
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Notify(InfoBarSeverity.Error, Strings.Format(Strings.DashSyncFailed, ex.Message));
            return null;
        }
        finally
        {
            _siteGate.Release();
        }
    }

    /// <summary>Exceptions attendues des opérations sur le moteur (affichées, jamais propagées).</summary>
    public static bool IsExpected(Exception ex) =>
        ex is IOException or UnauthorizedAccessException or InvalidOperationException or Win32Exception
            or CryptographicException or ArgumentException or NotSupportedException or HttpRequestException
            or TimeoutException;

    private async Task InitializeCoreAsync()
    {
        try
        {
            var hosts = await Task.Run(() =>
            {
                _engine.EnsureReconciled();
                return CurrentHosts();
            }).ConfigureAwait(true);

            lock (_stateGate)
            {
                _knownHosts = hosts;
            }

            _shell.SetSites(hosts);

            var settings = _engine.Settings;
            if (settings.AutoStartServices && settings.FirstRunCompleted)
            {
                await StartAllAsync().ConfigureAwait(true);
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Notify(InfoBarSeverity.Error, Strings.Format(Strings.DashStartFailed, ex.Message));
        }
    }

    private List<string> CurrentHosts() =>
        [.. _engine.Sites.Scan().Select(s => s.Name.Host).Order(StringComparer.Ordinal)];

    private async Task StartAsync(IReadOnlyList<ManagedService> targets)
    {
        using var busy = EnterBusy();
        try
        {
            await Task.Run(() => _engine.EnsureReconciled()).ConfigureAwait(true);

            var installed = targets.Where(IsInstalled).ToList();
            var missing = targets.Except(installed).Select(s => s.DisplayName).ToList();
            if (installed.Count == 0)
            {
                Notify(InfoBarSeverity.Warning, Strings.DashNothingInstalled);
                return;
            }

            if (missing.Count > 0 && targets.Count > 1)
            {
                Notify(
                    InfoBarSeverity.Informational,
                    Strings.Format(Strings.DashSkippedNotInstalled, string.Join(", ", missing)));
            }

            // Le moteur applique la configuration (vhosts, certificats, hosts — UAC si nécessaire) puis démarre,
            // sous sa porte d'opérations, avec la règle « tout ou rien » si un port est occupé (même logique que le CLI).
            var report = await Task.Run(() => installed.Count == 1 && targets.Count == 1
                ? _engine.StartAsync(installed[0].Id)
                : _engine.StartAllAsync()).ConfigureAwait(true);
            if (report.Warnings.Count > 0)
            {
                Notify(InfoBarSeverity.Warning, string.Join(Environment.NewLine, report.Warnings));
            }

            var errors = report.Errors;

            // Les erreurs propres à un service figurent déjà sur sa carte.
            var shown = installed.Select(s => s.LastError).Where(e => e is not null).ToHashSet(StringComparer.Ordinal);
            var rest = errors.Where(e => !shown.Contains(e)).ToList();
            if (rest.Count > 0)
            {
                Notify(InfoBarSeverity.Error, string.Join(Environment.NewLine, rest));
            }
        }
        catch (Exception ex) when (IsExpected(ex))
        {
            Notify(InfoBarSeverity.Error, Strings.Format(Strings.DashStartFailed, ex.Message));
        }
    }

    private async Task RunFromShellAsync(Func<Task> operation)
    {
        try
        {
            await operation().ConfigureAwait(true);
        }
#pragma warning disable CA1031 // Gestionnaire d'événement « async void » : rien ne doit faire tomber l'application.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Notify(InfoBarSeverity.Error, ex.Message);
        }

        if (LastNotice is { Severity: InfoBarSeverity.Error or InfoBarSeverity.Warning })
        {
            // Demande venue de la zone de notification : on montre la fenêtre pour que l'erreur soit lue.
            _shell.RaiseShowWindow();
        }
    }

    private void OnSitesChanged()
    {
        // Appelé depuis le minuteur du scanner : anti-rebond puis synchronisation.
        CancellationTokenSource cts;
        lock (_stateGate)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            cts = _debounce = new CancellationTokenSource();
        }

        _ = DebouncedSyncAsync(cts.Token);
    }

    private async Task DebouncedSyncAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(500, token).ConfigureAwait(false);
            if (Volatile.Read(ref _siteSyncPauses) > 0)
            {
                return;
            }

            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            await dispatcher.InvokeAsync(() => SyncSitesAsync(force: false)).Task.Unwrap();
        }
        catch (OperationCanceledException)
        {
            // Remplacé par un changement plus récent.
        }
#pragma warning disable CA1031 // Tâche lancée sans attente : rien ne doit faire tomber l'application.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            Notify(InfoBarSeverity.Error, Strings.Format(Strings.DashSyncFailed, ex.Message));
        }
    }

    private void ReportWarnings(ApplyResult result)
    {
        if (result.Warnings.Count > 0)
        {
            Notify(InfoBarSeverity.Warning, string.Join(Environment.NewLine, result.Warnings));
        }
    }

    private void Notify(InfoBarSeverity severity, string message)
    {
        var notice = new BridgeNotice(severity, message);
        OnUi(() =>
        {
            LastNotice = notice;
            NoticeRaised?.Invoke(this, notice);
        });
    }

    private static void OnUi(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.BeginInvoke(action);
        }
    }

    private BusyScope EnterBusy()
    {
        Interlocked.Increment(ref _busy);
        OnUi(() => BusyChanged?.Invoke(this, EventArgs.Empty));
        return new BusyScope(this);
    }

    private sealed class BusyScope(EngineBridge owner) : IDisposable
    {
        public void Dispose()
        {
            Interlocked.Decrement(ref owner._busy);
            OnUi(() => owner.BusyChanged?.Invoke(owner, EventArgs.Empty));
        }
    }

    private sealed class Resume(EngineBridge owner) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref owner._siteSyncPauses);
            }
        }
    }
}
