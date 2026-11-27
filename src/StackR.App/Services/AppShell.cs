namespace StackR.App.Services;

/// <summary>
/// Bus d'événements de la coquille : la zone de notification et la fenêtre y déposent leurs
/// demandes (tout démarrer, tout arrêter, ouvrir un site, quitter...), les tâches suivantes
/// les branchent sur le moteur.
/// </summary>
public sealed class AppShell
{
    private IReadOnlyList<string> _sites = [];

    public event EventHandler? StartAllRequested;

    public event EventHandler? StopAllRequested;

    public event EventHandler? ShowWindowRequested;

    public event EventHandler? QuitRequested;

    /// <summary>Levé quand l'installateur d'une mise à jour est lancé : l'application se ferme sans arrêter les services.</summary>
    public event EventHandler? ExitForUpdateRequested;

    /// <summary>Levé pour relancer StackR (paramètres qui n'agissent qu'au prochain lancement).</summary>
    public event EventHandler? RestartRequested;

    /// <summary>Levé quand la fenêtre se cache dans la zone de notification.</summary>
    public event EventHandler? HiddenToTray;

    public event EventHandler<string>? SiteOpenRequested;

    public event EventHandler? SitesChanged;

    /// <summary>Indique si au moins un service tourne (à brancher sur le moteur ; faux par défaut).</summary>
    public Func<bool> AnyServiceRunning { get; set; } = static () => false;

    /// <summary>Vrai une fois la sortie de l'application décidée (la fermeture de fenêtre n'est plus interceptée).</summary>
    public bool IsExiting { get; internal set; }

    /// <summary>Noms des sites affichés dans le sous-menu « Sites » de la zone de notification.</summary>
    public IReadOnlyList<string> Sites => _sites;

    public void SetSites(IEnumerable<string> sites)
    {
        _sites = [.. sites];
        SitesChanged?.Invoke(this, EventArgs.Empty);
    }

    public void RaiseStartAll() => StartAllRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseStopAll() => StopAllRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseShowWindow() => ShowWindowRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseOpenSite(string site) => SiteOpenRequested?.Invoke(this, site);

    public void RaiseHiddenToTray() => HiddenToTray?.Invoke(this, EventArgs.Empty);

    public void RaiseQuit() => QuitRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseExitForUpdate() => ExitForUpdateRequested?.Invoke(this, EventArgs.Empty);

    public void RaiseRestart() => RestartRequested?.Invoke(this, EventArgs.Empty);
}
