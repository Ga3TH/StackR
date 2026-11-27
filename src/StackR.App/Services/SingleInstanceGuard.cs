using System.IO;
using System.IO.Pipes;
using System.Security.Principal;

namespace StackR.App.Services;

/// <summary>
/// Instance unique par session utilisateur : Mutex <c>Local\StackR.App</c> et tube nommé
/// <c>StackR.App.Activate.&lt;SID&gt;</c> pour réactiver la fenêtre de l'instance existante.
/// </summary>
public sealed class SingleInstanceGuard : IDisposable
{
    public const string MutexName = @"Local\StackR.App";

    private const string ActivateMessage = "activate";

    private readonly CancellationTokenSource _cts = new();
    private Mutex? _mutex;
    private Task? _listener;
    private bool _disposed;

    // Variable d'environnement STACKR_INSTANCE : instance isolée pour essayer l'application à côté de la vraie.
    private static string Suffix => Environment.GetEnvironmentVariable("STACKR_INSTANCE") is { Length: > 0 } s ? "." + s : "";

    public static string PipeName =>
        $"StackR.App.Activate.{WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName}{Suffix}";

    /// <summary>Tente de devenir l'instance principale (sans attendre).</summary>
    public bool TryAcquire() => TryAcquire(TimeSpan.Zero);

    /// <summary>
    /// Tente de devenir l'instance principale en attendant au plus <paramref name="wait"/> que l'instance
    /// précédente libère le mutex (relance de StackR : l'ancienne instance est encore en train de se fermer).
    /// À appeler sur le thread de l'interface : le mutex est libéré par <see cref="Dispose"/> sur ce même thread.
    /// </summary>
    public bool TryAcquire(TimeSpan wait)
    {
        if (_mutex is not null)
        {
            return true;
        }

        var mutex = new Mutex(initiallyOwned: false, MutexName + Suffix);
        bool owned;
        try
        {
            owned = mutex.WaitOne(wait);
        }
        catch (AbandonedMutexException)
        {
            // L'instance précédente s'est terminée sans libérer le mutex : il nous revient.
            owned = true;
        }

        if (!owned)
        {
            mutex.Dispose();
            return false;
        }

        _mutex = mutex;
        return true;
    }

    /// <summary>Démarre l'écoute des demandes d'activation envoyées par les instances suivantes.</summary>
    public void StartListening(Action onActivate)
    {
        _listener = Task.Run(() => ListenAsync(onActivate, _cts.Token));
    }

    /// <summary>Demande à l'instance existante d'afficher sa fenêtre.</summary>
    public static bool SignalExistingInstance()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out, PipeOptions.CurrentUserOnly);
            client.Connect(2000);
            using var writer = new StreamWriter(client) { AutoFlush = true };
            writer.WriteLine(ActivateMessage);
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Arrête l'écoute et libère le mutex (idempotent) : une nouvelle instance peut alors démarrer.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _cts.Cancel();
        try
        {
            _listener?.Wait(500);
        }
        catch (AggregateException)
        {
            // Annulation attendue.
        }

        _cts.Dispose();
        if (_mutex is not null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Libéré depuis un autre thread : le système le libère à la fin du processus.
            }

            _mutex.Dispose();
            _mutex = null;
        }
    }

    private static async Task ListenAsync(Action onActivate, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.In,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await server.WaitForConnectionAsync(token);

                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                readCts.CancelAfter(TimeSpan.FromSeconds(2));
                using var reader = new StreamReader(server);
                var line = await reader.ReadLineAsync(readCts.Token);
                if (string.Equals(line, ActivateMessage, StringComparison.Ordinal))
                {
                    onActivate();
                }
            }
            catch (OperationCanceledException)
            {
                // Arrêt demandé, ou client trop lent : on repart à l'écoute.
            }
            catch (IOException)
            {
                // Client déconnecté prématurément.
            }
        }
    }
}
