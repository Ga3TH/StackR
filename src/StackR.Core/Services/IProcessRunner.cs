using System.Diagnostics;

namespace StackR.Core.Services;

/// <summary>Abstraction du lancement de processus (remplacée par un faux dans les tests).</summary>
public interface IProcessRunner
{
    /// <summary>Démarre un processus long (serveur), fenêtre cachée.</summary>
    Process Start(ProcessStartInfo psi);

    /// <summary>Processus portant ce PID, ou null s'il n'existe plus.</summary>
    Process? TryGet(int pid);

    /// <summary>Exécute une commande courte jusqu'à sa fin et renvoie son code de sortie.</summary>
    Task<int> RunAsync(string exe, string args, CancellationToken ct);
}
