using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using StackR.Core.Paths;
using StackR.Core.Security;
using StackR.Core.Services;
using StackR.Core.Settings;

namespace StackR.Core.Tests.Services;

// Racine StackR temporaire (avec espace et accent), composants factices et ports libres.
internal sealed class ServiceTestEnvironment : IDisposable
{
    private readonly TempDirectory _temp = new();

    public ServiceTestEnvironment(bool mySqlInitialized = true)
    {
        Paths = new StackRPaths(Path.Combine(_temp.Path, "Dossier é", "StackR"));
        Paths.EnsureCreated();

        var ports = FreePorts(5);
        Settings = new StackRSettings
        {
            Root = Paths.Root,
            HttpPort = ports[0],
            HttpsPort = ports[1],
            MySqlPort = ports[2],
            MailpitUiPort = ports[3],
            MailpitSmtpPort = ports[4],
        };

        ApacheDir = Paths.ComponentDir("apache", "2.4.62");
        PhpDir = Paths.ComponentDir("php", "8.4.1");
        MySqlDir = Paths.ComponentDir("mysql", "8.4.3");
        MailpitDir = Paths.ComponentDir("mailpit", "1.21.0");
        Touch(Path.Combine(ApacheDir, "bin", "httpd.exe"));
        Directory.CreateDirectory(PhpDir);
        Touch(Path.Combine(MySqlDir, "bin", "mysqld.exe"));
        Touch(Path.Combine(MySqlDir, "bin", "mysqladmin.exe"));
        Touch(Path.Combine(MySqlDir, "bin", "mysql.exe"));
        Touch(Path.Combine(MailpitDir, "mailpit.exe"));
        if (mySqlInitialized)
        {
            Directory.CreateDirectory(Path.Combine(Paths.MySqlData, "mysql"));
        }

        Secrets = new SecretStore(Paths.Secrets);
        Runner.ListenOnStart["httpd.exe"] = Settings.HttpPort;
        Runner.ListenOnStart["mysqld.exe"] = Settings.MySqlPort;
        Runner.ListenOnStart["mailpit.exe"] = Settings.MailpitUiPort;
    }

    public StackRPaths Paths { get; }

    public StackRSettings Settings { get; }

    public FakeProcessRunner Runner { get; } = new();

    public SecretStore Secrets { get; }

    public string ApacheDir { get; }

    public string PhpDir { get; }

    public string MySqlDir { get; }

    public string MailpitDir { get; }

    public ApacheService Apache(Func<string?>? apacheDir = null) =>
        new(Paths, Settings, Runner, apacheDir ?? (() => ApacheDir), () => PhpDir, FakeRoots);

    // Jamais les vrais magasins de certificats dans les tests.
    internal static IEnumerable<X509Certificate2> FakeRoots() => [];

    public MySqlService MySql() => new(Paths, Settings, Runner, () => MySqlDir, Secrets);

    public MailpitService Mailpit() => new(Paths, Settings, Runner, () => MailpitDir);

    public ServiceManager Manager() => new(Paths, Runner, [Apache(), MySql(), Mailpit()]);

    public Dictionary<string, int> ReadState() =>
        File.Exists(Paths.StateFile)
            ? JsonSerializer.Deserialize<Dictionary<string, int>>(File.ReadAllText(Paths.StateFile))!
            : [];

    public void WriteState(Dictionary<string, int> state)
    {
        Directory.CreateDirectory(Paths.Tmp);
        File.WriteAllText(Paths.StateFile, JsonSerializer.Serialize(state));
    }

    public void Dispose()
    {
        Runner.Dispose();
        _temp.Dispose();
    }

    // Réserve n ports distincts en même temps, puis les libère.
    public static int[] FreePorts(int count)
    {
        var listeners = Enumerable.Range(0, count).Select(_ => new TcpListener(IPAddress.Loopback, 0)).ToList();
        try
        {
            foreach (var l in listeners)
            {
                l.Start();
            }

            return listeners.Select(l => ((IPEndPoint)l.LocalEndpoint).Port).ToArray();
        }
        finally
        {
            foreach (var l in listeners)
            {
                l.Stop();
            }
        }
    }

    public static async Task WaitUntilAsync(Func<bool> condition, int timeoutMs = 10000)
    {
        var deadline = Environment.TickCount64 + timeoutMs;
        while (!condition())
        {
            if (Environment.TickCount64 > deadline)
            {
                throw new TimeoutException("Condition non atteinte à temps.");
            }

            await Task.Delay(50);
        }
    }

    private static void Touch(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, []);
    }
}
