namespace StackR.Core.Settings;

/// <summary>Règles de validation des ports saisis dans les paramètres.</summary>
public static class PortRules
{
    public const int MinPort = 1;
    public const int MaxPort = 65535;

    public static bool IsValid(int port) => port is >= MinPort and <= MaxPort;

    /// <summary>Premier port utilisé plusieurs fois, ou <c>null</c> s'il n'y a pas de doublon.</summary>
    public static int? FindDuplicate(IEnumerable<int> ports)
    {
        ArgumentNullException.ThrowIfNull(ports);
        var seen = new HashSet<int>();
        foreach (var port in ports)
        {
            if (!seen.Add(port))
            {
                return port;
            }
        }

        return null;
    }
}
