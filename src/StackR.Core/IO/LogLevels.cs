namespace StackR.Core.IO;

/// <summary>Gravité d'une ligne de journal, pour la coloration.</summary>
public enum LogLevel
{
    Info,
    Notice,
    Warning,
    Error,
}

/// <summary>
/// Reconnaît la gravité d'une ligne des journaux Apache (<c>[php:error]</c>, <c>[core:warn]</c>), PHP
/// (<c>PHP Fatal error</c>, <c>PHP Warning</c>), MySQL (<c>[ERROR]</c>, <c>[Warning]</c>) et Mailpit (<c>level=error</c>).
/// </summary>
public static class LogLevels
{
    private static readonly string[] ErrorMarkers =
    [
        ":error]", ":crit]", ":alert]", ":emerg]", "[error]", "[crit]", "[alert]", "[emerg]",
        "php fatal error", "php parse error", "uncaught", "level=error", "level=fatal",
    ];

    private static readonly string[] WarningMarkers =
    [
        ":warn]", "[warn]", "[warning]", "php warning", "level=warn", "level=warning",
    ];

    private static readonly string[] NoticeMarkers =
    [
        ":notice]", "[notice]", "php notice", "php deprecated", "[system]", "level=info",
    ];

    public static LogLevel Classify(string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return LogLevel.Info;
        }

        var lower = line.ToLowerInvariant();
        if (ErrorMarkers.Any(lower.Contains))
        {
            return LogLevel.Error;
        }

        if (WarningMarkers.Any(lower.Contains))
        {
            return LogLevel.Warning;
        }

        return NoticeMarkers.Any(lower.Contains) ? LogLevel.Notice : LogLevel.Info;
    }
}
