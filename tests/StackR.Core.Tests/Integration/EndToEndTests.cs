using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using StackR.Core.Manifests;
using StackR.Core.Services;

namespace StackR.Core.Tests.Integration;

// Test de bout en bout avec les VRAIS composants (≈ 350 Mo téléchargés depuis les sources officielles) :
// installation vérifiée, configuration, démarrage, page PHP en HTTP et HTTPS, PDO MySQL, mail() → Mailpit, arrêt.
// Racine temporaire avec espace et accent, ports libres aléatoires, hosts / registre / magasin de certificats simulés.
// Lancement : $env:STACKR_E2E = "1"; dotnet test -c Release --filter "Category=Integration"
[Trait("Category", "Integration")]
public sealed class EndToEndTests
{
    private static readonly ComponentKind[] Stack = [ComponentKind.Apache, ComponentKind.Php, ComponentKind.MySql, ComponentKind.Mailpit];

    [Fact]
    public async Task FullStack_InstallStartServeStop_Works()
    {
        if (Environment.GetEnvironmentVariable("STACKR_E2E") != "1")
        {
            return;
        }

        using var env = new EngineTestEnvironment();
        var site = env.AddSite("demo");
        Directory.CreateDirectory(Path.Combine(site, "public"));
        File.WriteAllText(Path.Combine(site, "public", "index.php"), "<?php echo 'StackR-OK ' . PHP_VERSION;");

        // Composants réels : processus réels, téléchargements réels (le reste reste simulé).
        using var engine = StackREngineFactory(env);

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(20));
        foreach (var kind in Stack)
        {
            var release = engine.Manifest.Recommended(kind) ?? throw new InvalidOperationException($"Pas de version recommandée pour {kind}.");
            await engine.InstallAsync(release, null, timeout.Token);
            Assert.True(engine.Store.IsInstalled(kind, release.Version), $"{kind} {release.Version} non installé.");
        }

        var applied = await engine.ApplyConfigurationAsync();
        Assert.Contains(env.ElevatedCalls, call => string.Join(' ', call).Contains("demo.test", StringComparison.Ordinal));

        var report = await engine.StartAllAsync(timeout.Token);
        try
        {
            Assert.True(report.Success, "Démarrage : " + string.Join(" | ", report.Errors) + " | " + string.Join(" | ", applied.Warnings)
                + " | " + string.Join(" | ", engine.Services.Services.Select(s => $"{s.Id}={s.Status} {s.LastError}")));
            Assert.All(engine.Services.Services, s => Assert.Equal(ServiceStatus.Running, s.Status));

            // PDO MySQL avec le mot de passe root généré (fichier temporaire de test).
            var password = engine.GetMySqlRootPassword();
            Assert.False(string.IsNullOrEmpty(password));
            File.WriteAllText(Path.Combine(site, "public", "db.php"),
                "<?php $p = new PDO('mysql:host=127.0.0.1;port=" + env.Settings.MySqlPort + "', 'root', base64_decode('"
                + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password!)) + "'));"
                + " echo 'DB-OK ' . $p->query('SELECT VERSION()')->fetchColumn();");
            File.WriteAllText(Path.Combine(site, "public", "mail.php"),
                "<?php echo mail('dev@demo.test', 'Bonjour StackR', 'Message de test') ? 'MAIL-OK' : 'MAIL-KO';");

            using var http = new HttpClient(new SocketsHttpHandler
            {
                // Résolution « demo.test » → 127.0.0.1 sans toucher au fichier hosts.
                ConnectCallback = ConnectToLoopbackAsync,
                SslOptions = new SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, cert, _, _) => ChainsToStackRCa(engine, cert),
                },
            })
            { Timeout = TimeSpan.FromSeconds(30) };

            var page = await http.GetStringAsync($"http://demo.test:{env.Settings.HttpPort}/");
            Assert.StartsWith("StackR-OK 8.", page, StringComparison.Ordinal);

            var securePage = await http.GetStringAsync($"https://demo.test:{env.Settings.HttpsPort}/");
            Assert.StartsWith("StackR-OK", securePage, StringComparison.Ordinal);

            var db = await http.GetStringAsync($"http://demo.test:{env.Settings.HttpPort}/db.php");
            Assert.True(db.StartsWith("DB-OK 8.", StringComparison.Ordinal), "db.php : " + db);

            var mail = await http.GetStringAsync($"http://demo.test:{env.Settings.HttpPort}/mail.php");
            Assert.True(mail == "MAIL-OK", "mail.php : " + mail);

            var messages = await http.GetStringAsync($"http://127.0.0.1:{env.Settings.MailpitUiPort}/api/v1/messages");
            using var json = JsonDocument.Parse(messages);
            Assert.True(json.RootElement.GetProperty("total").GetInt32() >= 1, "Mailpit n'a reçu aucun message.");

            // « php » dans un terminal (shim) : même php.ini, donc mêmes extensions que sous Apache.
            var cli = await RunCmdAsync(Path.Combine(env.Paths.Shims, "php.cmd"),
                "-r \"echo implode(',', array_map(fn($e) => extension_loaded($e) ? 1 : 0, ['pdo_mysql','mbstring','openssl','curl','zip','fileinfo','intl']));\"");
            Assert.True(cli == "1,1,1,1,1,1,1", "php.cmd : " + cli);

            // Écoute uniquement sur 127.0.0.1 : aucune connexion possible par une autre adresse locale.
            foreach (var port in new[] { env.Settings.HttpPort, env.Settings.MySqlPort, env.Settings.MailpitUiPort })
            {
                Assert.False(await AcceptsOnNonLoopbackAsync(port), $"Le port {port} écoute ailleurs que sur 127.0.0.1.");
            }
        }
        finally
        {
            await engine.StopAllAsync(CancellationToken.None);
        }

        Assert.All(engine.Services.Services, s => Assert.Equal(ServiceStatus.Stopped, s.Status));
    }

    private static StackREngine StackREngineFactory(EngineTestEnvironment env)
    {
        StackR.Core.Settings.SettingsStore.Save(env.SettingsPath, env.Settings);
        return StackREngine.Create(env.SettingsPath, env.Options with { ProcessRunner = null, HttpClient = null, Manifest = null });
    }

    private static async ValueTask<Stream> ConnectToLoopbackAsync(SocketsHttpConnectionContext context, CancellationToken ct)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
        try
        {
            await socket.ConnectAsync(new IPEndPoint(IPAddress.Loopback, context.DnsEndPoint.Port), ct);
            return new NetworkStream(socket, ownsSocket: true);
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    // Le certificat du site doit être émis par la CA locale de StackR (sans l'ajouter au magasin Windows).
    private static bool ChainsToStackRCa(StackREngine engine, X509Certificate? certificate)
    {
        if (certificate is null)
        {
            return false;
        }

        using var root = engine.Ca.EnsureRootCa();
        using var leaf = new X509Certificate2(certificate);
        using var chain = new X509Chain();
        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        chain.ChainPolicy.CustomTrustStore.Add(root);
        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        return chain.Build(leaf) && leaf.MatchesHostname("demo.test");
    }

    private static async Task<string> RunCmdAsync(string script, string args)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe", $"/d /c \"\"{script}\" {args}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return ((await stdout).Trim() + " " + (await stderr).Trim()).Trim();
    }

    private static async Task<bool> AcceptsOnNonLoopbackAsync(int port)
    {
        var addresses = (await Dns.GetHostAddressesAsync(Dns.GetHostName()))
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
        foreach (var address in addresses)
        {
            using var client = new TcpClient();
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await client.ConnectAsync(address, port, cts.Token);
                return true;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                // Refusé : attendu.
            }
        }

        return false;
    }
}
