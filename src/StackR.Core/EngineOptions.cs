using Microsoft.Win32;
using StackR.Core.Manifests;
using StackR.Core.Services;

namespace StackR.Core;

/// <summary>
/// Dépendances remplaçables du moteur (tests). Toute valeur nulle prend le comportement réel.
/// </summary>
public sealed record EngineOptions
{
    /// <summary>Catalogue des composants ; par défaut le manifeste embarqué.</summary>
    public Manifest? Manifest { get; init; }

    /// <summary>Client HTTP des téléchargements (non libéré par le moteur) ; par défaut un client propre au moteur.</summary>
    public HttpClient? HttpClient { get; init; }

    /// <summary>Lancement des processus ; par défaut <see cref="Services.ProcessRunner"/> journalisant dans <c>logs\</c>.</summary>
    public IProcessRunner? ProcessRunner { get; init; }

    /// <summary>Fichier hosts ; par défaut celui de Windows.</summary>
    public string? HostsPath { get; init; }

    /// <summary>Relance élevée du CLI ; par défaut <see cref="Hosts.Elevation.RunCliElevatedAsync"/> (UAC, -1 si refus).</summary>
    public Func<string[], Task<int>>? RunElevated { get; init; }

    /// <summary>Le processus a-t-il des droits administrateur ; par défaut <see cref="Hosts.Elevation.IsElevated"/>.</summary>
    public Func<bool>? IsElevated { get; init; }

    /// <summary>Remplace HKCU pour le PATH utilisateur.</summary>
    public RegistryKey? UserRegistryRoot { get; init; }

    /// <summary>Dossier de <c>stackr.exe</c> ajouté au PATH ; par défaut celui de l'exécutable courant.</summary>
    public string? InstallDirectory { get; init; }

    /// <summary>Dossier des paquets embarqués ; par défaut <c>packages\</c> à côté de l'exécutable courant.</summary>
    public string? PackagesDirectory { get; init; }

    /// <summary>
    /// Autorise le moteur à consulter et nettoyer le magasin <c>CurrentUser\Root</c>
    /// (anciennes CA StackR, avertissement « CA non approuvée ») ; désactivé dans les tests.
    /// </summary>
    public bool ManageCertificateStore { get; init; } = true;
}
