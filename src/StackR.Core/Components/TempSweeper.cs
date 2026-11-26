namespace StackR.Core.Components;

// Nettoyage au mieux des restes dans tmp\ après un plantage ou un arrêt brutal.
internal static class TempSweeper
{
    private static readonly string[] Patterns = ["install-*", "*.download", "*.download.partial", "client-*.cnf", "init-*.sql"];

    public static void Sweep(string tmp, TimeSpan olderThan)
    {
        if (!Directory.Exists(tmp))
        {
            return;
        }

        var limit = DateTime.UtcNow - olderThan;
        foreach (var pattern in Patterns)
        {
            IEnumerable<string> entries;
            try
            {
                entries = Directory.EnumerateFileSystemEntries(tmp, pattern).ToList();
            }
            catch (IOException)
            {
                continue;
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                TryDelete(entry, limit);
            }
        }
    }

    private static void TryDelete(string entry, DateTime limitUtc)
    {
        try
        {
            if (Directory.Exists(entry))
            {
                if (Directory.GetLastWriteTimeUtc(entry) <= limitUtc)
                {
                    Directory.Delete(entry, recursive: true);
                }
            }
            else if (File.GetLastWriteTimeUtc(entry) <= limitUtc)
            {
                File.Delete(entry);
            }
        }
        catch (IOException)
        {
            // Verrouillé ou en cours d'utilisation : au prochain balayage.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
