using System.Diagnostics;
using System.Globalization;
using StackR.Core.Config;
using StackR.Core.Paths;
using StackR.Core.Settings;

namespace StackR.Core.Services;

/// <summary>Apache httpd : <c>httpd.exe -f "&lt;etc&gt;\httpd.conf"</c>, validé par <c>httpd -t</c> avant chaque démarrage.</summary>
public sealed class ApacheService(
    StackRPaths paths,
    StackRSettings settings,
    IProcessRunner runner,
    Func<string?> apacheDir,
    Func<string?> phpDir) : ManagedService(paths, settings, runner)
{
    private readonly Func<string?> _apacheDir = apacheDir ?? throw new ArgumentNullException(nameof(apacheDir));
    private readonly Func<string?> _phpDir = phpDir ?? throw new ArgumentNullException(nameof(phpDir));

    public override string Id => "apache";

    public override string DisplayName => "Apache";

    public override int[] Ports => Settings.HttpsEnabled
        ? [Settings.HttpPort, Settings.HttpsPort]
        : [Settings.HttpPort];

    internal override string ExecutableName => "httpd.exe";

    private protected override IEnumerable<string> LogFiles =>
    [
        Path.Combine(Paths.Logs, "apache-error.log"),
        ProcessRunner.LogPathFor(Paths.Logs, ExecutableName),
    ];

    private protected override async Task<ProcessStartInfo> PrepareAsync(CancellationToken ct)
    {
        var dir = _apacheDir() ?? throw NotInstalled();
        var httpd = RequireFile(Path.Combine(dir, "bin", ExecutableName), DisplayName);
        var php = _phpDir() ?? throw new ServiceStartException("PHP n'est pas installé.");

        var writer = new ConfigWriter(Paths, Settings);
        writer.WritePhpIni(php);
        var conf = writer.WriteHttpdConf(dir, php);

        var code = await Runner.RunAsync(httpd, "-t -f " + Quote(conf), ct).ConfigureAwait(false);
        if (code != 0)
        {
            throw new ServiceStartException(string.Create(
                CultureInfo.InvariantCulture,
                $"La configuration Apache est invalide (httpd -t, code {code}).{LogTail([ProcessRunner.RunLogPathFor(Paths.Logs, httpd)])}"));
        }

        var psi = new ProcessStartInfo(httpd) { WorkingDirectory = dir };
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add(conf);

        // Les DLL de PHP (php8ts.dll, dépendances des extensions) doivent être trouvées par httpd.
        psi.Environment["PATH"] = php + ";" + (psi.Environment.TryGetValue("PATH", out var path) ? path : "");
        return psi;
    }
}
