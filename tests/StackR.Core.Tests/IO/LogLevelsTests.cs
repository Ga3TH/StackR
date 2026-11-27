using StackR.Core.IO;

namespace StackR.Core.Tests.IO;

public class LogLevelsTests
{
    [Theory]
    [InlineData("[Thu Nov 27] [php:error] [pid 1] PHP Fatal error:  Uncaught Error", LogLevel.Error)]
    [InlineData("2026-11-27T09:40:11Z 12 [ERROR] [MY-010584] [Repl] Access denied", LogLevel.Error)]
    [InlineData("time=\"x\" level=error msg=\"boom\"", LogLevel.Error)]
    [InlineData("[Thu Nov 27] [core:warn] [pid 1] AH00045: child process", LogLevel.Warning)]
    [InlineData("PHP Warning:  Undefined variable $user", LogLevel.Warning)]
    [InlineData("2026-11-27T08:12:05Z 0 [Warning] [MY-010068] [Server] CA certificate", LogLevel.Warning)]
    [InlineData("[Thu Nov 27] [mpm_winnt:notice] [pid 1] AH00455: Apache configured", LogLevel.Notice)]
    [InlineData("PHP Deprecated:  strftime() is deprecated", LogLevel.Notice)]
    [InlineData("127.0.0.1 - - [27/Nov/2026] \"GET / HTTP/1.1\" 200 1200", LogLevel.Info)]
    [InlineData("", LogLevel.Info)]
    [InlineData(null, LogLevel.Info)]
    public void Classify_KnownFormats(string? line, LogLevel expected) => Assert.Equal(expected, LogLevels.Classify(line));
}
