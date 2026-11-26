using System.Text.RegularExpressions;
using StackR.Core.Services;

namespace StackR.Core.Tests.Services;

public sealed partial class MySqlPasswordChangerTests : IDisposable
{
    private readonly ServiceTestEnvironment _env = new();

    public void Dispose() => _env.Dispose();

    [GeneratedRegex("--defaults-extra-file=\"([^\"]+)\"")]
    private static partial Regex OptionsFile();

    [Theory]
    [InlineData("court")]
    [InlineData("avec espace 12345")]
    [InlineData("apos'trophe123")]
    [InlineData("gui\"llemet123")]
    [InlineData("anti\\slash123")]
    [InlineData("dièse#12345")]
    [InlineData("")]
    public void Validate_BadPassword_ReturnsMessage(string password) =>
        Assert.NotNull(MySqlPasswordChanger.Validate(password));

    [Fact]
    public void Validate_GeneratedPassword_IsAccepted() =>
        Assert.Null(MySqlPasswordChanger.Validate(MySqlPasswordChanger.Generate()));

    [Fact]
    public async Task Change_Success_UpdatesSecretAndKeepsPasswordsOffCommandLine()
    {
        _env.Secrets.Set("mysql-root", "ancien-mot-de-passe");
        string? args0 = null;
        string? file = null;
        string? contents = null;
        _env.Runner.OnRun = (exe, args) =>
        {
            Assert.Equal("mysql.exe", Path.GetFileName(exe));
            args0 = args;
            file = OptionsFile().Match(args).Groups[1].Value;
            contents = File.ReadAllText(file);
            return 0;
        };

        await MySqlPasswordChanger.ChangeRootPasswordAsync(
            _env.Runner, _env.Paths, _env.Secrets, _env.MySqlDir, _env.Settings.MySqlPort, "Nouveau-Mdp-123!", CancellationToken.None);

        Assert.Equal("Nouveau-Mdp-123!", _env.Secrets.Get("mysql-root"));
        Assert.DoesNotContain("ancien-mot-de-passe", args0, StringComparison.Ordinal);
        Assert.DoesNotContain("Nouveau-Mdp-123!", args0, StringComparison.Ordinal);
        Assert.Contains("password=\"ancien-mot-de-passe\"", contents, StringComparison.Ordinal);
        Assert.Contains("ALTER USER 'root'@'localhost' IDENTIFIED BY 'Nouveau-Mdp-123!'", contents, StringComparison.Ordinal);
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Change_CommandFails_KeepsOldSecretAndCleansUp()
    {
        _env.Secrets.Set("mysql-root", "ancien-mot-de-passe");
        string? file = null;
        _env.Runner.OnRun = (_, args) =>
        {
            file = OptionsFile().Match(args).Groups[1].Value;
            return 1;
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => MySqlPasswordChanger.ChangeRootPasswordAsync(
            _env.Runner, _env.Paths, _env.Secrets, _env.MySqlDir, _env.Settings.MySqlPort, "Nouveau-Mdp-123!", CancellationToken.None));

        Assert.Equal("ancien-mot-de-passe", _env.Secrets.Get("mysql-root"));
        Assert.False(File.Exists(file));
    }

    [Fact]
    public async Task Change_NoSecret_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => MySqlPasswordChanger.ChangeRootPasswordAsync(
            _env.Runner, _env.Paths, _env.Secrets, _env.MySqlDir, _env.Settings.MySqlPort, "Nouveau-Mdp-123!", CancellationToken.None));
        Assert.Empty(_env.Runner.Runs);
    }

    [Fact]
    public async Task Change_InvalidPassword_ThrowsWithoutRunning()
    {
        _env.Secrets.Set("mysql-root", "ancien-mot-de-passe");
        await Assert.ThrowsAsync<ArgumentException>(() => MySqlPasswordChanger.ChangeRootPasswordAsync(
            _env.Runner, _env.Paths, _env.Secrets, _env.MySqlDir, _env.Settings.MySqlPort, "x' OR '1", CancellationToken.None));
        Assert.Empty(_env.Runner.Runs);
    }

    [Fact]
    public void Terminal_WithWindowsTerminal_PasswordOnlyInOptionsFile()
    {
        var mysql = Path.Combine(_env.MySqlDir, "bin", "mysql.exe");
        var psi = MySqlTerminal.CreateStartInfo(_env.Paths, mysql, 3306, "secret-xyz", @"C:\fake\wt.exe", out var file);
        try
        {
            Assert.Equal(@"C:\fake\wt.exe", psi.FileName);
            Assert.DoesNotContain("secret-xyz", string.Join(' ', psi.ArgumentList), StringComparison.Ordinal);
            Assert.Contains("--defaults-extra-file=" + file, psi.ArgumentList);
            Assert.Contains("password=\"secret-xyz\"", File.ReadAllText(file), StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void Terminal_WithoutWindowsTerminal_RunsMySqlDirectly()
    {
        var mysql = Path.Combine(_env.MySqlDir, "bin", "mysql.exe");
        var psi = MySqlTerminal.CreateStartInfo(_env.Paths, mysql, 3306, "secret-xyz", null, out var file);
        try
        {
            Assert.Equal(mysql, psi.FileName);
            Assert.True(psi.UseShellExecute);
        }
        finally
        {
            File.Delete(file);
        }
    }
}
