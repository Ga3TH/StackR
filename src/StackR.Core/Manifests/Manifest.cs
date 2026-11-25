namespace StackR.Core.Manifests;

public sealed class Manifest
{
    public Manifest(IReadOnlyList<ComponentRelease> releases) => Releases = releases;

    public IReadOnlyList<ComponentRelease> Releases { get; }

    public IEnumerable<ComponentRelease> For(ComponentKind kind) =>
        Releases.Where(r => r.Component == kind);

    /// <summary>Version la plus haute pour le composant, ou null s'il n'en existe aucune.</summary>
    public ComponentRelease? Recommended(ComponentKind kind) =>
        For(kind).OrderByDescending(r => ParseVersion(r.Version)).FirstOrDefault();

    private static Version ParseVersion(string v) =>
        System.Version.TryParse(v, out var parsed) ? parsed : new Version(0, 0);
}
