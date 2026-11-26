using StackR.Core;

namespace StackR.App.Services;

/// <summary>
/// Branche la coquille (<see cref="AppShell"/> : zone de notification, fenêtre) sur le moteur au démarrage,
/// indépendamment des pages : tout démarrer / arrêter, sites du menu, reprise des services, démarrage auto.
/// </summary>
public sealed class EngineBridge
{
    private readonly AppShell _shell;
    private readonly StackREngine _engine;

    public EngineBridge(AppShell shell, StackREngine engine)
    {
        _shell = shell;
        _engine = engine;
    }

    /// <summary>Appelé une fois, sur le thread de l'interface, après l'assistant de premier lancement.</summary>
    public void Initialize()
    {
        // Contenu : tâche 15 (tableau de bord et sites).
        _ = _shell;
        _ = _engine;
    }
}
