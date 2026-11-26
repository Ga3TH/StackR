using System.Globalization;
using System.Text;
using System.Text.Json;
using StackR.Core.Components;
using StackR.Core.IO;
using StackR.Core.Manifests;
using StackR.Core.Paths;
using StackR.Core.Security;
using StackR.Core.Settings;

namespace StackR.Core.Services;

/// <summary>Résultat de <see cref="ServiceManager.StartAllAsync"/> et des démarrages du moteur.</summary>
/// <param name="Success">Tous les services visés et installés tournent.</param>
/// <param name="Errors">Ports occupés ou en double, échecs de démarrage (en français).</param>
public sealed record StartReport(bool Success, IReadOnlyList<string> Errors)
{
    /// <summary>Avertissements non bloquants de l'application de la configuration (moteur).</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Identifiants des services visés mais ignorés parce que leur composant n'est pas installé (moteur).</summary>
    public IReadOnlyList<string> NotInstalled { get; init; } = [];
}

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

        string? ActiveDir(ComponentKind kind)
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
        }

        return CreateDefault(paths, settings, () => settings, ActiveDir, secrets, runner);
    }

    /// <summary>
    /// Variante du moteur : paramètres relus à chaque usage (<paramref name="current"/>) et dossier
    /// de la version active fourni par <paramref name="activeDirectory"/> (lecture sous verrou).
    /// </summary>
    internal static ServiceManager CreateDefault(
        StackRPaths paths,
        StackRSettings initial,
        Func<StackRSettings> current,
        Func<ComponentKind, string?> activeDirectory,
        SecretStore secrets,
        IProcessRunner runner)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(activeDirectory);
        return new ServiceManager(paths, runner,
        [
            new MySqlService(paths, initial, runner, () => activeDirectory(ComponentKind.MySql), secrets) { SettingsProvider = current },
            new ApacheService(paths, initial, runner, () => activeDirectory(ComponentKind.Apache), () => activeDirectory(ComponentKind.Php))
            {
                SettingsProvider = current,
            },
            new MailpitService(paths, initial, runner, () => activeDirectory(ComponentKind.Mailpit)) { SettingsProvider = current },
        ]);
    }

    /// <summary>
    /// Vérifie d'abord TOUS les ports : si l'un est pris, rien n'est démarré.
    /// Démarre ensuite chaque service arrêté.
    /// </summary>
    public Task<StartReport> StartAllAsync(CancellationToken cancellationToken) => StartServicesAsync(_services, cancellationToken);

    /// <summary>
    /// Même règle « tout ou rien » que <see cref="StartAllAsync"/>, limitée à <paramref name="targets"/> :
    /// ports en double (parmi les cibles et les services déjà actifs) ou occupés → aucun démarrage.
    /// </summary>
    internal async Task<StartReport> StartServicesAsync(IReadOnlyCollection<ManagedService> targets, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(targets);
        if (!_reconciled)
        {
            Reconcile();
        }

        var toStart = targets
            .Where(s => s.Status is not (ServiceStatus.Running or ServiceStatus.Starting or ServiceStatus.Stopping))
            .ToList();

        // Ports réclamés : ceux des cibles et ceux des services déjà en marche (ou en transition).
        var claimants = _services
            .Where(s => targets.Contains(s) || s.Status is ServiceStatus.Running or ServiceStatus.Starting or ServiceStatus.Stopping)
            .ToList();

        var errors = new List<string>();
        foreach (var group in claimants.SelectMany(s => s.Ports.Distinct().Select(p => (Port: p, Service: s))).GroupBy(x => x.Port))
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

            AtomicFile.WriteAllText(_paths.StateFile, JsonSerializer.Serialize(state), new UTF8Encoding(false));
        }
    }
}
