using System.CommandLine;
using System.Globalization;
using StackR.Core.Hosts;
using StackR.Core.Settings;

namespace StackR.Cli.Commands;

/// <summary><c>sites</c> et <c>hosts sync</c>.</summary>
internal static class SiteCommands
{
    public static Command Sites(CliContext ctx)
    {
        var command = new Command("sites", "Liste les sites .test détectés dans le dossier www.");
        command.SetAction(CliApp.Guarded(ctx, (_, _) => Task.FromResult(PrintSites(ctx))));
        return command;
    }

    public static Command Hosts(CliContext ctx)
    {
        var names = new Option<string?>("--names")
        {
            Description = "Noms .test séparés par des virgules (transmis par StackR lors de la relance élevée).",
        };
        var clear = new Option<bool>("--clear") { Description = "Vide le bloc StackR du fichier hosts." };
        var sync = new Command(
            "sync",
            "Met à jour le bloc StackR du fichier hosts (droits administrateur requis). Sans option : sites du dossier www.")
        {
            names,
            clear,
        };
        sync.SetAction(CliApp.Guarded(ctx, (pr, _) => Task.FromResult(Sync(ctx, pr.GetValue(names), pr.GetValue(clear)))));
        return new Command("hosts", "Fichier hosts de Windows.") { sync };
    }

    private static int PrintSites(CliContext ctx)
    {
        var engine = ctx.Engine;
        var sites = engine.Sites.Scan();
        if (sites.Count == 0)
        {
            ctx.Out.WriteLine($"Aucun site dans {engine.Settings.EffectiveWww(engine.Paths)}.");
            return ExitCodes.Ok;
        }

        var settings = engine.Settings;
        var table = new TextTable("Nom", "URL", "Racine");
        foreach (var site in sites)
        {
            var url = settings.HttpsEnabled
                ? Url("https", site.Name.Host, settings.HttpsPort, 443)
                : Url("http", site.Name.Host, settings.HttpPort, 80);
            table.Add(site.Name.Value, url, site.DocumentRoot);
        }

        table.WriteTo(ctx.Out);
        return ExitCodes.Ok;
    }

    private static string Url(string scheme, string host, int port, int defaultPort) =>
        port == defaultPort
            ? $"{scheme}://{host}"
            : string.Create(CultureInfo.InvariantCulture, $"{scheme}://{host}:{port}");

    /// <summary>
    /// Les noms reçus sont tous validés (<c>&lt;nom&gt;.test</c>) avant toute écriture : seul un bloc de lignes
    /// <c>127.0.0.1 &lt;nom&gt;.test</c> peut être produit, quel que soit l'appelant.
    /// </summary>
    private static int Sync(CliContext ctx, string? names, bool clear)
    {
        if (names is not null && clear)
        {
            ctx.Out.WriteLine("Les options --names et --clear sont incompatibles.");
            return ExitCodes.InvalidArguments;
        }

        List<string>? hosts = null;
        if (clear)
        {
            hosts = [];
        }
        else if (names is not null)
        {
            hosts = [.. names.Split(',')];
            var invalid = hosts.FirstOrDefault(h => !HostsBlock.IsValidHost(h));
            if (invalid is not null)
            {
                ctx.Out.WriteLine($"Nom d'hôte refusé : « {CliContext.Sanitize(invalid)} » (attendu : <nom>.test en minuscules). Fichier hosts inchangé.");
                return ExitCodes.InvalidArguments;
            }
        }

        if (!ctx.IsElevated())
        {
            ctx.Out.WriteLine("Droits administrateur requis : « stackr hosts sync » modifie le fichier hosts de Windows. "
                + "StackR le relance automatiquement via l'invite UAC ; sinon, exécutez-le depuis un terminal administrateur.");
            return ExitCodes.NotElevated;
        }

        // Sans option (usage manuel depuis un terminal administrateur) : sites du dossier www de ce compte.
        hosts ??= [.. ctx.Engine.Sites.Scan().Select(s => s.Name.Host)];
        ctx.WriteHosts(hosts);
        ctx.Out.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Fichier hosts à jour ({hosts.Distinct().Count()} site(s))."));
        return ExitCodes.Ok;
    }
}
