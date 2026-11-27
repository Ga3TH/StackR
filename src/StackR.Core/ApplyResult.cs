namespace StackR.Core;

/// <summary>
/// Résultat de <see cref="StackREngine.ApplyConfigurationAsync"/> : les problèmes non bloquants
/// (UAC refusée, certificat non émis…) sont rapportés ici au lieu de lever une exception.
/// </summary>
/// <param name="HostsUpdated">Le fichier hosts a été mis à jour.</param>
/// <param name="HostsDeclined">La mise à jour du fichier hosts a été refusée à l'invite UAC.</param>
/// <param name="Warnings">Avertissements destinés à l'utilisateur (en français).</param>
public sealed record ApplyResult(bool HostsUpdated, bool HostsDeclined, IReadOnlyList<string> Warnings)
{
    /// <summary>Services en cours d'exécution redémarrés parce que leur configuration avait changé (noms affichés).</summary>
    public IReadOnlyList<string> RestartedServices { get; init; } = [];
}
