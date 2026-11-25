using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using StackR.Core.Services;

namespace StackR.Core.Tests.Services;

// Exécuteur factice : chaque « service » est un ping.exe inoffensif, et le port attendu est
// ouvert par un TcpListener réel du processus de test.
internal sealed class FakeProcessRunner : IProcessRunner, IDisposable
{
    private readonly Lock _gate = new();
    private readonly List<TcpListener> _listeners = [];
    private readonly List<Process> _processes = [];
    private readonly List<Process> _watchers = [];

    public List<ProcessStartInfo> Started { get; } = [];

    public List<(string Exe, string Args)> Runs { get; } = [];

    // Nom de l'exécutable (httpd.exe…) -> port à ouvrir au démarrage.
    public Dictionary<string, int> ListenOnStart { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Func<string, string, int> OnRun { get; set; } = (_, _) => 0;

    // Le processus démarré s'arrête tout de suite sans ouvrir de port.
    public bool ExitImmediately { get; set; }

    public IReadOnlyList<Process> Processes
    {
        get
        {
            lock (_gate)
            {
                return _processes.ToList();
            }
        }
    }

    // Poignées indépendantes de celles remises aux services (qui les libèrent à l'arrêt).
    public IReadOnlyList<Process> Watchers
    {
        get
        {
            lock (_gate)
            {
                return _watchers.ToList();
            }
        }
    }

    public Process Start(ProcessStartInfo psi)
    {
        lock (_gate)
        {
            Started.Add(psi);
        }

        var exe = Path.GetFileName(psi.FileName);
        if (!ExitImmediately && ListenOnStart.TryGetValue(exe, out var port))
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            lock (_gate)
            {
                _listeners.Add(listener);
            }
        }

        var process = ExitImmediately
            ? StartDummy(Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/c exit 3")
            : StartDummy();
        lock (_gate)
        {
            _processes.Add(process);
            if (TryGet(process.Id) is { } watcher)
            {
                _watchers.Add(watcher);
            }
        }

        return process;
    }

    public Process? TryGet(int pid)
    {
        try
        {
            return Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public Task<int> RunAsync(string exe, string args, CancellationToken ct)
    {
        lock (_gate)
        {
            Runs.Add((exe, args));
        }

        return Task.FromResult(OnRun(exe, args));
    }

    // Processus inoffensif et long (ping local), sans fenêtre.
    public static Process StartDummy(string? exe = null, string args = "-n 120 127.0.0.1")
    {
        var psi = new ProcessStartInfo(exe ?? Path.Combine(Environment.SystemDirectory, "PING.EXE"), args)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        return Process.Start(psi)!;
    }

    public static void KillQuietly(Process p)
    {
        try
        {
            if (!p.HasExited)
            {
                p.Kill(entireProcessTree: true);
                p.WaitForExit(5000);
            }
        }
        catch (InvalidOperationException)
        {
        }
    }

    public void StopListeners()
    {
        lock (_gate)
        {
            foreach (var l in _listeners)
            {
                l.Stop();
            }

            _listeners.Clear();
        }
    }

    public void Dispose()
    {
        StopListeners();
        foreach (var p in Processes.Concat(Watchers))
        {
            KillQuietly(p);
            p.Dispose();
        }
    }
}
