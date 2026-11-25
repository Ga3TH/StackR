using System.Diagnostics;
using System.Text;

namespace StackR.Core.Services;

/// <summary>
/// Lancement réel des processus : aucune fenêtre, sorties standard redirigées vers <c>logs\</c>
/// (<c>&lt;exe&gt;.log</c> pour un serveur, <c>&lt;exe&gt;-run.log</c> pour une commande courte).
/// </summary>
public sealed class ProcessRunner(string logsDir) : IProcessRunner
{
    private static readonly UTF8Encoding Utf8NoBom = new(false);

    /// <summary>Journal des sorties d'un serveur démarré par <see cref="Start"/>.</summary>
    internal static string LogPathFor(string logsDir, string exe) =>
        Path.Combine(logsDir, Path.GetFileNameWithoutExtension(exe) + ".log");

    /// <summary>Journal des sorties d'une commande exécutée par <see cref="RunAsync"/>.</summary>
    internal static string RunLogPathFor(string logsDir, string exe) =>
        Path.Combine(logsDir, Path.GetFileNameWithoutExtension(exe) + "-run.log");

    public Process Start(ProcessStartInfo psi)
    {
        ArgumentNullException.ThrowIfNull(psi);
        Hide(psi);

        Directory.CreateDirectory(logsDir);
        var log = OpenLog(LogPathFor(logsDir, psi.FileName));
        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        var open = 2;
        void OnData(object? sender, DataReceivedEventArgs e)
        {
            lock (log)
            {
                if (e.Data is not null)
                {
                    log.WriteLine(e.Data);
                }
                else if (--open == 0)
                {
                    // Les deux flux sont fermés : le journal peut être libéré.
                    log.Dispose();
                }
            }
        }

        process.OutputDataReceived += OnData;
        process.ErrorDataReceived += OnData;
        try
        {
            process.Start();
        }
        catch
        {
            log.Dispose();
            process.Dispose();
            throw;
        }

        process.StandardInput.Close();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        return process;
    }

    public Process? TryGet(int pid)
    {
        try
        {
            return Process.GetProcessById(pid);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    public async Task<int> RunAsync(string exe, string args, CancellationToken ct)
    {
        var psi = new ProcessStartInfo(exe, args);
        Hide(psi);

        Directory.CreateDirectory(logsDir);
        using var log = OpenLog(RunLogPathFor(logsDir, exe));
        using var process = new Process { StartInfo = psi };
        process.Start();
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            ProcessIdentity.KillTree(process);
            throw;
        }

        await log.WriteAsync(await stdout.ConfigureAwait(false)).ConfigureAwait(false);
        await log.WriteAsync(await stderr.ConfigureAwait(false)).ConfigureAwait(false);
        return process.ExitCode;
    }

    private static void Hide(ProcessStartInfo psi)
    {
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.WindowStyle = ProcessWindowStyle.Hidden;
        psi.RedirectStandardInput = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        if (string.IsNullOrEmpty(psi.WorkingDirectory))
        {
            psi.WorkingDirectory = Path.GetDirectoryName(psi.FileName) ?? "";
        }
    }

    // Journal tronqué à chaque lancement, lisible pendant l'écriture (page Journaux).
    private static StreamWriter OpenLog(string path) =>
        new(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), Utf8NoBom)
        {
            AutoFlush = true,
        };
}
