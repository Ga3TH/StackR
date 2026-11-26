using System.Net;
using Microsoft.Win32;
using StackR.Core.Components;
using StackR.Core.Manifests;
using StackR.Core.Paths;
using StackR.Core.Settings;
using StackR.Core.Tests.Services;

namespace StackR.Core.Tests;

// Environnement complet du moteur, sans aucun état système réel : racine temporaire (espace + accent),
// fichier hosts temporaire, clé de registre de test, UAC simulée, magasin de certificats jamais modifié.
internal sealed class EngineTestEnvironment : IDisposable
{
    private readonly TempDirectory _temp = new();
    private readonly string _keyPath = @"Software\StackR.Tests\" + Guid.NewGuid().ToString("N");

    public EngineTestEnvironment()
    {
        Paths = new StackRPaths(Path.Combine(_temp.Path, "Dossier é", "StackR"));
        SettingsPath = Path.Combine(_temp.Path, "profil", "stackr.json");
        HostsPath = Path.Combine(_temp.Path, "hosts");
        File.WriteAllText(HostsPath, "127.0.0.1 localhost\r\n");
        InstallDir = Path.Combine(_temp.Path, "Programmes é", "StackR");
        Directory.CreateDirectory(InstallDir);
        File.WriteAllBytes(Path.Combine(InstallDir, "stackr.exe"), []);
        RegistryRoot = Registry.CurrentUser.CreateSubKey(_keyPath);

        var ports = ServiceTestEnvironment.FreePorts(5);
        Settings = new StackRSettings
        {
            Root = Paths.Root,
            HttpPort = ports[0],
            HttpsPort = ports[1],
            MySqlPort = ports[2],
            MailpitUiPort = ports[3],
            MailpitSmtpPort = ports[4],
        };
    }

    public StackRPaths Paths { get; }

    public string SettingsPath { get; }

    public string HostsPath { get; }

    public string InstallDir { get; }

    public RegistryKey RegistryRoot { get; }

    public StackRSettings Settings { get; set; }

    public FakeProcessRunner Runner { get; } = new();

    public List<string[]> ElevatedCalls { get; } = [];

    public int ElevatedResult { get; set; }

    // Remplace la relance élevée simulée (après enregistrement dans ElevatedCalls).
    public Func<string[], Task<int>>? ElevatedHandler { get; set; }

    public bool Elevated { get; set; }

    public HttpMessageHandler? Handler { get; set; }

    public Manifest? Manifest { get; set; }

    public EngineOptions Options => new()
    {
        HostsPath = HostsPath,
        RunElevated = args =>
        {
            lock (ElevatedCalls)
            {
                ElevatedCalls.Add(args);
            }

            return ElevatedHandler?.Invoke(args) ?? Task.FromResult(ElevatedResult);
        },
        IsElevated = () => Elevated,
        UserRegistryRoot = RegistryRoot,
        InstallDirectory = InstallDir,
        ManageCertificateStore = false,
        ProcessRunner = Runner,
        HttpClient = Handler is null ? null : new HttpClient(Handler),
        Manifest = Manifest,
    };

    // Écrit stackr.json (jamais de racine par défaut C:\StackR dans les tests) puis crée le moteur.
    public StackREngine CreateEngine()
    {
        SettingsStore.Save(SettingsPath, Settings);
        return StackREngine.Create(SettingsPath, Options);
    }

    public void Activate(string folder, string version) => Settings.ActiveVersions[folder] = version;

    // Composant factice installé (marqueur compris).
    public string InstallFake(string folder, string version)
    {
        var dir = Paths.ComponentDir(folder, version);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, ComponentStore.MarkerFileName), "{}");
        return dir;
    }

    public string AddSite(string folder)
    {
        var dir = Path.Combine(Paths.Www, folder);
        Directory.CreateDirectory(dir);
        return dir;
    }

    public string? ReadUserPath()
    {
        using var env = RegistryRoot.OpenSubKey("Environment");
        return env?.GetValue("Path", null, RegistryValueOptions.DoNotExpandEnvironmentNames) as string;
    }

    public void Dispose()
    {
        Runner.Dispose();
        RegistryRoot.Dispose();
        Registry.CurrentUser.DeleteSubKeyTree(_keyPath, throwOnMissingSubKey: false);
        _temp.Dispose();
    }

    // Serveur HTTP factice : renvoie toujours le même contenu.
    internal sealed class BytesHandler(byte[] payload) : HttpMessageHandler
    {
        public int Requests { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                RequestMessage = request,
                Content = new ByteArrayContent(payload),
            });
        }
    }
}
