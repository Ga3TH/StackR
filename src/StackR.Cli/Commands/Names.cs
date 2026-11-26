using StackR.Core.Manifests;

namespace StackR.Cli.Commands;

/// <summary>Noms de composants et de services acceptés sur la ligne de commande.</summary>
internal static class Names
{
    public static readonly string[] ServiceIds = ["apache", "mysql", "mailpit"];

    public static bool TryParseComponent(string? text, out ComponentKind kind)
    {
        kind = default;
        switch (text?.Trim().ToLowerInvariant())
        {
            case "apache" or "httpd":
                kind = ComponentKind.Apache;
                return true;
            case "php":
                kind = ComponentKind.Php;
                return true;
            case "mysql":
                kind = ComponentKind.MySql;
                return true;
            case "mailpit":
                kind = ComponentKind.Mailpit;
                return true;
            case "node" or "nodejs" or "node.js":
                kind = ComponentKind.Node;
                return true;
            case "composer":
                kind = ComponentKind.Composer;
                return true;
            default:
                return false;
        }
    }

    public static string UnknownComponent(string? text) =>
        $"Composant inconnu : « {CliContext.Sanitize(text)} » (attendu : apache, php, mysql, mailpit, node, composer).";

    public static string UnknownService(string? text) =>
        $"Service inconnu : « {CliContext.Sanitize(text)} » (attendu : {string.Join(", ", ServiceIds)}).";

    /// <summary>Composant dont dépend un service (pour savoir s'il est installé).</summary>
    public static ComponentKind ComponentOf(string serviceId) => serviceId switch
    {
        "apache" => ComponentKind.Apache,
        "mysql" => ComponentKind.MySql,
        "mailpit" => ComponentKind.Mailpit,
        _ => throw new ArgumentOutOfRangeException(nameof(serviceId), serviceId, null),
    };
}
