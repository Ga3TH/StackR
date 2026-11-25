using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace StackR.Core.Services;

/// <summary>Processus propriétaire d'un port TCP en écoute.</summary>
public sealed record PortOwner(int Pid, string ProcessName);

/// <summary>Détection des ports occupés (typiquement Laragon encore lancé).</summary>
public static partial class PortProbe
{
    private const int AfInet = 2;
    private const int TcpTableOwnerPidListener = 3;
    private const uint ErrorInsufficientBuffer = 122;
    private const int RowSize = 24; // MIB_TCPROW_OWNER_PID : 6 DWORD.

    /// <summary>Processus en écoute TCP sur 0.0.0.0 ou 127.0.0.1 pour ce port, ou null.</summary>
    public static PortOwner? FindOwner(int port)
    {
        var pid = FindListenerPid(port);
        return pid is null ? null : new PortOwner(pid.Value, DescribeProcess(pid.Value));
    }

    /// <summary>Message d'erreur si le port ne peut pas être pris par StackR, sinon null.</summary>
    internal static string? DescribeConflict(int port)
    {
        var owner = FindOwner(port);
        if (owner is not null)
        {
            return string.Create(
                CultureInfo.InvariantCulture, $"Port {port} utilisé par {owner.ProcessName} (PID {owner.Pid})");
        }

        // Aucun propriétaire visible : on vérifie quand même que le port est réservable
        // (plages exclues par Hyper-V, écouteur IPv6 exclusif…).
        return CanBind(port)
            ? null
            : string.Create(CultureInfo.InvariantCulture, $"Port {port} indisponible (réservé ou utilisé par un autre programme)");
    }

    /// <summary>Vrai si 127.0.0.1:<paramref name="port"/> accepte une connexion TCP.</summary>
    internal static async Task<bool> IsAcceptingAsync(int port, CancellationToken ct)
    {
        using var client = new TcpClient(AddressFamily.InterNetwork);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(1));
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token).ConfigureAwait(false);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return false;
        }
    }

    private static bool CanBind(int port)
    {
        // Liaison simple (non exclusive) : des connexions en TIME_WAIT ne doivent pas compter.
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        try
        {
            socket.Bind(new IPEndPoint(IPAddress.Loopback, port));
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static string DescribeProcess(int pid)
    {
        var image = ProcessIdentity.TryGetImagePath(pid);
        if (image is not null)
        {
            return Path.GetFileName(image);
        }

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            return "inconnu";
        }
    }

    private static int? FindListenerPid(int port)
    {
        var size = 0;
        var buffer = IntPtr.Zero;
        try
        {
            uint result;
            do
            {
                if (buffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(buffer);
                    buffer = IntPtr.Zero;
                }

                if (size > 0)
                {
                    buffer = Marshal.AllocHGlobal(size);
                }

                result = GetExtendedTcpTable(buffer, ref size, false, AfInet, TcpTableOwnerPidListener, 0);
            }
            while (result == ErrorInsufficientBuffer);

            if (result != 0 || buffer == IntPtr.Zero)
            {
                return null;
            }

            var count = Marshal.ReadInt32(buffer);
            for (var i = 0; i < count; i++)
            {
                var row = buffer + 4 + (i * RowSize);
                var localAddr = (uint)Marshal.ReadInt32(row, 4);
                var localPort = (uint)Marshal.ReadInt32(row, 8);
                var rowPort = (int)(((localPort & 0xFF) << 8) | ((localPort >> 8) & 0xFF));
                if (rowPort != port)
                {
                    continue;
                }

                var address = new IPAddress(localAddr);
                if (address.Equals(IPAddress.Any) || address.Equals(IPAddress.Loopback))
                {
                    return Marshal.ReadInt32(row, 20);
                }
            }

            return null;
        }
        finally
        {
            if (buffer != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    [LibraryImport("iphlpapi.dll")]
    private static partial uint GetExtendedTcpTable(
        IntPtr tcpTable, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order, int af, int tableClass, uint reserved);
}
