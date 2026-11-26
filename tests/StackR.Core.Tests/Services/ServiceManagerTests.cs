using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using StackR.Core.Services;

namespace StackR.Core.Tests.Services;

public sealed class ServiceManagerTests : IDisposable
{
    private readonly ServiceTestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private static string OwnName => Path.GetFileName(Environment.ProcessPath)!;

    [Fact]
    public async Task StartAll_PortBusy_ReportsOwnerAndStartsNothing()
    {
        // Simule Laragon : quelqu'un écoute déjà sur le port MySQL.
        var squatter = new TcpListener(IPAddress.Loopback, _env.Settings.MySqlPort);
        squatter.Start();
        try
        {
            var manager = _env.Manager();

            var report = await manager.StartAllAsync(CancellationToken.None);

            Assert.False(report.Success);
            Assert.Contains(
                $"Port {_env.Settings.MySqlPort} utilisé par {OwnName} (PID {Environment.ProcessId})",
                report.Errors);
            Assert.Empty(_env.Runner.Started);
            Assert.Empty(_env.Runner.Runs);
            Assert.All(manager.Services, s => Assert.Equal(ServiceStatus.Stopped, s.Status));
            Assert.Empty(_env.ReadState());
        }
        finally
        {
            squatter.Stop();
        }
    }

    [Fact]
    public async Task StartAll_Success_WritesState()
    {
        var manager = _env.Manager();

        var report = await manager.StartAllAsync(CancellationToken.None);

        Assert.True(report.Success, string.Join(" | ", report.Errors));
        Assert.Empty(report.Errors);
        Assert.All(manager.Services, s => Assert.Equal(ServiceStatus.Running, s.Status));
        var state = _env.ReadState();
        Assert.Equal(3, state.Count);
        foreach (var s in manager.Services)
        {
            Assert.Equal(s.ProcessId, state[s.Id]);
        }

        Assert.True(File.Exists(Path.Combine(_env.Paths.Etc, "httpd.conf")));
        Assert.True(File.Exists(Path.Combine(_env.Paths.Etc, "php.ini")));
        Assert.True(File.Exists(Path.Combine(_env.Paths.Etc, "my.ini")));
        Assert.Equal(3, _env.Runner.Started.Count);
    }

    [Fact]
    public async Task StopAll_ClearsState()
    {
        var manager = _env.Manager();
        var report = await manager.StartAllAsync(CancellationToken.None);
        Assert.True(report.Success, string.Join(" | ", report.Errors));
        var processes = _env.Runner.Watchers;

        await manager.StopAllAsync(CancellationToken.None);

        Assert.Empty(_env.ReadState());
        Assert.All(manager.Services, s => Assert.Equal(ServiceStatus.Stopped, s.Status));
        Assert.Equal(3, processes.Count);
        Assert.All(processes, p => Assert.True(p.WaitForExit(5000)));
    }

    [Fact]
    public async Task Reconcile_ReusedPid_IsNotOurs()
    {
        // Un PID recyclé : un processus étranger (hors de bin\) porte l'ancien PID d'Apache.
        using var foreign = FakeProcessRunner.StartDummy();
        try
        {
            _env.WriteState(new() { ["apache"] = foreign.Id, ["mysql"] = 999_999_999 });
            var manager = _env.Manager();

            manager.Reconcile();

            var apache = manager.Services.Single(s => s.Id == "apache");
            Assert.Equal(ServiceStatus.Stopped, apache.Status);
            Assert.Null(apache.ProcessId);
            Assert.Empty(_env.ReadState());

            await manager.StopAllAsync(CancellationToken.None);
            Assert.False(foreign.HasExited, "Un processus étranger ne doit jamais être tué.");
        }
        finally
        {
            FakeProcessRunner.KillQuietly(foreign);
        }
    }

    [Fact]
    public async Task Reconcile_OurPid_MarksRunning()
    {
        // Processus resté vivant après la mort de l'app : son exécutable est sous bin\.
        var exe = Path.Combine(_env.ApacheDir, "bin", "httpd.exe");
        File.Copy(Path.Combine(Environment.SystemDirectory, "PING.EXE"), exe, overwrite: true);
        using var ours = FakeProcessRunner.StartDummy(exe);
        try
        {
            _env.WriteState(new() { ["apache"] = ours.Id });
            var manager = _env.Manager();

            manager.Reconcile();

            var apache = manager.Services.Single(s => s.Id == "apache");
            Assert.Equal(ServiceStatus.Running, apache.Status);
            Assert.Equal(ours.Id, apache.ProcessId);
            Assert.Equal(new Dictionary<string, int> { ["apache"] = ours.Id }, _env.ReadState());

            await manager.StopAllAsync(CancellationToken.None);
            Assert.True(ours.WaitForExit(5000));
            Assert.Equal(ServiceStatus.Stopped, apache.Status);
            Assert.Empty(_env.ReadState());
        }
        finally
        {
            FakeProcessRunner.KillQuietly(ours);
        }
    }

    [Fact]
    public async Task Crash_WhileRunning_SetsErrorAndUpdatesState()
    {
        var manager = _env.Manager();
        Assert.True((await manager.StartAllAsync(CancellationToken.None)).Success);
        var mailpit = manager.Services.Single(s => s.Id == "mailpit");

        // Le statut devient Error sous verrou AVANT que StatusChanged soit levé : sonder Status
        // seul laisse une fenêtre où l'événement (et l'écriture de state.json) n'a pas encore eu lieu.
        // On attend donc l'événement lui-même. Le gestionnaire du ServiceManager, abonné en premier
        // (constructeur), a déjà réécrit state.json quand le nôtre s'exécute.
        var crashed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        mailpit.StatusChanged += (_, _) =>
        {
            if (mailpit.Status == ServiceStatus.Error)
            {
                crashed.TrySetResult();
            }
        };
        var pid = mailpit.ProcessId!.Value;

        using (var victim = Process.GetProcessById(pid))
        {
            victim.Kill();
        }

        await crashed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Equal(ServiceStatus.Error, mailpit.Status);
        Assert.NotNull(mailpit.LastError);
        Assert.Contains("Mailpit", mailpit.LastError, StringComparison.Ordinal);
        Assert.Null(mailpit.ProcessId);
        var state = _env.ReadState();
        Assert.False(state.ContainsKey("mailpit"));
        Assert.Equal(2, state.Count);
    }

    [Fact]
    public async Task StartAll_ComponentMissing_ReportsFrenchError()
    {
        var manager = new ServiceManager(
            _env.Paths, _env.Runner, [_env.Apache(() => null), _env.Mailpit()]);

        var report = await manager.StartAllAsync(CancellationToken.None);

        Assert.False(report.Success);
        Assert.Contains("Apache n'est pas installé.", report.Errors);
        var apache = manager.Services.Single(s => s.Id == "apache");
        Assert.Equal(ServiceStatus.Error, apache.Status);
        Assert.Equal("Apache n'est pas installé.", apache.LastError);
    }

    [Fact]
    public async Task StartAll_StateWrittenAtomically_NoTempLeft()
    {
        var manager = _env.Manager();

        await manager.StartAllAsync(CancellationToken.None);

        Assert.True(File.Exists(_env.Paths.StateFile));
        Assert.Empty(Directory.EnumerateFiles(_env.Paths.Tmp, "state.json.*.tmp"));
        Assert.Equal(3, _env.ReadState().Count);
    }
}
