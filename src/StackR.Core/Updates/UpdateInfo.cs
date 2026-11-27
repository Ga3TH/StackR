namespace StackR.Core.Updates;

/// <summary>Une version plus récente publiée sur GitHub.</summary>
/// <param name="Version">Numéro de la version (X.Y.Z).</param>
/// <param name="ReleaseNotes">Notes de version (corps de la release).</param>
/// <param name="InstallerUrl">Téléchargement de l'installateur ; nul si la release n'en propose pas de vérifiable.</param>
/// <param name="Sha256">Empreinte SHA-256 (64 caractères hexadécimaux) de l'installateur ; nulle si absente.</param>
/// <param name="SizeBytes">Taille de l'installateur (plafond du téléchargement).</param>
/// <param name="HtmlUrl">Page de la release.</param>
public sealed record UpdateInfo(
    Version Version,
    string ReleaseNotes,
    Uri? InstallerUrl,
    string? Sha256,
    long SizeBytes,
    Uri HtmlUrl)
{
    /// <summary>Vrai si l'installateur peut être téléchargé et vérifié ; sinon on propose « Voir la release ».</summary>
    public bool CanInstall => InstallerUrl is not null && Sha256 is not null && SizeBytes > 0;
}

/// <summary>Issue d'une recherche de mise à jour.</summary>
public enum UpdateCheckStatus
{
    UpToDate,
    Available,
    Failed,
}

public sealed record UpdateCheckResult(UpdateCheckStatus Status, UpdateInfo? Info);
