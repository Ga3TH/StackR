using StackR.Core.Manifests;

namespace StackR.Core.Components;

/// <summary>Avancement de <see cref="StackREngine.EnsureDefaultComponentsAsync"/>.</summary>
/// <param name="Release">Version en cours d'installation.</param>
/// <param name="Index">Rang (à partir de 1) du composant dans le lot.</param>
/// <param name="Count">Nombre de composants à installer.</param>
/// <param name="Stage">Téléchargement, Vérification, Extraction ou Terminé (voir <see cref="InstallProgress"/>).</param>
/// <param name="Percent">Avancement du composant en cours, de 0 à 100.</param>
public sealed record DefaultInstallProgress(ComponentRelease Release, int Index, int Count, string Stage, double Percent);

/// <summary>Bilan de <see cref="StackREngine.EnsureDefaultComponentsAsync"/>.</summary>
/// <param name="Installed">Versions installées et activées.</param>
/// <param name="Errors">Échecs d'installation (en français), un par composant.</param>
/// <param name="Warnings">Avertissements de l'application de la configuration.</param>
public sealed record DefaultInstallReport(
    IReadOnlyList<ComponentRelease> Installed, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool Success => Errors.Count == 0;
}
