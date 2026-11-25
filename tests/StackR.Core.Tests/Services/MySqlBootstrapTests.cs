using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using StackR.Core.Services;

namespace StackR.Core.Tests.Services;

public sealed partial class MySqlBootstrapTests : IDisposable
{
    private readonly ServiceTestEnvironment _env = new(mySqlInitialized: false);

    public void Dispose() => _env.Dispose();

    [GeneratedRegex("--defaults-extra-file=\"([^\"]+)\"")]
    private static partial Regex ExtraFile();

    [Fact]
    public async Task EnsureInitialized_EmptyDataDir_SetsRootPasswordAndStoresSecret()
    {
        string? clientFile = null;
        string? contents = null;
        string? secretAtAlter = null;
        var aclUserOnly = false;
        _env.Runner.OnRun = (exe, args) =>
        {
            switch (Path.GetFileName(exe))
            {
                case "mysqld.exe":
                    Assert.Contains("--initialize-insecure", args, StringComparison.Ordinal);
                    Directory.CreateDirectory(Path.Combine(_env.Paths.MySqlData, "mysql"));
                    return 0;
                case "mysql.exe":
                    clientFile = ExtraFile().Match(args).Groups[1].Value;
                    contents = File.ReadAllText(clientFile);
                    secretAtAlter = _env.Secrets.Get("mysql-root");
                    aclUserOnly = IsUserOnly(clientFile);
                    return 0;
                case "mysqladmin.exe":
                    foreach (var p in _env.Runner.Processes)
                    {
                        FakeProcessRunner.KillQuietly(p);
                    }

                    return 0;
                default:
                    return 1;
            }
        };

        await MySqlBootstrap.EnsureInitializedAsync(
            _env.Paths, _env.Settings, _env.Runner, _env.Secrets, _env.MySqlDir, CancellationToken.None);

        var password = _env.Secrets.Get("mysql-root");
        Assert.NotNull(password);
        Assert.Equal(24, password.Length);
        Assert.Equal(password, secretAtAlter);
        Assert.Contains($"ALTER USER 'root'@'localhost' IDENTIFIED BY '{password}';", contents, StringComparison.Ordinal);
        Assert.True(aclUserOnly, "Le fichier client doit être réservé à l'utilisateur courant.");
        Assert.False(File.Exists(clientFile));
        Assert.Single(_env.Runner.Started);
        Assert.All(_env.Runner.Watchers, p => Assert.True(p.WaitForExit(5000)));
        Assert.Empty(Directory.EnumerateFiles(_env.Paths.Tmp, "client-*.cnf"));
    }

    [Fact]
    public async Task EnsureInitialized_AlreadyInitialized_DoesNothing()
    {
        Directory.CreateDirectory(Path.Combine(_env.Paths.MySqlData, "mysql"));

        await MySqlBootstrap.EnsureInitializedAsync(
            _env.Paths, _env.Settings, _env.Runner, _env.Secrets, _env.MySqlDir, CancellationToken.None);

        Assert.Empty(_env.Runner.Runs);
        Assert.Empty(_env.Runner.Started);
        Assert.Null(_env.Secrets.Get("mysql-root"));
    }

    [Fact]
    public async Task EnsureInitialized_InitializeFails_LeavesDataDirEmpty()
    {
        _env.Runner.OnRun = (exe, _) =>
        {
            File.WriteAllText(Path.Combine(_env.Paths.MySqlData, "ibdata1"), "partiel");
            return 1;
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => MySqlBootstrap.EnsureInitializedAsync(
            _env.Paths, _env.Settings, _env.Runner, _env.Secrets, _env.MySqlDir, CancellationToken.None));

        Assert.Empty(Directory.EnumerateFileSystemEntries(_env.Paths.MySqlData));
        Assert.Empty(_env.Runner.Started);
    }

    private static bool IsUserOnly(string path)
    {
        var security = new FileInfo(path).GetAccessControl();
        var me = WindowsIdentity.GetCurrent().User;
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier)).Cast<FileSystemAccessRule>().ToList();
        return security.AreAccessRulesProtected
            && rules.Count > 0
            && rules.All(r => r.IdentityReference.Equals(me) && r.AccessControlType == AccessControlType.Allow);
    }
}
