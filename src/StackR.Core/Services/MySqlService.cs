using System.Diagnostics;
using System.Security.Cryptography;
using StackR.Core.Config;
using StackR.Core.Paths;
using StackR.Core.Security;
using StackR.Core.Settings;

namespace StackR.Core.Services;

/// <summary>
/// MySQL : <c>mysqld --defaults-file=&lt;etc&gt;\my.ini</c>. Arrêt propre par <c>mysqladmin shutdown</c>
/// (mot de passe dans un fichier client temporaire), repli sur l'arbre de processus après 15 s.
/// </summary>
public sealed class MySqlService(
    StackRPaths paths,
    StackRSettings settings,
    IProcessRunner runner,
    Func<string?> mysqlDir,
    SecretStore secrets) : ManagedService(paths, settings, runner)
{
    internal static readonly TimeSpan ServerTimeout = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan ShutdownTimeout = TimeSpan.FromSeconds(15);

    private readonly Func<string?> _mysqlDir = mysqlDir ?? throw new ArgumentNullException(nameof(mysqlDir));
    private readonly SecretStore _secrets = secrets ?? throw new ArgumentNullException(nameof(secrets));

    public override string Id => "mysql";

    public override string DisplayName => "MySQL";

    public override int[] Ports => [Settings.MySqlPort];

    internal override string ExecutableName => "mysqld.exe";

    internal override TimeSpan StartTimeout => ServerTimeout;

    private protected override IEnumerable<string> LogFiles => ServerLogFiles(Paths);

    internal static IEnumerable<string> ServerLogFiles(StackRPaths paths) =>
    [
        Path.Combine(paths.Logs, "mysql-error.log"),
        ProcessRunner.LogPathFor(paths.Logs, "mysqld.exe"),
    ];

    internal static ProcessStartInfo ServerStartInfo(string mysqlDir, string myIni)
    {
        var psi = new ProcessStartInfo(Path.Combine(mysqlDir, "bin", "mysqld.exe")) { WorkingDirectory = mysqlDir };
        psi.ArgumentList.Add("--defaults-file=" + AnsiPath.For(myIni));
        return psi;
    }

    /// <summary><c>mysqladmin --defaults-extra-file="&lt;tmp&gt;\client-&lt;guid&gt;.cnf" --user=root shutdown</c>.</summary>
    internal static Task<int> ShutdownAsync(
        IProcessRunner runner, StackRPaths paths, string mysqladmin, int port, string password, CancellationToken ct) =>
        MySqlClientFile.RunAsync(
            runner, paths, mysqladmin, MySqlClientFile.ClientSection(port, password), "--user=root shutdown", ct);

    private protected override async Task<ProcessStartInfo> PrepareAsync(CancellationToken ct)
    {
        var dir = _mysqlDir() ?? throw NotInstalled();
        RequireFile(Path.Combine(dir, "bin", ExecutableName), DisplayName);

        var myIni = new ConfigWriter(Paths, Settings).WriteMyIni(dir);
        await MySqlBootstrap.EnsureInitializedAsync(Paths, Settings, Runner, _secrets, dir, ct).ConfigureAwait(false);
        return ServerStartInfo(dir, myIni);
    }

    private protected override async Task StopProcessAsync(Process process, CancellationToken ct)
    {
        var clock = Stopwatch.StartNew();
        string? password;
        try
        {
            password = _secrets.Get(MySqlBootstrap.SecretName);
        }
        catch (CryptographicException)
        {
            password = null;
        }

        var dir = _mysqlDir();
        var mysqladmin = dir is null ? null : Path.Combine(dir, "bin", "mysqladmin.exe");
        if (password is not null && mysqladmin is not null && File.Exists(mysqladmin))
        {
            using var limit = CancellationTokenSource.CreateLinkedTokenSource(ct);
            limit.CancelAfter(ShutdownTimeout);
            try
            {
                await ShutdownAsync(Runner, Paths, mysqladmin, Settings.MySqlPort, password, limit.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                // mysqladmin bloqué : repli plus bas.
            }

            var remaining = ShutdownTimeout - clock.Elapsed;
            if (remaining > TimeSpan.Zero)
            {
                await WaitForExitAsync(process, remaining, ct).ConfigureAwait(false);
            }
        }

        if (!process.HasExited)
        {
            // Repli : arbre de processus (PID vérifié comme nôtre).
            await base.StopProcessAsync(process, ct).ConfigureAwait(false);
        }
    }
}
