using StackR.Core.Manifests;

namespace StackR.Core.Components;

/// <summary>Installation par lot de l'assistant de premier lancement.</summary>
public static class FirstRunInstall
{
    /// <summary>
    /// Comme <see cref="StackREngine.InstallAsync"/> (empreinte vérifiée, première version installée activée et
    /// enregistrée), mais sans réappliquer la configuration : l'assistant installe plusieurs composants d'affilée
    /// puis appelle <see cref="StackREngine.ApplyConfigurationAsync"/> une seule fois à la fin, ce qui évite une
    /// demande UAC (fichier hosts) et une régénération des certificats à chaque composant.
    /// </summary>
    public static async Task InstallWithoutApplyAsync(
        this StackREngine engine, ComponentRelease release, IProgress<InstallProgress>? progress, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(release);
        await engine.Installer.InstallAsync(release, progress, ct).ConfigureAwait(false);

        if (engine.ActiveDirectory(release.Component) is null)
        {
            engine.Settings.ActiveVersions[ComponentStore.FolderName(release.Component)] = release.Version;
            engine.SaveSettings();
        }
    }
}
