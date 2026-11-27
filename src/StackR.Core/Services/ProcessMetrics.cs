using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace StackR.Core.Services;

/// <summary>Mesure d'un processus serveur : démarrage et mémoire (lui et ses descendants, ex. enfant httpd).</summary>
/// <param name="Pid">PID du processus principal.</param>
/// <param name="StartedAt">Heure de démarrage du processus.</param>
/// <param name="WorkingSetBytes">Mémoire vive utilisée (ensemble de travail) par le processus et ses descendants.</param>
/// <param name="ProcessCount">Nombre de processus comptés.</param>
public sealed record ProcessSnapshot(int Pid, DateTimeOffset StartedAt, long WorkingSetBytes, int ProcessCount);

/// <summary>Lecture légère (sans WMI) de l'heure de démarrage et de la mémoire d'un arbre de processus.</summary>
public static partial class ProcessMetrics
{
    private const uint Th32csSnapProcess = 0x00000002;

    /// <summary>Mesure le processus <paramref name="pid"/> et ses descendants, ou null s'il n'existe plus.</summary>
    public static ProcessSnapshot? TryRead(int pid)
    {
        try
        {
            using var main = Process.GetProcessById(pid);
            var started = new DateTimeOffset(main.StartTime);
            var total = main.WorkingSet64;
            var count = 1;
            foreach (var child in Descendants(pid, ParentMap()))
            {
                try
                {
                    using var process = Process.GetProcessById(child);
                    total += process.WorkingSet64;
                    count++;
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception)
                {
                    // Terminé entre-temps ou inaccessible : ignoré.
                }
            }

            return new ProcessSnapshot(pid, started, total, count);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or Win32Exception or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>PID des descendants de <paramref name="root"/> d'après une table enfant → parent (cycles ignorés).</summary>
    internal static IReadOnlyList<int> Descendants(int root, IReadOnlyDictionary<int, int> parentOf)
    {
        var children = parentOf
            .Where(p => p.Key != p.Value)
            .GroupBy(p => p.Value)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Key).ToList());
        var result = new List<int>();
        var seen = new HashSet<int> { root };
        var queue = new Queue<int>();
        queue.Enqueue(root);
        while (queue.Count > 0)
        {
            if (!children.TryGetValue(queue.Dequeue(), out var kids))
            {
                continue;
            }

            foreach (var kid in kids.Where(seen.Add))
            {
                result.Add(kid);
                queue.Enqueue(kid);
            }
        }

        return result;
    }

    /// <summary>Table PID → PID parent de tous les processus (instantané Toolhelp).</summary>
    private static unsafe Dictionary<int, int> ParentMap()
    {
        var map = new Dictionary<int, int>();
        var snapshot = CreateToolhelp32Snapshot(Th32csSnapProcess, 0);
        if (snapshot == -1)
        {
            return map;
        }

        try
        {
            var entry = default(ProcessEntry32);
            entry.Size = (uint)sizeof(ProcessEntry32);
            if (!Process32First(snapshot, ref entry))
            {
                return map;
            }

            do
            {
                map[(int)entry.ProcessId] = (int)entry.ParentProcessId;
            }
            while (Process32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return map;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private unsafe struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public nint DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;
        public fixed char ExeFile[260];
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial nint CreateToolhelp32Snapshot(uint flags, uint processId);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32FirstW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32First(nint snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll", EntryPoint = "Process32NextW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool Process32Next(nint snapshot, ref ProcessEntry32 entry);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(nint handle);
}
