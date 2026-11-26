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
public sealed class StackREngine : IDisposable
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    private readonly EngineOptions _options;
    private readonly string _settingsPath;
    private readonly HttpClient? _ownedHttp;
    private readonly SemaphoreSlim _applyGate = new(1, 1);
    private readonly Lock _reconcileGate = new();
    private bool _reconciled;
    private bool _disposed;

    private StackREngine(string settingsPath, StackRSettings settings, EngineOptions options)
    {
        _settingsPath = settingsPath;
        _options = options;
        Settings = settings;
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
        Services = ServiceManager.CreateDefault(Paths, Settings, Store, Secrets, options.ProcessRunner ?? new ProcessRunner(Paths.Logs));
        Sites = new SiteScanner(Settings.EffectiveWww(Paths));
        Ca = new LocalCertificateAuthority(Paths, Secrets);
        Shell = new ShellIntegration(Paths, options.UserRegistryRoot, options.InstallDirectory);
    }

    public StackRPaths Paths { get; }

    /// <summary>
    /// Paramètres courants. <see cref="StackRSettings.ActiveVersions"/> est partagé avec les services
    /// (modifié sur place). Les autres valeurs se changent par <see cref="UpdateSettings"/> ; racine, dossier www
    /// et ports ne prennent effet qu'au prochain lancement (voir <see cref="RestartRequired"/>).
    /// </summary>
    public StackRSettings Settings { get; private set; }

    /// <summary>Vrai si des paramètres enregistrés exigent un redémarrage de StackR (racine, www, ports).</summary>
    public bool RestartRequired { get; private set; }

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
    public string? ActiveVersion(ComponentKind component) =>
        Settings.ActiveVersions.TryGetValue(ComponentStore.FolderName(component), out var version) ? version : null;

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
        SettingsStore.Save(_settingsPath, Settings);
    }

    /// <summary>
    /// Applique <paramref name="change"/> aux paramètres et les enregistre. Thème, langue, démarrage avec Windows,
    /// démarrage auto, HTTPS et fin d'assistant sont pris en compte tout de suite (réappliquer la configuration
    /// pour HTTPS) ; racine, dossier www et ports au prochain lancement.
    /// </summary>
    /// <returns>Vrai si un redémarrage de StackR est nécessaire pour appliquer les changements.</returns>
    public bool UpdateSettings(Func<StackRSettings, StackRSettings> change)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(change);

        var before = Settings;
        // Le dictionnaire des versions actives est partagé avec les services : on garde la même instance.
        var next = change(before) with { ActiveVersions = before.ActiveVersions };
        SettingsStore.Save(_settingsPath, next);
        Settings = next;

        var restart = !string.Equals(next.Root, before.Root, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(next.WwwPath, before.WwwPath, StringComparison.OrdinalIgnoreCase)
            || next.HttpPort != before.HttpPort
            || next.HttpsPort != before.HttpsPort
            || next.MySqlPort != before.MySqlPort
            || next.MailpitUiPort != before.MailpitUiPort
            || next.MailpitSmtpPort != before.MailpitSmtpPort;
        RestartRequired |= restart;
        SettingsChanged?.Invoke(this, EventArgs.Empty);
        return restart;
    }

    /// <summary>Démarre tous les services (règle « tout ou rien » si un port est occupé).</summary>
    public Task<StartReport> StartAllAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureReconciled();
        return Services.StartAllAsync(cancellationToken);
    }

    /// <summary>Arrête tous les services lancés par StackR.</summary>
    public Task StopAllAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        EnsureReconciled();
        return Services.StopAllAsync(cancellationToken);
    }

    /// <summary>Mot de passe root de MySQL (secret DPAPI), ou null si MySQL n'a pas encore été initialisé.</summary>
    public string? GetMySqlRootPassword() => Secrets.Get(MySqlBootstrap.SecretName);

    /// <summary>
    /// Réécrit httpd.conf / php.ini / my.ini des composants actifs, les vhosts et certificats des sites
    /// (en supprimant les vhosts des sites disparus), les shims et le PATH (premier lancement) ;
    /// si le bloc StackR du fichier hosts diffère, le CLI est relancé élevé (<c>hosts sync</c>).
    /// </summary>
    public async Task<ApplyResult> ApplyConfigurationAsync()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _applyGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var warnings = new List<string>();
            var dirs = ActiveDirectories();
            WriteComponentConfigs(dirs, warnings);

            var sites = Sites.Scan();
            WriteVhosts(sites, warnings);
            WriteShims(dirs, warnings);

            var (updated, declined) = await SyncHostsAsync([.. sites.Select(s => s.Name.Host)], warnings).ConfigureAwait(false);
            return new ApplyResult(updated, declined, warnings);
        }
        finally
        {
            _applyGate.Release();
        }
    }

    /// <summary>
    /// Active <paramref name="version"/> (déjà installée) de <paramref name="component"/>, sauvegarde,
    /// réapplique la configuration et redémarre le service concerné s'il tournait.
    /// </summary>
    /// <exception cref="InvalidOperationException">La version n'est pas installée.</exception>
    public async Task<ApplyResult> UseAsync(ComponentKind component, string version)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
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

        Settings.ActiveVersions[ComponentStore.FolderName(component)] = version;
        SaveSettings();
        var result = await ApplyConfigurationAsync().ConfigureAwait(false);

        if (!wasRunning)
        {
            return result;
        }

        await service!.StartAsync(CancellationToken.None).ConfigureAwait(false);
        return service.Status == ServiceStatus.Error && service.LastError is { } error
            ? result with { Warnings = [.. result.Warnings, $"{service.DisplayName} n'a pas redémarré : {error}"] }
            : result;
    }

    /// <summary>Active une version de PHP (voir <see cref="UseAsync"/>) : Apache est redémarré s'il tournait.</summary>
    public Task<ApplyResult> UsePhpAsync(string version) => UseAsync(ComponentKind.Php, version);

    /// <summary>
    /// Installe <paramref name="r"/> (empreinte vérifiée) ; si aucune version installée du composant n'est active,
    /// celle-ci le devient. La configuration est ensuite réappliquée.
    /// </summary>
    public async Task<ApplyResult> InstallAsync(ComponentRelease r, IProgress<InstallProgress>? p, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(r);
        await Installer.InstallAsync(r, p, ct).ConfigureAwait(false);

        if (ActiveDirectory(r.Component) is null)
        {
            Settings.ActiveVersions[ComponentStore.FolderName(r.Component)] = r.Version;
            SaveSettings();
        }

        return await ApplyConfigurationAsync().ConfigureAwait(false);
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
        _ => component.ToString(),
    };

    /// <summary>Service à redémarrer quand la version active de <paramref name="component"/> change.</summary>
    internal ManagedService? AffectedService(ComponentKind component)
    {
        var id = component switch
        {
            ComponentKind.Apache or ComponentKind.Php => "apache",
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
        _applyGate.Dispose();
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

    private void WriteComponentConfigs(Dictionary<ComponentKind, string> dirs, List<string> warnings)
    {
        var writer = new ConfigWriter(Paths, Settings);
        dirs.TryGetValue(ComponentKind.Php, out var php);
        if (php is not null)
        {
            writer.WritePhpIni(php);
        }

        if (dirs.TryGetValue(ComponentKind.Apache, out var apache))
        {
            if (php is null)
            {
                warnings.Add("Apache est actif mais aucune version de PHP n'est installée et active : httpd.conf n'a pas été généré.");
            }
            else
            {
                writer.WriteHttpdConf(apache, php);
            }
        }

        if (dirs.TryGetValue(ComponentKind.MySql, out var mysql))
        {
            writer.WriteMyIni(mysql);
        }
    }

    private void WriteVhosts(IReadOnlyList<Site> sites, List<string> warnings)
    {
        var sitesDir = Path.Combine(Paths.Etc, "sites");
        Directory.CreateDirectory(sitesDir);

        var https = Settings.HttpsEnabled && sites.Count > 0 && EnsureCa(warnings);
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
            AtomicFile.WriteAllText(Path.Combine(sitesDir, file), VhostBuilder.Build(site, Settings, cert, key), Utf8NoBom);
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

    private void WriteShims(Dictionary<ComponentKind, string> dirs, List<string> warnings)
    {
        try
        {
            Shell.WriteShims(dirs);
            if (!Settings.FirstRunCompleted)
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
