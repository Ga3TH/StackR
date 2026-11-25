using System.Text.RegularExpressions;
using StackR.Core.Services;

namespace StackR.Core.Tests.Services;

public sealed partial class ManagedServiceTests : IDisposable
{
    private readonly ServiceTestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [GeneratedRegex("--defaults-extra-file=\"([^\"]+)\"")]
    private static partial Regex ExtraFile();

    [Fact]
    public async Task Apache_InvalidConfig_ReportsErrorAndDoesNotStart()
    {
        _env.Runner.OnRun = (exe, args) => exe.EndsWith("httpd.exe", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        var apache = _env.Apache();

        await apache.StartAsync(CancellationToken.None);

        Assert.Equal(ServiceStatus.Error, apache.Status);
        Assert.Contains("configuration Apache", apache.LastError, StringComparison.Ordinal);
        Assert.Empty(_env.Runner.Started);
        var (_, testArgs) = Assert.Single(_env.Runner.Runs);
        Assert.StartsWith("-t -f \"", testArgs, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(_env.Paths.Etc, "httpd.conf"), testArgs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Apache_Start_UsesGeneratedConf()
    {
        var apache = _env.Apache();

        await apache.StartAsync(CancellationToken.None);

        Assert.Equal(ServiceStatus.Running, apache.Status);
        var psi = Assert.Single(_env.Runner.Started);
        Assert.Equal(Path.Combine(_env.ApacheDir, "bin", "httpd.exe"), psi.FileName);
        Assert.Equal(["-f", Path.Combine(_env.Paths.Etc, "httpd.conf")], psi.ArgumentList);
        Assert.StartsWith(_env.PhpDir + ";", psi.Environment["PATH"], StringComparison.OrdinalIgnoreCase);
        Assert.Equal([_env.Settings.HttpPort, _env.Settings.HttpsPort], apache.Ports);
    }

    [Fact]
    public async Task Mailpit_Start_BindsLoopbackOnly()
    {
        var mailpit = _env.Mailpit();

        await mailpit.StartAsync(CancellationToken.None);

        Assert.Equal(ServiceStatus.Running, mailpit.Status);
        var psi = Assert.Single(_env.Runner.Started);
        Assert.Equal(
            [
                "--listen", $"127.0.0.1:{_env.Settings.MailpitUiPort}",
                "--smtp", $"127.0.0.1:{_env.Settings.MailpitSmtpPort}",
                "--database", Path.Combine(_env.Paths.Data, "mailpit.db"),
            ],
            psi.ArgumentList);
    }

    [Fact]
    public async Task Start_ProcessExitsBeforePort_ReportsErrorWithLogTail()
    {
        File.WriteAllText(Path.Combine(_env.Paths.Logs, "mailpit.log"), "ligne 1\nerreur fatale : bind refusé\n");
        _env.Runner.ExitImmediately = true;
        var mailpit = _env.Mailpit();

        await mailpit.StartAsync(CancellationToken.None);

        Assert.Equal(ServiceStatus.Error, mailpit.Status);
        Assert.Contains("erreur fatale : bind refusé", mailpit.LastError, StringComparison.Ordinal);
        Assert.Null(mailpit.ProcessId);
    }

    [Fact]
    public async Task Start_PortBusy_ReportsOwner()
    {
        using var squatter = new ServiceSquatter(_env.Settings.MailpitSmtpPort);
        var mailpit = _env.Mailpit();

        await mailpit.StartAsync(CancellationToken.None);

        Assert.Equal(ServiceStatus.Error, mailpit.Status);
        Assert.Equal(
            $"Port {_env.Settings.MailpitSmtpPort} utilisé par {Path.GetFileName(Environment.ProcessPath)} (PID {Environment.ProcessId})",
            mailpit.LastError);
        Assert.Empty(_env.Runner.Started);
    }

    [Fact]
    public async Task MySql_Stop_UsesMysqladminWithTemporaryClientFile()
    {
        _env.Secrets.Set("mysql-root", "MotDePasse-1");
        var mysql = _env.MySql();
        await mysql.StartAsync(CancellationToken.None);
        Assert.Equal(ServiceStatus.Running, mysql.Status);

        string? clientFile = null;
        string? contents = null;
        _env.Runner.OnRun = (exe, args) =>
        {
            Assert.EndsWith("mysqladmin.exe", exe, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("--user=root shutdown", args, StringComparison.Ordinal);
            clientFile = ExtraFile().Match(args).Groups[1].Value;
            contents = File.ReadAllText(clientFile);
            foreach (var p in _env.Runner.Processes)
            {
                FakeProcessRunner.KillQuietly(p);
            }

            return 0;
        };

        await mysql.StopAsync(CancellationToken.None);

        Assert.Equal(ServiceStatus.Stopped, mysql.Status);
        Assert.NotNull(clientFile);
        Assert.StartsWith(Path.Combine(_env.Paths.Tmp, "client-"), clientFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("[client]", contents, StringComparison.Ordinal);
        Assert.Contains("password=\"MotDePasse-1\"", contents, StringComparison.Ordinal);
        Assert.False(File.Exists(clientFile));
    }

    private sealed class ServiceSquatter : IDisposable
    {
        private readonly System.Net.Sockets.TcpListener _listener;

        public ServiceSquatter(int port)
        {
            _listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, port);
            _listener.Start();
        }

        public void Dispose() => _listener.Stop();
    }
}
