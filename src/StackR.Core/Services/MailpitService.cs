using System.Diagnostics;
using System.Globalization;
using StackR.Core.Paths;
using StackR.Core.Settings;

namespace StackR.Core.Services;

/// <summary>Mailpit : interface web et SMTP, tous deux sur 127.0.0.1.</summary>
public sealed class MailpitService(
    StackRPaths paths,
    StackRSettings settings,
    IProcessRunner runner,
    Func<string?> mailpitDir) : ManagedService(paths, settings, runner)
{
    private readonly Func<string?> _mailpitDir = mailpitDir ?? throw new ArgumentNullException(nameof(mailpitDir));

    public override string Id => "mailpit";

    public override string DisplayName => "Mailpit";

    // Le premier port (interface web) sert à détecter que Mailpit est prêt.
    public override int[] Ports => [Settings.MailpitUiPort, Settings.MailpitSmtpPort];

    internal override string ExecutableName => "mailpit.exe";

    private protected override IEnumerable<string> LogFiles =>
        [ProcessRunner.LogPathFor(Paths.Logs, ExecutableName)];

    private protected override Task<ProcessStartInfo> PrepareAsync(CancellationToken ct)
    {
        var dir = _mailpitDir() ?? throw NotInstalled();
        var exe = RequireFile(Path.Combine(dir, ExecutableName), DisplayName);
        Directory.CreateDirectory(Paths.Data);

        var psi = new ProcessStartInfo(exe) { WorkingDirectory = dir };
        psi.ArgumentList.Add("--listen");
        psi.ArgumentList.Add(string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{Settings.MailpitUiPort}"));
        psi.ArgumentList.Add("--smtp");
        psi.ArgumentList.Add(string.Create(CultureInfo.InvariantCulture, $"127.0.0.1:{Settings.MailpitSmtpPort}"));
        psi.ArgumentList.Add("--database");
        psi.ArgumentList.Add(Path.Combine(Paths.Data, "mailpit.db"));
        return Task.FromResult(psi);
    }
}
