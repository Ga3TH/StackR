using System.CommandLine;
using StackR.Core;
using StackR.Core.Components;
using StackR.Core.Manifests;

namespace StackR.Cli.Commands;

/// <summary><c>versions</c>, <c>install</c>, <c>use</c>, <c>uninstall</c>.</summary>
internal static class ComponentCommands
{
    public static Command Versions(CliContext ctx)
    {
        var component = new Argument<string?>("component")
        {
            Description = "apache, php, mysql, mailpit, node ou composer (tous si absent).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var command = new Command("versions", "Liste les versions stables du catalogue (installée, active, recommandée).") { component };
        command.SetAction(CliApp.Guarded(ctx, (pr, _) => Task.FromResult(PrintVersions(ctx, pr.GetValue(component)))));
        return command;
    }

    public static Command Install(CliContext ctx)
    {
        var component = new Argument<string>("component") { Description = "apache, php, mysql, mailpit, node ou composer." };
        var version = new Argument<string?>("version")
        {
            Description = "Version du catalogue (par défaut : la recommandée).",
            Arity = ArgumentArity.ZeroOrOne,
        };
        var command = new Command("install", "Télécharge (empreinte SHA-256 vérifiée) et installe un composant.") { component, version };
        command.SetAction(CliApp.Guarded(ctx, (pr, ct) => InstallAsync(ctx, pr.GetValue(component), pr.GetValue(version), ct)));
        return command;
    }

    public static Command Use(CliContext ctx)
    {
        var component = new Argument<string>("component") { Description = "php, apache, mysql, mailpit, node ou composer." };
        var version = new Argument<string>("version") { Description = "Version installée à activer." };
        var command = new Command("use", "Active une version installée (ex. : stackr use php 8.3.37).") { component, version };
        command.SetAction(CliApp.Guarded(ctx, (pr, _) => UseAsync(ctx, pr.GetValue(component), pr.GetValue(version)!)));
        return command;
    }

    public static Command Uninstall(CliContext ctx)
    {
        var component = new Argument<string>("component") { Description = "apache, php, mysql, mailpit, node ou composer." };
        var version = new Argument<string>("version") { Description = "Version installée à supprimer (pas la version active)." };
        var command = new Command("uninstall", "Supprime une version installée qui n'est pas active (service arrêté).") { component, version };
        command.SetAction(CliApp.Guarded(ctx, (pr, _) => UninstallAsync(ctx, pr.GetValue(component), pr.GetValue(version)!)));
        return command;
    }

    private static int PrintVersions(CliContext ctx, string? filter)
    {
        ComponentKind[] kinds;
        if (filter is null)
        {
            kinds = Enum.GetValues<ComponentKind>();
        }
        else if (Names.TryParseComponent(filter, out var kind))
        {
            kinds = [kind];
        }
        else
        {
            ctx.Out.WriteLine(Names.UnknownComponent(filter));
            return ExitCodes.Error;
        }

        var engine = ctx.Engine;
        foreach (var kind in kinds)
        {
            var installed = engine.Store.Installed(kind);
            var active = engine.ActiveDirectory(kind) is null ? null : engine.ActiveVersion(kind);
            var recommended = engine.Manifest.Recommended(kind)?.Version;
            var catalog = engine.Manifest.For(kind).Select(r => r.Version).ToList();
            var versions = catalog
                .Concat(installed.Where(v => !catalog.Contains(v, StringComparer.OrdinalIgnoreCase)))
                .OrderByDescending(ParseVersion)
                .ToList();

            ctx.Out.WriteLine(StackREngine.DisplayName(kind));
            if (versions.Count == 0)
            {
                ctx.Out.WriteLine("  (aucune version au catalogue)");
            }

            var width = versions.Count == 0 ? 0 : versions.Max(v => v.Length);
            foreach (var v in versions)
            {
                var marks = new List<string>();
                if (installed.Contains(v, StringComparer.OrdinalIgnoreCase))
                {
                    marks.Add(catalog.Contains(v) ? "Installée" : "Installée (hors catalogue)");
                }

                if (string.Equals(v, active, StringComparison.OrdinalIgnoreCase))
                {
                    marks.Add("Active");
                }

                if (string.Equals(v, recommended, StringComparison.Ordinal))
                {
                    marks.Add("Recommandée");
                }

                ctx.Out.WriteLine(("  " + v.PadRight(width) + "  " + string.Join(", ", marks)).TrimEnd());
            }

            ctx.Out.WriteLine();
        }

        return ExitCodes.Ok;
    }

    private static async Task<int> InstallAsync(CliContext ctx, string? componentText, string? version, CancellationToken ct)
    {
        if (!Names.TryParseComponent(componentText, out var kind))
        {
            ctx.Out.WriteLine(Names.UnknownComponent(componentText));
            return ExitCodes.Error;
        }

        var engine = ctx.Engine;
        var name = StackREngine.DisplayName(kind);
        var release = version is null
            ? engine.Manifest.Recommended(kind)
            : engine.Manifest.For(kind).FirstOrDefault(r => string.Equals(r.Version, version, StringComparison.OrdinalIgnoreCase));
        if (release is null)
        {
            var available = string.Join(", ", engine.Manifest.For(kind).Select(r => r.Version));
            ctx.Out.WriteLine(version is null
                ? $"Aucune version de {name} au catalogue."
                : $"Version {CliContext.Sanitize(version)} introuvable pour {name}. Versions disponibles : {(available.Length == 0 ? "aucune" : available)}.");
            return ExitCodes.Error;
        }

        if (release.Requires.Contains("vcredist-x64", StringComparer.OrdinalIgnoreCase) && !Prerequisites.HasVcRedistX64())
        {
            ctx.Out.WriteLine($"Attention : {name} nécessite le runtime Visual C++ x64 (14.50 ou plus récent) : {Prerequisites.VcRedistUrl}");
        }

        var already = engine.Store.IsInstalled(kind, release.Version);
        ctx.Out.WriteLine(already
            ? $"{name} {release.Version} est déjà installé."
            : $"Installation de {name} {release.Version}...");

        var bar = new TextProgressBar(ctx.Out, ctx.Interactive);
        ApplyResult result;
        try
        {
            result = await engine.InstallAsync(release, already ? null : bar, ct).ConfigureAwait(false);
        }
        finally
        {
            bar.Complete();
        }

        ctx.PrintWarnings(result);
        var active = string.Equals(engine.ActiveVersion(kind), release.Version, StringComparison.OrdinalIgnoreCase);
        if (!already)
        {
            ctx.Out.WriteLine($"{name} {release.Version} installé{(active ? " (version active)" : $". Pour l'activer : stackr use {kind.ToString().ToLowerInvariant()} {release.Version}")}.");
        }

        return ExitCodes.Ok;
    }

    private static async Task<int> UseAsync(CliContext ctx, string? componentText, string version)
    {
        if (!Names.TryParseComponent(componentText, out var kind))
        {
            ctx.Out.WriteLine(Names.UnknownComponent(componentText));
            return ExitCodes.Error;
        }

        var engine = ctx.Engine;
        var name = StackREngine.DisplayName(kind);
        bool installed;
        try
        {
            installed = engine.Store.IsInstalled(kind, version);
        }
        catch (ArgumentException)
        {
            installed = false;
        }

        if (!installed)
        {
            ctx.Out.WriteLine($"{name} {CliContext.Sanitize(version)} n'est pas installé (stackr install {kind.ToString().ToLowerInvariant()} {CliContext.Sanitize(version)}).");
            return ExitCodes.Error;
        }

        var result = await engine.UseAsync(kind, version).ConfigureAwait(false);
        ctx.PrintWarnings(result);
        ctx.Out.WriteLine($"{name} {version} est maintenant la version active.");
        return ExitCodes.Ok;
    }

    private static async Task<int> UninstallAsync(CliContext ctx, string? componentText, string version)
    {
        if (!Names.TryParseComponent(componentText, out var kind))
        {
            ctx.Out.WriteLine(Names.UnknownComponent(componentText));
            return ExitCodes.Error;
        }

        var engine = ctx.Engine;
        var name = StackREngine.DisplayName(kind);
        bool installed;
        try
        {
            installed = engine.Store.IsInstalled(kind, version);
        }
        catch (ArgumentException)
        {
            installed = false;
        }

        if (!installed)
        {
            ctx.Out.WriteLine($"{name} {CliContext.Sanitize(version)} n'est pas installé.");
            return ExitCodes.Error;
        }

        try
        {
            await engine.UninstallAsync(kind, version).ConfigureAwait(false);
        }
        catch (InvalidOperationException ex)
        {
            ctx.Out.WriteLine(ex.Message);
            return ExitCodes.Error;
        }

        ctx.Out.WriteLine($"{name} {version} désinstallé.");
        return ExitCodes.Ok;
    }

    private static Version ParseVersion(string v) => System.Version.TryParse(v, out var parsed) ? parsed : new Version(0, 0);
}
