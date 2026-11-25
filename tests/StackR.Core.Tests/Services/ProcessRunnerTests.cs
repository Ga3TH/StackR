using System.Diagnostics;
using StackR.Core.Services;

namespace StackR.Core.Tests.Services;

public sealed class ProcessRunnerTests : IDisposable
{
    private readonly TempDirectory _logs = new();

    public void Dispose() => _logs.Dispose();

    private static string Cmd => Path.Combine(Environment.SystemDirectory, "cmd.exe");

    [Fact]
    public async Task RunAsync_ReturnsExitCodeAndLogsOutput()
    {
        var runner = new ProcessRunner(_logs.Path);

        var code = await runner.RunAsync(Cmd, "/c echo bonjour& echo oups 1>&2& exit 3", CancellationToken.None);

        Assert.Equal(3, code);
        var log = File.ReadAllText(Path.Combine(_logs.Path, "cmd-run.log"));
        Assert.Contains("bonjour", log, StringComparison.Ordinal);
        Assert.Contains("oups", log, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Start_RedirectsOutputToLog()
    {
        var runner = new ProcessRunner(_logs.Path);

        using var process = runner.Start(new ProcessStartInfo(Cmd, "/c echo pret"));
        await process.WaitForExitAsync();

        var log = Path.Combine(_logs.Path, "cmd.log");
        await ServiceTestEnvironment.WaitUntilAsync(() => File.Exists(log) && File.ReadAllText(log).Contains("pret", StringComparison.Ordinal));
        Assert.True(process.StartInfo.CreateNoWindow);
    }

    [Fact]
    public void TryGet_UnknownPid_ReturnsNull()
    {
        Assert.Null(new ProcessRunner(_logs.Path).TryGet(999_999_999));
    }
}
