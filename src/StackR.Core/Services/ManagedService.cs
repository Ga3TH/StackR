using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using StackR.Core.Paths;
using StackR.Core.Settings;

namespace StackR.Core.Services;

/// <summary>
/// Serveur lancé par StackR comme processus enfant caché, écoutant uniquement sur 127.0.0.1.
/// Démarrage : configuration (ré)écrite, validation, lancement, attente du port.
/// </summary>
public abstract class ManagedService
{
    private readonly Lock _gate = new();
    private readonly StackRSettings _settings;
    private Process? _process;
    private ServiceStatus _status = ServiceStatus.Stopped;
    private string? _lastError;

    private protected ManagedService(StackRPaths paths, StackRSettings settings, IProcessRunner runner)
    {
        Paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        Runner = runner ?? throw new ArgumentNullException(nameof(runner));
    }

    public abstract string Id { get; }

    public abstract string DisplayName { get; }

#pragma warning disable CA1819 // Signature imposée par le contrat (int[] Ports).
    public abstract int[] Ports { get; }
#pragma warning restore CA1819

    public ServiceStatus Status
    {
        get
        {
            lock (_gate)
            {
                return _status;
            }
        }
    }

    public string? LastError
    {
        get
        {
            lock (_gate)
            {
                return _lastError;
            }
        }
    }

    public event EventHandler? StatusChanged;

    /// <summary>PID du processus suivi (démarrage, en cours ou arrêt), sinon null.</summary>
    internal int? ProcessId
    {
        get
        {
            lock (_gate)
            {
                return _process?.Id;
            }
        }
    }

    /// <summary>Nom du fichier exécutable du serveur (httpd.exe…), pour reconnaître nos processus.</summary>
    internal abstract string ExecutableName { get; }

    /// <summary>Délai maximal pour que le port réponde après le lancement.</summary>
    internal virtual TimeSpan StartTimeout => TimeSpan.FromSeconds(20);

    private protected StackRPaths Paths { get; }

    /// <summary>
    /// Paramètres relus à chaque usage (moteur : un changement d'HTTPS s'applique au prochain démarrage
    /// sans relancer StackR) ; à défaut, ceux passés au constructeur.
    /// </summary>
    internal Func<StackRSettings>? SettingsProvider { get; init; }

    private protected StackRSettings Settings => SettingsProvider?.Invoke() ?? _settings;

    private protected IProcessRunner Runner { get; }

    /// <summary>Journaux à consulter en cas d'échec, par ordre de préférence.</summary>
    private protected abstract IEnumerable<string> LogFiles { get; }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        Process? stale;
        lock (_gate)
        {
            if (_status is ServiceStatus.Starting or ServiceStatus.Running or ServiceStatus.Stopping)
            {
                return;
            }

            stale = _process;
            _status = ServiceStatus.Starting;
            _lastError = null;
        }

        OnStatusChanged();

        if (stale is not null)
        {
            // Un arrêt a échoué (état Error) : le processus suivi, vérifié comme nôtre, est tué avant
            // tout nouveau lancement pour ne jamais le laisser orphelin.
            KillAndWait(stale);
            if (!HasExited(stale))
            {
                SetStatus(stale, ServiceStatus.Error, $"Impossible d'arrêter {DisplayName} (PID {stale.Id}).");
                return;
            }

            Release(stale, ServiceStatus.Starting, null);
        }

        Process? process = null;
        try
        {
            foreach (var port in Ports)
            {
                if (PortProbe.DescribeConflict(port) is { } conflict)
                {
                    throw new ServiceStartException(conflict);
                }
            }

            var psi = await PrepareAsync(cancellationToken).ConfigureAwait(false);
            process = Runner.Start(psi);
            lock (_gate)
            {
                _process = process;
            }

            process.EnableRaisingEvents = true;
            process.Exited += OnProcessExited;
            OnStatusChanged(); // Le PID est connu : state.json peut l'enregistrer.

            await WaitUntilListeningAsync(process, Ports[0], StartTimeout, DisplayName, LogFiles, cancellationToken)
                .ConfigureAwait(false);

            var running = false;
            lock (_gate)
            {
                if (ReferenceEquals(_process, process) && _status == ServiceStatus.Starting)
                {
                    _status = ServiceStatus.Running;
                    running = true;
                }
            }

            if (running)
            {
                OnStatusChanged();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Abandon(process, ServiceStatus.Stopped, null);
            throw;
        }
        catch (Exception ex) when (IsStartFailure(ex))
        {
            Abandon(process, ServiceStatus.Error, ex.Message);
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        Process? process;
        lock (_gate)
        {
            process = _process;
            if (process is null)
            {
                // Rien ne tourne : on efface simplement une erreur précédente.
                if (_status != ServiceStatus.Error)
                {
                    return;
                }

                _status = ServiceStatus.Stopped;
                _lastError = null;
            }
            else if (_status is ServiceStatus.Running or ServiceStatus.Error)
            {
                _status = ServiceStatus.Stopping;
            }
            else
            {
                // Démarrage (annulable via son jeton) ou arrêt déjà en cours.
                return;
            }
        }

        OnStatusChanged();
        if (process is null)
        {
            return;
        }

        try
        {
            await StopProcessAsync(process, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException
                                       or UnauthorizedAccessException or CryptographicException)
        {
            // Dernier recours : l'arbre de processus (vérifié comme nôtre).
            KillAndWait(process);
        }
        catch (OperationCanceledException)
        {
            if (!HasExited(process))
            {
                SetStatus(process, ServiceStatus.Running, null);
                throw;
            }
        }

        if (!HasExited(process))
        {
            SetStatus(process, ServiceStatus.Error, $"Impossible d'arrêter {DisplayName} (PID {process.Id}).");
            return;
        }

        Release(process, ServiceStatus.Stopped, null);
    }

    /// <summary>
    /// Reprend un processus resté vivant d'une session précédente (déjà vérifié comme nôtre).
    /// </summary>
    internal bool TryAdopt(Process process)
    {
        lock (_gate)
        {
            if (_process is not null || HasExited(process))
            {
                return false;
            }

            _process = process;
            _status = ServiceStatus.Running;
            _lastError = null;
        }

        process.EnableRaisingEvents = true;
        process.Exited += OnProcessExited;
        OnStatusChanged();
        return true;
    }

    /// <summary>Écrit la configuration, valide, et renvoie de quoi lancer le serveur.</summary>
    private protected abstract Task<ProcessStartInfo> PrepareAsync(CancellationToken ct);

    /// <summary>Arrêt du processus ; par défaut, arbre de processus.</summary>
    private protected virtual async Task StopProcessAsync(Process process, CancellationToken ct)
    {
        ProcessIdentity.KillTree(process);
        await WaitForExitAsync(process, TimeSpan.FromSeconds(10), ct).ConfigureAwait(false);
    }

    private protected ServiceStartException NotInstalled() => new($"{DisplayName} n'est pas installé.");

    private protected static string RequireFile(string path, string displayName)
    {
        return File.Exists(path)
            ? path
            : throw new ServiceStartException($"{displayName} est incomplet : {Path.GetFileName(path)} introuvable dans {Path.GetDirectoryName(path)}.");
    }

    private protected static string Quote(string arg) => "\"" + arg + "\"";

    /// <summary>Attend que 127.0.0.1:port accepte une connexion ; échoue si le processus s'arrête avant.</summary>
    internal static async Task WaitUntilListeningAsync(
        Process process, int port, TimeSpan timeout, string displayName, IEnumerable<string> logFiles, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (HasExited(process))
            {
                throw new ServiceStartException(
                    $"{displayName} s'est arrêté pendant le démarrage{ExitCodeSuffix(process)}.{LogTail(logFiles)}");
            }

            if (await PortProbe.IsAcceptingAsync(port, ct).ConfigureAwait(false))
            {
                return;
            }

            if (clock.Elapsed > timeout)
            {
                throw new ServiceStartException(string.Create(
                    CultureInfo.InvariantCulture,
                    $"{displayName} ne répond pas sur le port {port} après {timeout.TotalSeconds:0} s.{LogTail(logFiles)}"));
            }

            await Task.Delay(200, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Attend la fin du processus au plus <paramref name="timeout"/> (sans lever si le délai expire).</summary>
    internal static async Task WaitForExitAsync(Process process, TimeSpan timeout, CancellationToken ct)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            // Délai écoulé : l'appelant décide (kill).
        }
    }

    /// <summary>Dernières lignes du journal le plus récent parmi <paramref name="files"/>.</summary>
    internal static string LogTail(IEnumerable<string> files, int lines = 10)
    {
        var file = files
            .Select(f => new FileInfo(f))
            .Where(f => f.Exists && f.Length > 0)
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .FirstOrDefault();
        if (file is null)
        {
            return "";
        }

        try
        {
            using var stream = new FileStream(file.FullName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            const int MaxBytes = 16 * 1024;
            if (stream.Length > MaxBytes)
            {
                stream.Seek(-MaxBytes, SeekOrigin.End);
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            var tail = reader.ReadToEnd()
                .Split('\n')
                .Select(l => l.TrimEnd('\r'))
                .Where(l => l.Length > 0)
                .TakeLast(lines);
            return $"\nDernières lignes de {file.Name} :\n" + string.Join('\n', tail);
        }
        catch (IOException)
        {
            return "";
        }
        catch (UnauthorizedAccessException)
        {
            return "";
        }
    }

    private static bool IsStartFailure(Exception ex) =>
        ex is ServiceStartException or InvalidOperationException or IOException or UnauthorizedAccessException
            or Win32Exception or CryptographicException or ArgumentException;

    private static bool HasExited(Process process)
    {
        try
        {
            return process.HasExited;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
        catch (Win32Exception)
        {
            return false;
        }
    }

    private static string ExitCodeSuffix(Process process)
    {
        try
        {
            return string.Create(CultureInfo.InvariantCulture, $" (code {process.ExitCode})");
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return "";
        }
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        if (sender is not Process process)
        {
            return;
        }

        bool crashed;
        lock (_gate)
        {
            // Pendant un démarrage ou un arrêt, c'est l'appelant qui gère la fin du processus.
            crashed = ReferenceEquals(_process, process) && _status == ServiceStatus.Running;
        }

        if (crashed)
        {
            Release(
                process,
                ServiceStatus.Error,
                $"{DisplayName} s'est arrêté de façon inattendue{ExitCodeSuffix(process)}.{LogTail(LogFiles)}");
        }
    }

    private static void KillAndWait(Process process)
    {
        try
        {
            ProcessIdentity.KillTree(process);
            process.WaitForExit(5000);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Accès refusé ou déjà terminé : l'état est vérifié ensuite.
        }
    }

    // Échec ou annulation du démarrage : le processus éventuel est tué puis libéré.
    private void Abandon(Process? process, ServiceStatus status, string? error)
    {
        if (process is not null)
        {
            KillAndWait(process);
            Release(process, status, error);
            return;
        }

        lock (_gate)
        {
            _status = status;
            _lastError = error;
        }

        OnStatusChanged();
    }

    // Le processus n'est plus suivi : nouvel état, poignée libérée.
    private void Release(Process process, ServiceStatus status, string? error)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_process, process))
            {
                return;
            }

            _process = null;
            _status = status;
            _lastError = error;
        }

        process.Exited -= OnProcessExited;
        process.Dispose();
        OnStatusChanged();
    }

    private void SetStatus(Process process, ServiceStatus status, string? error)
    {
        lock (_gate)
        {
            if (!ReferenceEquals(_process, process))
            {
                return;
            }

            _status = status;
            _lastError = error;
        }

        OnStatusChanged();
    }

    private void OnStatusChanged() => StatusChanged?.Invoke(this, EventArgs.Empty);
}
