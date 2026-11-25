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

    public static string PipeName =>
        $"StackR.App.Activate.{WindowsIdentity.GetCurrent().User?.Value ?? Environment.UserName}";

    /// <summary>Tente de devenir l'instance principale.</summary>
    public bool TryAcquire()
    {
        _mutex = new Mutex(initiallyOwned: true, MutexName, out var createdNew);
        if (!createdNew)
        {
            _mutex.Dispose();
            _mutex = null;
        }

        return createdNew;
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

    public void Dispose()
    {
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
