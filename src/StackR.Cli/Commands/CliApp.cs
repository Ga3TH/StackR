using System.CommandLine;
using StackR.Core;
using StackR.Core.Hosts;

namespace StackR.Cli.Commands;

/// <summary>Construction et exécution des commandes <c>stackr</c>. Codes de sortie : 0 ok, 1 erreur, 2 arguments invalides, 5 droits insuffisants.</summary>
internal static class CliApp
{
    /// <summary>Point d'entrée réel : moteur créé à la demande depuis <c>%LOCALAPPDATA%\StackR\stackr.json</c>.</summary>
    public static Task<int> MainAsync(string[] args)
    {
        var context = new CliContext(() => StackREngine.Create(), Console.Out, Console.Error)
        {
            Interactive = !Console.IsOutputRedirected,
            IsElevated = () => Elevation.IsElevated,
            WriteHosts = hosts => HostsFile.Write(hosts),
            OwnsEngine = true,
        };
        return RunAsync(args, context);
    }

    /// <summary>Exécute une commande sur un moteur existant (tests, intégration) ; sorties et erreurs dans <paramref name="output"/>.</summary>
    internal static Task<int> RunAsync(string[] args, StackREngine engine, TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(engine);
        var context = new CliContext(() => engine, output, output)
        {
            IsElevated = () => engine.IsElevated,
            WriteHosts = engine.WriteHostsFile,
        };
        return RunAsync(args, context);
    }

    internal static async Task<int> RunAsync(string[] args, CliContext context)
    {
        ArgumentNullException.ThrowIfNull(args);
        ArgumentNullException.ThrowIfNull(context);
        using (context)
        {
            var root = BuildRoot(context);
            var configuration = new InvocationConfiguration { Output = context.Out, Error = context.Error };
            return await root.Parse(args).InvokeAsync(configuration).ConfigureAwait(false);
        }
    }

    internal static RootCommand BuildRoot(CliContext ctx) => new("StackR : environnement de développement web local (Apache, PHP, MySQL, Mailpit).")
    {
        ServiceCommands.Status(ctx),
        ServiceCommands.Start(ctx),
        ServiceCommands.Stop(ctx),
        ServiceCommands.Restart(ctx),
        SiteCommands.Sites(ctx),
        ComponentCommands.Versions(ctx),
        ComponentCommands.Install(ctx),
        ComponentCommands.Use(ctx),
        SiteCommands.Hosts(ctx),
        SecurityCommands.Database(ctx),
        SecurityCommands.CertificateAuthority(ctx),
    };

    /// <summary>Toute erreur devient un message français et le code 1 (jamais de trace de pile).</summary>
    internal static Func<ParseResult, CancellationToken, Task<int>> Guarded(
        CliContext ctx, Func<ParseResult, CancellationToken, Task<int>> body) => async (parseResult, ct) =>
        {
            try
            {
                return await body(parseResult, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                ctx.Error.WriteLine("Opération annulée.");
                return ExitCodes.Error;
            }
#pragma warning disable CA1031 // Frontière du processus : toute erreur est présentée à l'utilisateur.
            catch (Exception ex)
#pragma warning restore CA1031
            {
                ctx.Error.WriteLine("Erreur : " + ex.Message);
                return ExitCodes.Error;
            }
        };
}
