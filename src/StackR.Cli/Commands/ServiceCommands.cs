using System.CommandLine;
using System.Globalization;
using StackR.Core;
using StackR.Core.Services;

namespace StackR.Cli.Commands;

/// <summary><c>status</c>, <c>start</c>, <c>stop</c>, <c>restart</c>.</summary>
internal static class ServiceCommands
{
    public static Command Status(CliContext ctx)
    {
        var command = new Command("status", "Affiche l'état des services (Apache, MySQL, Mailpit).");
        command.SetAction(CliApp.Guarded(ctx, (_, _) => Task.FromResult(PrintStatus(ctx))));
        return command;
    }

    public static Command Start(CliContext ctx)
    {
        var service = ServiceArgument();
        var command = new Command("start", "Démarre un service, ou tous les services installés.") { service };
        command.SetAction(CliApp.Guarded(ctx, (pr, ct) => StartAsync(ctx, pr.GetValue(service), ct)));
        return command;
    }

    public static Command Stop(CliContext ctx)
    {
        var service = ServiceArgument();
        var command = new Command("stop", "Arrête un service, ou tous les services.") { service };
        command.SetAction(CliApp.Guarded(ctx, (pr, ct) => StopAsync(ctx, pr.GetValue(service), ct)));
        return command;
    }

    public static Command Restart(CliContext ctx)
    {
        var service = ServiceArgument();
        var command = new Command("restart", "Redémarre un service, ou tous les services installés.") { service };
        command.SetAction(CliApp.Guarded(ctx, async (pr, ct) =>
        {
            var code = await StopAsync(ctx, pr.GetValue(service), ct).ConfigureAwait(false);
            return code != ExitCodes.Ok ? code : await StartAsync(ctx, pr.GetValue(service), ct).ConfigureAwait(false);
        }));
        return command;
    }

    internal static string StatusText(ServiceStatus status) => status switch
    {
        ServiceStatus.Stopped => "Arrêté",
        ServiceStatus.Starting => "Démarrage",
        ServiceStatus.Running => "Actif",
        ServiceStatus.Stopping => "Arrêt en cours",
        ServiceStatus.Error => "Erreur",
        _ => status.ToString(),
    };

    private static Argument<string?> ServiceArgument() => new("service")
    {
        Description = "apache, mysql ou mailpit (tous si absent).",
        Arity = ArgumentArity.ZeroOrOne,
    };

    private static int PrintStatus(CliContext ctx)
    {
        var engine = ctx.Engine;
        engine.EnsureReconciled();

        var table = new TextTable("Service", "État", "Version", "Ports");
        foreach (var service in engine.Services.Services)
        {
            var kind = Names.ComponentOf(service.Id);
            var version = engine.ActiveDirectory(kind) is null ? "non installé" : engine.ActiveVersion(kind)!;
            table.Add(service.DisplayName, StatusText(service.Status), version, Ports(service));
        }

        table.WriteTo(ctx.Out);
        ctx.Out.WriteLine();
        var php = engine.ActiveDirectory(Core.Manifests.ComponentKind.Php) is null ? "aucun" : engine.ActiveVersion(Core.Manifests.ComponentKind.Php);
        ctx.Out.WriteLine($"PHP actif : {php}");
        ctx.Out.WriteLine($"Dossier www : {Core.Settings.SettingsExtensions.EffectiveWww(engine.Settings, engine.Paths)}");
        PrintErrors(ctx, engine.Services.Services);
        return ExitCodes.Ok;
    }

    private static async Task<int> StartAsync(CliContext ctx, string? id, CancellationToken ct)
    {
        if (!TryResolve(ctx, id, out var targets))
        {
            return ExitCodes.Error;
        }

        var engine = ctx.Engine;
        engine.EnsureReconciled();
        var installed = targets.Where(s => engine.ActiveDirectory(Names.ComponentOf(s.Id)) is not null).ToList();
        foreach (var skipped in targets.Except(installed))
        {
            ctx.Out.WriteLine(id is null
                ? $"{skipped.DisplayName} : ignoré (non installé)."
                : $"{skipped.DisplayName} n'est pas installé (stackr install {skipped.Id}).");
        }

        if (installed.Count == 0)
        {
            if (id is null)
            {
                ctx.Out.WriteLine("Aucun service installé (stackr install apache, php, mysql, mailpit).");
            }

            return ExitCodes.Error;
        }

        // Vhosts et certificats des nouveaux sites, hosts (UAC si nécessaire) avant le démarrage.
        ctx.PrintWarnings(await engine.ApplyConfigurationAsync().ConfigureAwait(false));

        List<string> errors;
        if (installed.Count == engine.Services.Services.Count)
        {
            errors = [.. (await engine.Services.StartAllAsync(ct).ConfigureAwait(false)).Errors];
        }
        else
        {
            // Même règle que StartAllAsync : si un port est pris, aucun service n'est démarré.
            var toStart = installed.Where(s => s.Status is not (ServiceStatus.Running or ServiceStatus.Starting)).ToList();
            errors = [.. toStart.SelectMany(s => s.Ports).Distinct().Select(PortProbe.DescribeConflict).OfType<string>()];
            if (errors.Count == 0)
            {
                foreach (var service in toStart)
                {
                    await service.StartAsync(ct).ConfigureAwait(false);
                }
            }
        }

        foreach (var error in errors)
        {
            ctx.Out.WriteLine("Erreur : " + error);
        }

        PrintLines(ctx, installed);
        return errors.Count == 0 && installed.All(s => s.Status == ServiceStatus.Running) ? ExitCodes.Ok : ExitCodes.Error;
    }

    private static async Task<int> StopAsync(CliContext ctx, string? id, CancellationToken ct)
    {
        if (!TryResolve(ctx, id, out var targets))
        {
            return ExitCodes.Error;
        }

        var engine = ctx.Engine;
        engine.EnsureReconciled();
        if (id is null)
        {
            await engine.Services.StopAllAsync(ct).ConfigureAwait(false);
        }
        else
        {
            await targets[0].StopAsync(ct).ConfigureAwait(false);
        }

        PrintLines(ctx, targets);
        return targets.Any(s => s.Status == ServiceStatus.Error) ? ExitCodes.Error : ExitCodes.Ok;
    }

    private static bool TryResolve(CliContext ctx, string? id, out List<ManagedService> targets)
    {
        var all = ctx.Engine.Services.Services;
        if (id is null)
        {
            targets = [.. all];
            return true;
        }

        var normalized = id.Trim().ToLowerInvariant();
        targets = [.. all.Where(s => s.Id == normalized)];
        if (targets.Count == 0)
        {
            ctx.Out.WriteLine(Names.UnknownService(id));
            return false;
        }

        return true;
    }

    private static void PrintLines(CliContext ctx, IEnumerable<ManagedService> services)
    {
        foreach (var service in services)
        {
            var ports = service.Status == ServiceStatus.Running ? $" (ports {Ports(service)})" : "";
            ctx.Out.WriteLine($"{service.DisplayName} : {StatusText(service.Status).ToLower(CultureInfo.CurrentCulture)}{ports}");
        }

        PrintErrors(ctx, services);
    }

    private static void PrintErrors(CliContext ctx, IEnumerable<ManagedService> services)
    {
        foreach (var service in services.Where(s => s.Status == ServiceStatus.Error && s.LastError is not null))
        {
            ctx.Out.WriteLine($"{service.DisplayName} : {service.LastError}");
        }
    }

    private static string Ports(ManagedService service) =>
        string.Join(", ", service.Ports.Select(p => p.ToString(CultureInfo.InvariantCulture)));
}
