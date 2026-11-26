using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.RegularExpressions;
using StackR.Core.Services;

namespace StackR.Core.Tests.Services;

public sealed partial class MySqlBootstrapTests : IDisposable
{
    private readonly ServiceTestEnvironment _env = new(mySqlInitialized: false);

    public void Dispose() => _env.Dispose();

    [GeneratedRegex("--init-file=\"([^\"]+)\"")]
    private static partial Regex InitFile();

    [Fact]
    public async Task EnsureInitialized_EmptyDataDir_SetsRootPasswordViaInitFile()
    {
        string? initFile = null;
        string? contents = null;
        string? secretAtInit = null;
        string? args0 = null;
        var aclUserOnly = false;
        _env.Runner.OnRun = (exe, args) =>
        {
            Assert.Equal("mysqld.exe", Path.GetFileName(exe));
            args0 = args;
            initFile = InitFile().Match(args).Groups[1].Value;
            contents = File.ReadAllText(initFile);
            secretAtInit = _env.Secrets.Get("mysql-root");
            aclUserOnly = IsUserOnly(initFile);
            Directory.CreateDirectory(Path.Combine(_env.Paths.MySqlData, "mysql"));
            return 0;
        };

        await MySqlBootstrap.EnsureInitializedAsync(
            _env.Paths, _env.Settings, _env.Runner, _env.Secrets, _env.MySqlDir, CancellationToken.None);

        var password = _env.Secrets.Get("mysql-root");
        Assert.NotNull(password);
        Assert.Equal(24, password.Length);
        Assert.Equal(password, secretAtInit);
        Assert.Contains("--initialize-insecure", args0, StringComparison.Ordinal);
        Assert.DoesNotContain(password, args0, StringComparison.Ordinal);
        Assert.Contains($"ALTER USER 'root'@'localhost' IDENTIFIED BY '{password}';", contents, StringComparison.Ordinal);
        Assert.True(aclUserOnly, "Le fichier d'init doit être réservé à l'utilisateur courant.");
        Assert.False(File.Exists(initFile));
        Assert.Single(_env.Runner.Runs);
        Assert.Empty(_env.Runner.Started);
        Assert.Empty(Directory.EnumerateFiles(_env.Paths.Tmp, "init-*.sql"));
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
        Assert.Empty(Directory.EnumerateFiles(_env.Paths.Tmp, "init-*.sql"));
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
