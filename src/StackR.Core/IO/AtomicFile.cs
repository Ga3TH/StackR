using System.Text;

namespace StackR.Core.IO;

// Écriture atomique : fichier temporaire unique dans le même dossier, puis remplacement de la cible.
internal static class AtomicFile
{
    public static void WriteAllText(string path, string content, Encoding enc)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(enc);
        WriteAllBytes(path, enc.GetBytes(content));
    }

    public static void WriteAllBytes(string path, byte[] content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(content);

        var full = Path.GetFullPath(path);
        var dir = Path.GetDirectoryName(full);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var tmp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                fs.Write(content);
                fs.Flush(flushToDisk: true);
            }

            File.Move(tmp, full, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp))
            {
                File.Delete(tmp);
            }
        }
    }
}
