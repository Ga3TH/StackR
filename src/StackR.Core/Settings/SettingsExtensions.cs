using StackR.Core.Paths;

namespace StackR.Core.Settings;

public static class SettingsExtensions
{
    // Dossier www effectif : WwwPath s'il est renseigné, sinon <Root>\www.
    public static string EffectiveWww(this StackRSettings settings, StackRPaths paths)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(paths);
        return string.IsNullOrWhiteSpace(settings.WwwPath) ? paths.Www : settings.WwwPath;
    }
}
