using System.ComponentModel;
using System.Globalization;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using StackR.Core.Certificates;
using StackR.Core.Components;
using StackR.Core.Config;
using StackR.Core.Download;
using StackR.Core.Hosts;
using StackR.Core.IO;
using StackR.Core.Manifests;
using StackR.Core.Paths;
using StackR.Core.Security;
using StackR.Core.Services;
using StackR.Core.Settings;
using StackR.Core.Shell;
using StackR.Core.Sites;

namespace StackR.Core;

/// <summary>
/// Façade de StackR.Core partagée par l'application et le CLI : relie paramètres, composants,
/// services, sites, CA locale, shims et fichier hosts.
/// </summary>
/// <remarks>
/// Les opérations qui modifient l'état (installer, activer, désinstaller, démarrer, arrêter, appliquer
/// la configuration) passent une à une par une porte asynchrone unique : l'interface peut les déclencher
/// en même temps sans qu'elles s'entremêlent. Le téléchargement d'une installation se fait hors de la porte.
/// </remarks>
public sealed class StackREngine : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly EngineOptions _options;
    private readonly string _settingsPath;
    private readonly HttpClient? _ownedHttp;

    // Porte des opérations : jamais reprise par une opération qui la détient (variantes « Core » internes).
    // Volontairement jamais libérée : Dispose pendant une opération ne doit pas faire échouer son Release.
    private readonly SemaphoreSlim _operationGate = new(1, 1);

    // Protège le dictionnaire partagé des versions actives et l'écriture de stackr.json.
    private readonly Lock _settingsGate = new();
    private readonly Lock _reconcileGate = new();

    // Valeurs figées au lancement : racine, dossier www et ports ne changent qu'au prochain lancement.
    private readonly StackRSettings _startup;
    private volatile StackRSettings _settings;
    private volatile StackRSettings _effective;
    private volatile bool _restartRequired;
    private bool _reconciled;
    private volatile bool _disposed;

    private StackREngine(string settingsPath, StackRSettings settings, EngineOptions options)
    {
        _settingsPath = settingsPath;
        _options = options;
        _startup = settings;
        _settings = settings;
        _effective = settings;
        Paths = new StackRPaths(settings.Root);
        Paths.EnsureCreated();

        Manifest = options.Manifest ?? ManifestLoader.LoadEmbedded();
        Secrets = new SecretStore(Paths.Secrets);
        Store = new ComponentStore(Paths, ActiveVersion);

        HttpClient http;
        if (options.HttpClient is { } provided)
        {
            http = provided;
        }
        else
        {
            // Pas de délai global : SecureDownloader applique son propre délai d'inactivité.
            _ownedHttp = http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        }

        Installer = new ComponentInstaller(new SecureDownloader(http), Paths);
        Services = ServiceManager.CreateDefault(
            Paths, settings, () => _effective, ActiveDirectory, Secrets, options.ProcessRunner ?? new ProcessRunner(Paths.Logs));
        Sites = new SiteScanner(settings.EffectiveWww(Paths));
        Ca = new LocalCertificateAuthority(Paths, Secrets);
        Shell = new ShellIntegration(Paths, options.UserRegistryRoot, options.InstallDirectory);
    }

    public StackRPaths Paths { get; }

    /// <summary>
    /// Paramètres enregistrés. <see cref="StackRSettings.ActiveVersions"/> est partagé avec les services
    /// (modifié sur place par le moteur : lire une version par <see cref="ActiveVersion"/>). Les autres valeurs
    /// se changent par <see cref="UpdateSettings"/> ; racine, dossier www et ports ne prennent effet qu'au
    /// prochain lancement (voir <see cref="RestartRequired"/> et <see cref="EffectiveSettings"/>).
    /// </summary>
    public StackRSettings Settings => _settings;

    /// <summary>
    /// Paramètres réellement en vigueur dans ce processus : ceux de <see cref="Settings"/>, sauf racine,
    /// dossier www et ports, qui gardent leur valeur du lancement jusqu'au redémarrage de StackR.
    /// </summary>
    public StackRSettings EffectiveSettings => _effective;

    /// <summary>Vrai si des paramètres enregistrés exigent un redémarrage de StackR (racine, www, ports).</summary>
    public bool RestartRequired => _restartRequired;

    /// <summary>Levé après chaque <see cref="UpdateSettings"/> réussi.</summary>
    public event EventHandler? SettingsChanged;

    public Manifest Manifest { get; }

    public ComponentStore Store { get; }

    public ComponentInstaller Installer { get; }

    public ServiceManager Services { get; }

    public SiteScanner Sites { get; }

    public LocalCertificateAuthority Ca { get; }

    public SecretStore Secrets { get; }

    public ShellIntegration Shell { get; }

    /// <summary>Droits administrateur du processus courant.</summary>
    internal bool IsElevated => (_options.IsElevated ?? (() => Elevation.IsElevated))();

    /// <summary>
    /// Crée le moteur à partir de <paramref name="settingsPath"/> (par défaut <see cref="SettingsStore.DefaultPath"/>,
    /// <c>%LOCALAPPDATA%\StackR\stackr.json</c>) ; l'arborescence de la racine est créée si besoin.
    /// </summary>
    public static StackREngine Create(string? settingsPath = null) => Create(settingsPath, new EngineOptions());

    /// <summary>Variante avec dépendances remplacées (tests).</summary>
    public static StackREngine Create(string? settingsPath, EngineOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var path = settingsPath ?? SettingsStore.DefaultPath;
        return new StackREngine(path, SettingsStore.Load(path), options);
    }

    /// <summary>Version active d'un composant (clé : nom de dossier en minuscules), ou null.</summary>
    public string? ActiveVersion(ComponentKind component)
    {
        lock (_settingsGate)
        {
            return _settings.ActiveVersions.TryGetValue(ComponentStore.FolderName(component), out var version) ? version : null;
        }
    }

    /// <summary>Dossier de la version active, uniquement si elle est réellement installée.</summary>
    public string? ActiveDirectory(ComponentKind component)
    {
        var version = ActiveVersion(component);
        if (version is null)
        {
            return null;
        }

        try
        {
            return Store.IsInstalled(component, version) ? Paths.ComponentDir(ComponentStore.FolderName(component), version) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public void SaveSettings()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        lock (_settingsGate)
        {
            SettingsStore.Save(_settingsPath, _settings);
        }
    }

    /// <summary>
    /// Applique <paramref name="change"/> aux paramètres et les enregistre. Thème, langue, démarrage avec Windows,
    /// démarrage auto, HTTPS et fin d'assistant sont pris en compte tout de suite (réappliquer la configuration
    /// et redémarrer Apache pour HTTPS) ; racine, dossier www et ports au prochain lancement.
    /// Les versions actives ne se changent pas ici (voir <see cref="UseAsync"/>).
    /// </summary>
    /// <returns>Vrai si un redémarrage de StackR est nécessaire pour appliquer les paramètres enregistrés.</returns>
    public bool UpdateSettings(Func<StackRSettings, StackRSettings> change)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(change);

        bool restart;
        lock (_settingsGate)
        {
            var before = _settings;
            // Le dictionnaire des versions actives est partagé avec les services : on garde la même instance.
            var next = (change(before) ?? throw new InvalidOperationException("Paramètres nuls."))
                with { ActiveVersions = before.ActiveVersions };
            SettingsStore.Save(_settingsPath, next);
            _settings = next;
            _effective = next with
            {
                Root = _startup.Root,
                WwwPath = _startup.WwwPath,
                HttpPort = _startup.HttpPort,
                HttpsPort = _startup.HttpsPort,
                MySqlPort = _startup.MySqlPort,
                MailpitUiPort = _startup.MailpitUiPort,
                MailpitSmtpPort = _startup.MailpitSmtpPort,
            };

            // Comparé au lancement : revenir à la valeur d'origine annule le redémarrage demandé.
            restart = !string.Equals(next.Root, _startup.Root, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(next.WwwPath, _startup.WwwPath, StringComparison.OrdinalIgnoreCase)
                || next.HttpPort != _startup.HttpPort
                || next.HttpsPort != _startup.HttpsPort
                || next.MySqlPort != _startup.MySqlPort
                || next.MailpitUiPort != _startup.MailpitUiPort
                || next.MailpitSmtpPort != _startup.MailpitSmtpPort;
            _restartRequired = restart;
        }

        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return restart;
    }

    /// <summary>
    /// Applique la configuration puis démarre les services dont le composant est installé (les autres sont
    /// listés dans <see cref="StartReport.NotInstalled"/>). Règle « tout ou rien » : si un port est occupé,
    /// aucun service n'est démarré.
    /// </summary>
    public Task<StartReport> StartAllAsync(CancellationToken cancellationToken = default) =>
        ExclusiveAsync(() => StartCoreAsync(Services.Services, cancellationToken), cancellationToken);

    /// <summary>Applique la configuration puis démarre <paramref name="serviceId"/> (apache, mysql ou mailpit).</summary>
    /// <exception cref="ArgumentException">Service inconnu.</exception>
    public async Task<StartReport> StartAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        var service = FindService(serviceId);
        return await ExclusiveAsync(() => StartCoreAsync([service], cancellationToken), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Arrête tous les services lancés par StackR.</summary>
    public Task StopAllAsync(CancellationToken cancellationToken = default) =>
        ExclusiveAsync(
            async () =>
            {
                EnsureReconciled();
                await Services.StopAllAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    /// <summary>Arrête <paramref name="serviceId"/> (apache, mysql ou mailpit) s'il a été lancé par StackR.</summary>
    /// <exception cref="ArgumentException">Service inconnu.</exception>
    public async Task StopAsync(string serviceId, CancellationToken cancellationToken = default)
    {
        var service = FindService(serviceId);
        await ExclusiveAsync(
            async () =>
            {
                EnsureReconciled();
                await service.StopAsync(cancellationToken).ConfigureAwait(false);
            },
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Mot de passe root de MySQL (secret DPAPI), ou null si MySQL n'a pas encore été initialisé.</summary>
    public string? GetMySqlRootPassword() => Secrets.Get(MySqlBootstrap.SecretName);

    /// <summary>
    /// Réécrit httpd.conf / php.ini / my.ini des composants actifs, les vhosts et certificats des sites
    /// (en supprimant les vhosts des sites disparus), les shims et le PATH (premier lancement) ;
    /// si le bloc StackR du fichier hosts diffère, le CLI est relancé élevé (<c>hosts sync</c>).
    /// </summary>
    public Task<ApplyResult> ApplyConfigurationAsync() => ExclusiveAsync(ApplyCoreAsync, CancellationToken.None);

    /// <summary>
    /// Active <paramref name="version"/> (déjà installée) de <paramref name="component"/>, sauvegarde,
    /// réapplique la configuration et redémarre le service concerné s'il tournait.
    /// </summary>
    /// <exception cref="InvalidOperationException">La version n'est pas installée.</exception>
    public async Task<ApplyResult> UseAsync(ComponentKind component, string version)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ComponentStore.ValidateVersion(version);
        return await ExclusiveAsync(() => UseCoreAsync(component, version), CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Active une version de PHP (voir <see cref="UseAsync"/>) : Apache est redémarré s'il tournait.</summary>
    public Task<ApplyResult> UsePhpAsync(string version) => UseAsync(ComponentKind.Php, version);

    /// <summary>
    /// Installe <paramref name="r"/> (empreinte vérifiée) ; si aucune version installée du composant n'est active,
    /// celle-ci le devient. La configuration est ensuite réappliquée. Le téléchargement, la vérification et
    /// l'extraction se font hors de la porte des opérations : démarrer ou arrêter un service reste possible.
    /// </summary>
    public async Task<ApplyResult> InstallAsync(ComponentRelease r, IProgress<InstallProgress>? p, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(r);
        await Installer.InstallAsync(r, p, ct).ConfigureAwait(false);

        // Le composant est publié dans bin\ : l'activation et l'application ne sont plus annulées.
        return await ExclusiveAsync(
            async () =>
            {
                if (ActiveDirectory(r.Component) is null && Store.IsInstalled(r.Component, r.Version))
                {
                    SetActiveVersion(r.Component, r.Version);
                }

                return await ApplyCoreAsync().ConfigureAwait(false);
            },
            CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>
    /// Désinstalle <paramref name="version"/> de <paramref name="component"/> (sans effet si elle est absente).
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// C'est la version active, ou le service qui utilise ce composant n'est pas arrêté.
    /// </exception>
    /// <exception cref="IOException">Des fichiers sont encore utilisés (la version n'est plus vue comme installée).</exception>
    public async Task UninstallAsync(ComponentKind component, string version)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ComponentStore.ValidateVersion(version);
        await ExclusiveAsync(() => UninstallCoreAsync(component, version), CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>Prépare la CA (nettoyage des anciennes racines StackR) puis l'ajoute au magasin racine de l'utilisateur.</summary>
    /// <exception cref="CryptographicException">L'utilisateur a refusé l'ajout.</exception>
    public void TrustCertificateAuthority()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureCa(warnings: null);
        Ca.Trust();
    }

    /// <summary>Relit <c>state.json</c> une seule fois pour reprendre les services lancés par un autre processus StackR.</summary>
    public void EnsureReconciled()
    {
        lock (_reconcileGate)
        {
            if (_reconciled)
            {
                return;
            }

            Services.Reconcile();
            _reconciled = true;
        }
    }

    /// <summary>Écrit exactement <paramref name="hosts"/> dans le bloc StackR (exige des droits administrateur).</summary>
    /// <exception cref="ArgumentException">Un nom n'est pas un nom <c>.test</c> valide (rien n'est écrit).</exception>
    internal void WriteHostsFile(IReadOnlyCollection<string> hosts) => HostsFile.Write(hosts, _options.HostsPath);

    internal static string DisplayName(ComponentKind component) => component switch
    {
        ComponentKind.Apache => "Apache",
        ComponentKind.Php => "PHP",
        ComponentKind.MySql => "MySQL",
        ComponentKind.Mailpit => "Mailpit",
        ComponentKind.Node => "Node.js",
        ComponentKind.Composer => "Composer",
        ComponentKind.Adminer => "Adminer",
        _ => component.ToString(),
    };

    /// <summary>Composant principal d'un service (pour savoir s'il est installé).</summary>
    internal static ComponentKind ComponentOf(ManagedService service) => service.Id switch
    {
        "apache" => ComponentKind.Apache,
        "mysql" => ComponentKind.MySql,
        "mailpit" => ComponentKind.Mailpit,
        _ => throw new ArgumentOutOfRangeException(nameof(service), service.Id, null),
    };

    /// <summary>Service à redémarrer quand la version active de <paramref name="component"/> change.</summary>
    internal ManagedService? AffectedService(ComponentKind component)
    {
        var id = component switch
        {
            ComponentKind.Apache or ComponentKind.Php or ComponentKind.Adminer => "apache",
            ComponentKind.MySql => "mysql",
            ComponentKind.Mailpit => "mailpit",
            _ => null,
        };
        return id is null ? null : Services.Services.FirstOrDefault(s => s.Id == id);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Sites.Dispose();
        _ownedHttp?.Dispose();
    }

    private async Task<T> ExclusiveAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task ExclusiveAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            await operation().ConfigureAwait(false);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private ManagedService FindService(string serviceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(serviceId);
        var id = serviceId.Trim().ToLowerInvariant();
        return Services.Services.FirstOrDefault(s => s.Id == id)
            ?? throw new ArgumentException($"Service inconnu : « {serviceId} » (attendu : apache, mysql ou mailpit).", nameof(serviceId));
    }

    // Sous la porte.
    private async Task<StartReport> StartCoreAsync(IReadOnlyList<ManagedService> targets, CancellationToken cancellationToken)
    {
        EnsureReconciled();
        var installed = targets.Where(s => ActiveDirectory(ComponentOf(s)) is not null).ToList();
        var notInstalled = targets.Except(installed).Select(s => s.Id).ToList();
        if (installed.Count == 0)
        {
            var error = targets.Count == 1
                ? $"{targets[0].DisplayName} n'est pas installé."
                : "Aucun service n'est installé (Apache, MySQL ou Mailpit).";
            return new StartReport(false, [error]) { NotInstalled = notInstalled };
        }

        // Vhosts et certificats des nouveaux sites, hosts (UAC si nécessaire) avant le démarrage.
        var applied = await ApplyCoreAsync().ConfigureAwait(false);
        var report = await Services.StartServicesAsync(installed, cancellationToken).ConfigureAwait(false);
        return report with { Warnings = applied.Warnings, NotInstalled = notInstalled };
    }

    // Sous la porte.
    private async Task<ApplyResult> UseCoreAsync(ComponentKind component, string version)
    {
        if (!Store.IsInstalled(component, version))
        {
            throw new InvalidOperationException($"{DisplayName(component)} {version} n'est pas installé.");
        }

        EnsureReconciled();
        var service = AffectedService(component);
        var wasRunning = service?.Status == ServiceStatus.Running;
        if (wasRunning)
        {
            // Arrêt avant le changement : l'arrêt propre (mysqladmin) utilise encore l'ancienne version.
            await service!.StopAsync(CancellationToken.None).ConfigureAwait(false);
        }

        SetActiveVersion(component, version);
        var result = await ApplyCoreAsync().ConfigureAwait(false);

        if (!wasRunning)
        {
            return result;
        }

        await service!.StartAsync(CancellationToken.None).ConfigureAwait(false);
        return service.Status == ServiceStatus.Error && service.LastError is { } error
            ? result with { Warnings = [.. result.Warnings, $"{service.DisplayName} n'a pas redémarré : {error}"] }
            : result;
    }

    // Sous la porte.
    private async Task UninstallCoreAsync(ComponentKind component, string version)
    {
        var name = DisplayName(component);
        if (string.Equals(ActiveVersion(component), version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Impossible de désinstaller {name} {version} : c'est la version active.");
        }

        EnsureReconciled();
        if (AffectedService(component) is { } service
            && service.Status is ServiceStatus.Running or ServiceStatus.Starting or ServiceStatus.Stopping)
        {
            throw new InvalidOperationException(
                $"Impossible de désinstaller {name} {version} pendant que {service.DisplayName} tourne : arrêtez-le d'abord.");
        }

        try
        {
            // Suppression potentiellement longue (MySQL) : hors du thread appelant.
            await Task.Run(() => Store.Uninstall(component, version)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Désinstallation de {name} {version} incomplète : des fichiers sont encore utilisés ({ex.Message}). "
                + "Fermez les programmes concernés puis réessayez.",
                ex);
        }
    }

    // Modifie le dictionnaire partagé et enregistre ; restaure l'ancienne valeur si l'écriture échoue.
    private void SetActiveVersion(ComponentKind component, string version)
    {
        var key = ComponentStore.FolderName(component);
        lock (_settingsGate)
        {
            var versions = _settings.ActiveVersions;
            var had = versions.TryGetValue(key, out var previous);
            versions[key] = version;
            try
            {
                SettingsStore.Save(_settingsPath, _settings);
            }
            catch
            {
                if (had)
                {
                    versions[key] = previous!;
                }
                else
                {
                    versions.Remove(key);
                }

                throw;
            }
        }
    }

    // Sous la porte.
    private async Task<ApplyResult> ApplyCoreAsync()
    {
        // Instantané : une modification concurrente des paramètres ne mélange pas deux états dans les fichiers.
        var settings = _effective;
        var warnings = new List<string>();
        var dirs = ActiveDirectories();
        WriteComponentConfigs(dirs, settings, warnings);

        var sites = Sites.Scan();
        WriteVhosts(sites, settings, warnings);
        WriteShims(dirs, settings, warnings);

        var (updated, declined) = await SyncHostsAsync([.. sites.Select(s => s.Name.Host)], warnings).ConfigureAwait(false);
        return new ApplyResult(updated, declined, warnings);
    }

    private Dictionary<ComponentKind, string> ActiveDirectories()
    {
        var dirs = new Dictionary<ComponentKind, string>();
        foreach (var kind in Enum.GetValues<ComponentKind>())
        {
            if (ActiveDirectory(kind) is { } dir)
            {
                dirs[kind] = dir;
            }
        }

        return dirs;
    }

    private void WriteComponentConfigs(Dictionary<ComponentKind, string> dirs, StackRSettings settings, List<string> warnings)
    {
        var writer = new ConfigWriter(Paths, settings);
        dirs.TryGetValue(ComponentKind.Php, out var php);
        if (php is not null)
        {
            writer.WritePhpIni(php);
        }

        dirs.TryGetValue(ComponentKind.Adminer, out var adminer);
        if (dirs.TryGetValue(ComponentKind.Apache, out var apache))
        {
            if (php is null)
            {
                warnings.Add("Apache est actif mais aucune version de PHP n'est installée et active : httpd.conf n'a pas été généré.");
            }
            else
            {
                writer.WriteHttpdConf(apache, php, adminer);
            }
        }

        if (dirs.TryGetValue(ComponentKind.MySql, out var mysql))
        {
            writer.WriteMyIni(mysql);
        }
    }

    private void WriteVhosts(IReadOnlyList<Site> sites, StackRSettings settings, List<string> warnings)
    {
        var sitesDir = Path.Combine(Paths.Etc, "sites");
        Directory.CreateDirectory(sitesDir);

        var https = settings.HttpsEnabled && sites.Count > 0 && EnsureCa(warnings);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var site in sites)
        {
            string? cert = null;
            string? key = null;
            if (https)
            {
                try
                {
                    (cert, key) = Ca.IssueSiteCertificate(site.Name.Host);
                }
                catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or TimeoutException)
                {
                    warnings.Add($"Certificat HTTPS de {site.Name.Host} non émis ({ex.Message}) : le site reste accessible en HTTP.");
                }
            }

            // Le nom de site est une étiquette DNS validée : nom de fichier sûr.
            var file = site.Name.Value + ".conf";
            AtomicFile.WriteAllText(Path.Combine(sitesDir, file), VhostBuilder.Build(site, settings, cert, key), Utf8NoBom);
            written.Add(file);
        }

        foreach (var stale in Directory.EnumerateFiles(sitesDir, "*.conf"))
        {
            var name = Path.GetFileName(stale);
            if (string.Equals(Path.GetExtension(stale), ".conf", StringComparison.OrdinalIgnoreCase) && !written.Contains(name))
            {
                File.Delete(stale);
            }
        }
    }

    /// <summary>
    /// Génère ou recharge la CA (verrou inter-processus dans <see cref="LocalCertificateAuthority"/>) ;
    /// si elle vient d'être régénérée, retire les anciennes racines StackR du magasin de l'utilisateur.
    /// </summary>
    private bool EnsureCa(List<string>? warnings)
    {
        try
        {
            var before = Ca.CurrentCaThumbprint();
            using var root = Ca.EnsureRootCa();
            if (!_options.ManageCertificateStore)
            {
                return true;
            }

            if (!string.Equals(before, root.Thumbprint, StringComparison.OrdinalIgnoreCase))
            {
                LocalCertificateAuthority.RemoveStaleTrustedRoots(root);
            }

            if (warnings is not null && !Ca.IsTrusted())
            {
                warnings.Add("La CA locale de StackR n'est pas approuvée par Windows : lancez « stackr ca trust » pour éviter les alertes HTTPS.");
            }

            return true;
        }
        catch (Exception ex) when (ex is CryptographicException or IOException or UnauthorizedAccessException or TimeoutException)
        {
            if (warnings is null)
            {
                throw;
            }

            warnings.Add($"HTTPS indisponible : la CA locale n'a pas pu être préparée ({ex.Message}).");
            return false;
        }
    }

    private void WriteShims(Dictionary<ComponentKind, string> dirs, StackRSettings settings, List<string> warnings)
    {
        try
        {
            Shell.WriteShims(dirs);
            if (!settings.FirstRunCompleted)
            {
                Shell.EnsureOnUserPath();
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            warnings.Add($"Raccourcis en ligne de commande (shims/PATH) non mis à jour : {ex.Message}");
        }
    }

    /// <summary>
    /// Le moteur calcule les noms et les transmet au CLI élevé : avec une élévation « par-dessus l'épaule »,
    /// celui-ci s'exécute sous un autre compte, dont les paramètres et le dossier www diffèrent.
    /// </summary>
    private async Task<(bool Updated, bool Declined)> SyncHostsAsync(IReadOnlyCollection<string> hosts, List<string> warnings)
    {
        try
        {
            if (!HostsFile.NeedsUpdate(hosts, _options.HostsPath))
            {
                return (false, false);
            }

            if (IsElevated)
            {
                WriteHostsFile(hosts);
                return (true, false);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            warnings.Add($"Fichier hosts non mis à jour : {ex.Message}");
            return (false, false);
        }

        var names = string.Join(',', hosts.Order(StringComparer.Ordinal));
        if (names.Length > Elevation.MaxTokenLength)
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"Trop de sites ({hosts.Count}) pour une seule mise à jour du fichier hosts : la liste dépasse {Elevation.MaxTokenLength} caractères."));
            return (false, false);
        }

        string[] args = hosts.Count == 0 ? ["hosts", "sync", "--clear"] : ["hosts", "sync", "--names", names];
        int code;
        try
        {
            code = await (_options.RunElevated ?? Elevation.RunCliElevatedAsync)(args).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is FileNotFoundException or InvalidOperationException or Win32Exception or ArgumentException)
        {
            warnings.Add($"Fichier hosts non mis à jour : {ex.Message}");
            return (false, false);
        }

        switch (code)
        {
            case 0:
                return (true, false);
            case -1:
                warnings.Add("Mise à jour du fichier hosts refusée (UAC) : les nouveaux sites .test ne seront pas résolus. "
                    + "Elle sera proposée à nouveau à la prochaine application de la configuration.");
                return (false, true);
            default:
                warnings.Add(string.Create(CultureInfo.InvariantCulture, $"La mise à jour du fichier hosts a échoué (code {code})."));
                return (false, false);
        }
    }
}
