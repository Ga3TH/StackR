using StackR.Core.Manifests;
using StackR.Core.Paths;

namespace StackR.Core.Components;

/// <summary>
/// Inventaire des composants installés sous <c>bin\&lt;composant&gt;\&lt;version&gt;</c>.
/// Un dossier n'est considéré installé que s'il contient le marqueur <c>.stackr-installed</c>.
/// </summary>
/// <param name="paths">Arborescence StackR.</param>
/// <param name="activeVersion">Donne la version active d'un composant (ou null), d'après les paramètres.</param>
public sealed class ComponentStore(StackRPaths paths, Func<ComponentKind, string?> activeVersion)
{
    internal const string MarkerFileName = ".stackr-installed";

    private readonly StackRPaths _paths = paths ?? throw new ArgumentNullException(nameof(paths));
    private readonly Func<ComponentKind, string?> _activeVersion = activeVersion ?? throw new ArgumentNullException(nameof(activeVersion));

    /// <summary>Nom du dossier sous <c>bin\</c> : le nom du composant en minuscules.</summary>
    internal static string FolderName(ComponentKind c) => c.ToString().ToLowerInvariant();

    public IReadOnlyList<string> Installed(ComponentKind c)
    {
        var root = Path.Combine(_paths.Bin, FolderName(c));
        if (!Directory.Exists(root))
        {
            return [];
        }

        return Directory.EnumerateDirectories(root)
            .Where(d => !d.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(Path.Combine(d, MarkerFileName)))
            .Select(d => Path.GetFileName(d))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public bool IsInstalled(ComponentKind c, string version)
    {
        ValidateVersion(version);
        return File.Exists(Path.Combine(_paths.ComponentDir(FolderName(c), version), MarkerFileName));
    }

    /// <exception cref="InvalidOperationException">La version est la version active du composant.</exception>
    public void Uninstall(ComponentKind c, string version)
    {
        ValidateVersion(version);
        if (string.Equals(_activeVersion(c), version, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Impossible de désinstaller {c} {version} : c'est la version active.");
        }

        var dir = _paths.ComponentDir(FolderName(c), version);
        if (Directory.Exists(dir))
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>Une version devient un nom de dossier : aucun séparateur ni caractère interdit.</summary>
    internal static void ValidateVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (version.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || version is "." or ".." || version.EndsWith('.'))
        {
            throw new ArgumentException($"Version invalide « {version} ».", nameof(version));
        }
    }
}
