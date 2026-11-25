using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace StackR.Core.Services;

// Identification des processus (chemin de l'exécutable) et arrêt par arbre.
internal static partial class ProcessIdentity
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary>Chemin complet de l'exécutable du PID, ou null si inaccessible.</summary>
    public static string? TryGetImagePath(int pid)
    {
        using var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        return handle.IsInvalid ? null : QueryImagePath(handle);
    }

    /// <summary>
    /// Vrai seulement si l'exécutable du processus est <paramref name="exeName"/> sous <paramref name="binDir"/>.
    /// Accès refusé ou processus terminé : pas « nôtre ».
    /// </summary>
    public static bool IsOurs(Process process, string binDir, string exeName)
    {
        ArgumentNullException.ThrowIfNull(process);
        string? image;
        try
        {
            // La poignée du Process épingle l'identité : pas de confusion avec un PID recyclé ensuite.
            image = process.HasExited ? null : QueryImagePath(process.SafeHandle);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or NotSupportedException)
        {
            return false;
        }

        return image is not null
            && IsUnder(image, binDir)
            && string.Equals(Path.GetFileName(image), exeName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Comparaison de chemins complets normalisés, insensible à la casse.</summary>
    public static bool IsUnder(string path, string directory)
    {
        string full;
        string dir;
        try
        {
            full = Path.GetFullPath(path);
            dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        return full.StartsWith(dir, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Tue le processus et ses descendants ; sans effet s'il est déjà terminé.</summary>
    public static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Terminé entre-temps.
        }
    }

    private static unsafe string? QueryImagePath(SafeHandle handle)
    {
        var buffer = new char[32768];
        var size = (uint)buffer.Length;
        fixed (char* p = buffer)
        {
            if (!QueryFullProcessImageName(handle, 0, p, ref size))
            {
                return null;
            }
        }

        return new string(buffer, 0, (int)size);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial SafeProcessHandle OpenProcess(
        uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "QueryFullProcessImageNameW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool QueryFullProcessImageName(SafeHandle process, uint flags, char* exeName, ref uint size);
}
