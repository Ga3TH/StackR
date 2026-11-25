using System.Globalization;
using System.Text.Json;
using StackR.Core.Components;
using StackR.Core.Manifests;
using StackR.Core.Paths;
using StackR.Core.Security;
using StackR.Core.Settings;

namespace StackR.Core.Services;

/// <summary>Résultat de <see cref="ServiceManager.StartAllAsync"/>.</summary>
public sealed record StartReport(bool Success, IReadOnlyList<string> Errors);

/// <summary>
/// Pilote l'ensemble des services et tient à jour <c>tmp\state.json</c> (<c>{id: pid}</c>) pour
/// retrouver, après un redémarrage de l'app, les processus qui sont réellement les nôtres.
/// </summary>
public sealed class ServiceManager
{
    private readonly StackRPaths _paths;
    private readonly IProcessRunner _runner;
    private readonly List<ManagedService> _services;
    private readonly Lock _stateGate = new();
    private bool _reconciled;

    public ServiceManager(StackRPaths paths, IProcessRunner runner, IEnumerable<ManagedService> services)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        ArgumentNullException.ThrowIfNull(services);
        _services = services.ToList();
        foreach (var service in _services)
        {
            service.StatusChanged += (_, _) => TryWriteState();
        }
    }

    public IReadOnlyList<ManagedService> Services => _services;

    /// <summary>
    /// Services par défaut (MySQL, Apache, Mailpit) pointant vers les versions actives installées.
    /// </summary>
    public static ServiceManager CreateDefault(
        StackRPaths paths, StackRSettings settings, ComponentStore store, SecretStore secrets, IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(store);

        Func<string?> ActiveDir(ComponentKind kind) => () =>
        {
            var folder = ComponentStore.FolderName(kind);
            if (!settings.ActiveVersions.TryGetValue(folder, out var version))
            {
                return null;
            }

            try
            {
                return store.IsInstalled(kind, version) ? paths.ComponentDir(folder, version) : null;
            }
            catch (ArgumentException)
            {
                return null;
            }
        };

        return new ServiceManager(paths, runner,
        [
            new MySqlService(paths, settings, runner, ActiveDir(ComponentKind.MySql), secrets),
            new ApacheService(paths, settings, runner, ActiveDir(ComponentKind.Apache), ActiveDir(ComponentKind.Php)),
            new MailpitService(paths, settings, runner, ActiveDir(ComponentKind.Mailpit)),
        ]);
    }

    /// <summary>
    /// Vérifie d'abord TOUS les ports : si l'un est pris, rien n'est démarré.
    /// Démarre ensuite chaque service arrêté.
    /// </summary>
    public async Task<StartReport> StartAllAsync(CancellationToken cancellationToken)
    {
        if (!_reconciled)
        {
            Reconcile();
        }

        var toStart = _services
            .Where(s => s.Status is not (ServiceStatus.Running or ServiceStatus.Starting or ServiceStatus.Stopping))
            .ToList();

        var errors = new List<string>();
        foreach (var group in _services.SelectMany(s => s.Ports.Distinct().Select(p => (Port: p, Service: s))).GroupBy(x => x.Port))
        {
            if (group.Count() > 1)
            {
                errors.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"Port {group.Key} attribué à plusieurs services ({string.Join(", ", group.Select(x => x.Service.DisplayName))})"));
            }
        }

        foreach (var port in toStart.SelectMany(s => s.Ports).Distinct())
        {
            if (PortProbe.DescribeConflict(port) is { } conflict)
            {
                errors.Add(conflict);
            }
        }

        if (errors.Count > 0)
        {
            return new StartReport(false, errors);
        }

        foreach (var service in toStart)
        {
            await service.StartAsync(cancellationToken).ConfigureAwait(false);
            if (service.Status == ServiceStatus.Error && service.LastError is { } error)
            {
                errors.Add(error);
            }
        }

        return new StartReport(errors.Count == 0, errors);
    }

    public async Task StopAllAsync(CancellationToken cancellationToken)
    {
        // Ordre inverse du démarrage ; seuls nos processus suivis (lancés ou vérifiés) sont arrêtés.
        for (var i = _services.Count - 1; i >= 0; i--)
        {
            await _services[i].StopAsync(cancellationToken).ConfigureAwait(false);
        }

        TryWriteState();
    }

    /// <summary>
    /// Relit <c>state.json</c> : un PID n'est repris que si son exécutable est celui du service sous
    /// <c>bin\</c>. Les autres entrées (processus disparu, PID recyclé, accès refusé) sont abandonnées,
    /// sans jamais toucher au processus.
    /// </summary>
    public void Reconcile()
    {
        var state = ReadState();
        foreach (var service in _services)
        {
            if (service.ProcessId is not null || !state.TryGetValue(service.Id, out var pid))
            {
                continue;
            }

            var process = _runner.TryGet(pid);
            if (process is null)
            {
                continue;
            }

            if (!ProcessIdentity.IsOurs(process, _paths.Bin, service.ExecutableName) || !service.TryAdopt(process))
            {
                process.Dispose();
            }
        }

        _reconciled = true;
        TryWriteState();
    }

    private Dictionary<string, int> ReadState()
    {
        try
        {
            return File.Exists(_paths.StateFile)
                ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(_paths.StateFile)) ?? []
                : [];
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    // Appelé depuis les événements des services (y compris le thread de sortie d'un processus) :
    // une erreur d'écriture ne doit jamais remonter.
    private void TryWriteState()
    {
        try
        {
            WriteState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // state.json sera réécrit au prochain changement d'état.
        }
    }

    private void WriteState()
    {
        lock (_stateGate)
        {
            var state = new Dictionary<string, int>();

            // Avant la réconciliation, on conserve les entrées non encore examinées.
            if (!_reconciled)
            {
                foreach (var (id, pid) in ReadState())
                {
                    if (_services.Any(s => s.Id == id && s.ProcessId is null))
                    {
                        state[id] = pid;
                    }
                }
            }

            foreach (var service in _services)
            {
                if (service.ProcessId is int pid)
                {
                    state[service.Id] = pid;
                }
            }

            Directory.CreateDirectory(_paths.Tmp);
            var tmp = _paths.StateFile + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(state));
                File.Move(tmp, _paths.StateFile, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmp))
                {
                    File.Delete(tmp);
                }
            }
        }
    }
}
