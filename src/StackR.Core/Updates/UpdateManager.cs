using StackR.Core.Settings;

namespace StackR.Core.Updates;

/// <summary>
/// État des mises à jour (sans interface) : dernière version trouvée, bandeau visible ou masqué pour cette
/// version, mode d'installation. Le modèle de vue de la fenêtre principale s'appuie dessus.
/// </summary>
public sealed class UpdateManager(
    Func<CancellationToken, Task<UpdateCheckResult>> check,
    Func<StackRSettings> getSettings,
    Action<Func<StackRSettings, StackRSettings>> saveSettings,
    bool isInstalledCopy)
{
    private readonly Func<CancellationToken, Task<UpdateCheckResult>> _check = check ?? throw new ArgumentNullException(nameof(check));
    private readonly Func<StackRSettings> _getSettings = getSettings ?? throw new ArgumentNullException(nameof(getSettings));
    private readonly Action<Func<StackRSettings, StackRSettings>> _save = saveSettings ?? throw new ArgumentNullException(nameof(saveSettings));

    /// <summary>Levé quand <see cref="Available"/> ou la visibilité du bandeau change.</summary>
    public event EventHandler? Changed;

    /// <summary>Version plus récente connue, sinon nul.</summary>
    public UpdateInfo? Available { get; private set; }

    /// <summary>Copie installée par l'installateur (sinon portable : on ouvre la page de la release).</summary>
    public bool IsInstalledCopy { get; } = isInstalledCopy;

    /// <summary>Vrai si le bouton peut mettre à jour directement (copie installée et installateur vérifiable).</summary>
    public bool CanInstallHere => IsInstalledCopy && Available is { CanInstall: true };

    /// <summary>Vrai si le bandeau doit s'afficher (mise à jour trouvée et non masquée pour cette version).</summary>
    public bool IsBannerVisible =>
        Available is { } a
        && !string.Equals(_getSettings().DismissedUpdateVersion, a.Version.ToString(3), StringComparison.Ordinal);

    /// <summary>
    /// Cherche une mise à jour. Une recherche automatique ne fait rien si l'option est désactivée (résultat
    /// <see cref="UpdateCheckStatus.UpToDate"/>) ; une recherche manuelle réaffiche le bandeau masqué.
    /// </summary>
    public async Task<UpdateCheckStatus> CheckAsync(bool manual, CancellationToken ct = default)
    {
        if (!manual && !_getSettings().AutoCheckUpdates)
        {
            return UpdateCheckStatus.UpToDate;
        }

        var result = await _check(ct).ConfigureAwait(false);
        switch (result.Status)
        {
            case UpdateCheckStatus.Available when result.Info is { } info:
                Available = info;
                if (manual)
                {
                    SafeSave(s => s with { DismissedUpdateVersion = "" });
                }

                break;
            case UpdateCheckStatus.UpToDate:
                Available = null;
                break;
            default:
                // Échec : on garde ce qu'on savait.
                break;
        }

        Changed?.Invoke(this, EventArgs.Empty);
        return result.Status;
    }

    /// <summary>Masque le bandeau pour la version actuellement proposée (mémorisé dans les paramètres).</summary>
    public void Dismiss()
    {
        if (Available is not { } a)
        {
            return;
        }

        var version = a.Version.ToString(3);
        SafeSave(s => s with { DismissedUpdateVersion = version });
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void SafeSave(Func<StackRSettings, StackRSettings> change)
    {
        try
        {
            _save(change);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Préférence non enregistrée : le bandeau reste simplement affiché au prochain lancement.
        }
    }
}
