using StackR.Cli.Commands;
using StackR.Core.Manifests;

namespace StackR.Core.Tests.Cli;

public sealed class CliTests : IDisposable
{
    private readonly EngineTestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    private async Task<(int Code, string Output)> RunAsync(params string[] args)
    {
        using var engine = _env.CreateEngine();
        using var output = new StringWriter();
        var code = await CliApp.RunAsync(args, engine, output);
        return (code, output.ToString());
    }

    [Fact]
    public async Task Cli_HostsSync_NotElevated_Returns5()
    {
        var before = File.ReadAllText(_env.HostsPath);

        var (code, output) = await RunAsync("hosts", "sync", "--names", "alpha.test");

        Assert.Equal(5, code);
        Assert.Contains("administrateur", output, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(_env.HostsPath));
    }

    [Fact]
    public async Task Cli_HostsSync_Elevated_WritesExactlyTheGivenNames()
    {
        _env.Elevated = true;
        _env.AddSite("ignore-moi"); // jamais lu : seuls les noms transmis comptent

        var (code, _) = await RunAsync("hosts", "sync", "--names", "alpha.test,beta-site.test");

        Assert.Equal(0, code);
        Assert.Equal(
            "127.0.0.1 localhost\r\n# >>> StackR\r\n127.0.0.1 alpha.test\r\n127.0.0.1 beta-site.test\r\n# <<< StackR\r\n",
            File.ReadAllText(_env.HostsPath));
    }

    [Theory]
    [InlineData("alpha.test,evil.com")]
    [InlineData("alpha.test,,beta.test")]
    [InlineData("Alpha.test")]
    [InlineData("localhost")]
    public async Task Cli_HostsSync_InvalidName_Returns2_AndWritesNothing(string names)
    {
        _env.Elevated = true;
        var before = File.ReadAllText(_env.HostsPath);

        var (code, output) = await RunAsync("hosts", "sync", "--names", names);

        Assert.Equal(2, code);
        Assert.Contains("refusé", output, StringComparison.Ordinal);
        Assert.Equal(before, File.ReadAllText(_env.HostsPath));
    }

    [Fact]
    public async Task Cli_HostsSync_Clear_RemovesBlock()
    {
        _env.Elevated = true;
        File.WriteAllText(_env.HostsPath, "127.0.0.1 localhost\r\n# >>> StackR\r\n127.0.0.1 ancien.test\r\n# <<< StackR\r\n");

        var (code, _) = await RunAsync("hosts", "sync", "--clear");

        Assert.Equal(0, code);
        Assert.Equal("127.0.0.1 localhost\r\n", File.ReadAllText(_env.HostsPath));
    }

    [Fact]
    public async Task Cli_HostsSync_NamesAndClear_Returns2()
    {
        _env.Elevated = true;

        var (code, _) = await RunAsync("hosts", "sync", "--names", "a.test", "--clear");

        Assert.Equal(2, code);
    }

    private static async Task<(int Code, string Output, int EngineCreations)> RunWithoutEngineAsync(bool elevated, params string[] args)
    {
        var creations = 0;
        using var output = new StringWriter();
        var context = new CliContext(
            () =>
            {
                creations++;
                throw new InvalidOperationException("Le moteur ne doit pas être créé.");
            },
            output,
            output)
        {
            IsElevated = () => elevated,
            WriteHosts = _ => { },
        };
        var code = await CliApp.RunAsync(args, context);
        return (code, output.ToString(), creations);
    }

    [Theory]
    [InlineData("--names", "alpha.test")]
    [InlineData("--clear", null)]
    public async Task Cli_HostsSync_WithOption_NeverCreatesEngine(string option, string? value)
    {
        string[] args = value is null ? ["hosts", "sync", option] : ["hosts", "sync", option, value];
        var (code, output, creations) = await RunWithoutEngineAsync(true, args);

        Assert.Equal(0, code);
        Assert.Equal(0, creations);
        Assert.DoesNotContain("Erreur", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_HostsSync_ElevatedWithoutOption_RefusesWithCode2_NeverCreatesEngine()
    {
        var (code, output, creations) = await RunWithoutEngineAsync(true, "hosts", "sync");

        Assert.Equal(2, code);
        Assert.Equal(0, creations);
        Assert.Contains("--names", output, StringComparison.Ordinal);
        Assert.Contains("--clear", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_ResponseFileArgument_IsNotExpanded()
    {
        var file = Path.Combine(Path.GetTempPath(), "stackr-rsp-" + Guid.NewGuid().ToString("N") + ".rsp");
        File.WriteAllText(file, "status");
        try
        {
            var (code, output, creations) = await RunWithoutEngineAsync(true, "@" + file);

            // Sans expansion : « @fichier » est un argument inconnu (erreur d'analyse), jamais la commande « status ».
            Assert.NotEqual(0, code);
            Assert.Equal(0, creations);
            Assert.DoesNotContain("Service", output, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public async Task Cli_Status_PrintsAllServices()
    {
        _env.InstallFake("apache", "2.4.71");
        _env.Activate("apache", "2.4.71");

        var (code, output) = await RunAsync("status");

        Assert.Equal(0, code);
        var lines = output.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        Assert.Contains(lines, l => l.StartsWith("Service", StringComparison.Ordinal) && l.Contains("État", StringComparison.Ordinal));
        var apache = Assert.Single(lines, l => l.StartsWith("Apache", StringComparison.Ordinal));
        Assert.Contains("Arrêté", apache, StringComparison.Ordinal);
        Assert.Contains("2.4.71", apache, StringComparison.Ordinal);
        Assert.Contains(_env.Settings.HttpPort.ToString(System.Globalization.CultureInfo.InvariantCulture), apache, StringComparison.Ordinal);
        Assert.Contains("non installé", Assert.Single(lines, l => l.StartsWith("MySQL", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Single(lines, l => l.StartsWith("Mailpit", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Cli_Sites_ListsNameUrlAndRoot()
    {
        var dir = _env.AddSite("Mon Projet");
        Directory.CreateDirectory(Path.Combine(dir, "public"));

        var (code, output) = await RunAsync("sites");

        Assert.Equal(0, code);
        var line = Assert.Single(output.Split('\n'), l => l.StartsWith("mon-projet", StringComparison.Ordinal));
        Assert.Contains($"https://mon-projet.test:{_env.Settings.HttpsPort}", line, StringComparison.Ordinal);
        Assert.Contains(Path.Combine(dir, "public"), line, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_Versions_MarksInstalledActiveRecommended()
    {
        using (var probe = _env.CreateEngine())
        {
            var recommended = probe.Manifest.Recommended(ComponentKind.Php)!.Version;
            _env.InstallFake("php", recommended);
            _env.Activate("php", recommended);
        }

        var (code, output) = await RunAsync("versions", "php");

        Assert.Equal(0, code);
        Assert.DoesNotContain("Apache", output, StringComparison.Ordinal);
        var active = Assert.Single(output.Split('\n'), l => l.Contains("Active", StringComparison.Ordinal));
        Assert.Contains("Installée", active, StringComparison.Ordinal);
        Assert.Contains("Recommandée", active, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_Use_NotInstalled_Returns1()
    {
        var (code, output) = await RunAsync("use", "php", "8.4.28");

        Assert.Equal(1, code);
        Assert.Contains("n'est pas installé", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_Use_SwitchesActiveVersion()
    {
        _env.InstallFake("php", "8.3.37");
        _env.InstallFake("php", "8.4.28");
        _env.Activate("php", "8.3.37");

        var (code, _) = await RunAsync("use", "php", "8.4.28");

        Assert.Equal(0, code);
        Assert.Equal("8.4.28", Core.Settings.SettingsStore.Load(_env.SettingsPath).ActiveVersions["php"]);
    }

    [Theory]
    [InlineData("start", "nginx")]
    [InlineData("versions", "perl")]
    [InlineData("install", "perl")]
    public async Task Cli_UnknownTarget_Returns1(string command, string target)
    {
        var (code, output) = await RunAsync(command, target);

        Assert.Equal(1, code);
        Assert.Contains("inconnu", output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cli_DbPassword_NotInitialized_Returns1()
    {
        var (code, output) = await RunAsync("db", "password");

        Assert.Equal(1, code);
        Assert.Contains("MySQL", output, StringComparison.Ordinal);
    }
}
