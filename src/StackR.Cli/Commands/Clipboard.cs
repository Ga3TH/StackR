using System.Diagnostics;
using System.Text;

namespace StackR.Cli.Commands;

/// <summary>Presse-papiers via <c>%SystemRoot%\System32\clip.exe</c> (texte sur l'entrée standard).</summary>
internal static class Clipboard
{
    public static async Task<bool> CopyAsync(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        // Chemin absolu : jamais un clip.exe trouvé dans le dossier courant ou le PATH.
        var psi = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "clip.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            StandardInputEncoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
        };

        using var process = Process.Start(psi);
        if (process is null)
        {
            return false;
        }

        // UTF-16 avec BOM : clip.exe le reconnaît comme du texte Unicode.
        await process.StandardInput.WriteAsync(text).ConfigureAwait(false);
        process.StandardInput.Close();
        await process.WaitForExitAsync().ConfigureAwait(false);
        return process.ExitCode == 0;
    }
}
