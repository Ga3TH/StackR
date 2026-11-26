using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace StackR.Core.Config;

/// <summary>
/// MySQL pour Windows lit ses fichiers d'options et ses arguments dans la page de code ANSI du système
/// (pas en UTF-8) : un chemin « C:\Dossier é\StackR » écrit en UTF-8 devient illisible. Les chemins sont donc
/// écrits en ANSI ; un chemin que cette page de code ne peut pas représenter est remplacé par son nom court 8.3.
/// </summary>
public static partial class AnsiPath
{
    /// <summary>Page de code ANSI du système (ex. Windows-1252), sans BOM et stricte (aucun caractère remplacé).</summary>
    public static Encoding Encoding { get; } = CreateEncoding();

    /// <summary>Vrai si <paramref name="text"/> s'écrit sans perte dans la page de code ANSI.</summary>
    public static bool IsRepresentable(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        try
        {
            return string.Equals(Encoding.GetString(Encoding.GetBytes(text)), text, StringComparison.Ordinal);
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    /// <summary>
    /// Renvoie <paramref name="path"/> tel quel s'il est représentable en ANSI, sinon son nom court 8.3
    /// (le dossier existant le plus proche est raccourci, le reste du chemin doit être en ANSI).
    /// </summary>
    /// <summary>
    /// Forme la plus sûre pour PHP (qui charge ses extensions avec des API ANSI) : le chemin tel quel s'il est en
    /// ASCII, sinon son nom court 8.3 s'il est en ASCII, sinon la forme ANSI de <see cref="For"/>.
    /// </summary>
    public static string ForAscii(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (Ascii.IsValid(path))
        {
            return path;
        }

        var full = Path.GetFullPath(path);
        if (Directory.Exists(full) || File.Exists(full))
        {
            var shortened = ShortPath(full);
            if (Ascii.IsValid(shortened))
            {
                return shortened;
            }
        }

        return For(path);
    }

    /// <exception cref="InvalidOperationException">Aucune forme ANSI possible (noms courts désactivés sur le volume).</exception>
    public static string For(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (IsRepresentable(path))
        {
            return path;
        }

        // Le nom court n'existe que pour un chemin existant : on raccourcit le plus long préfixe existant.
        var full = Path.GetFullPath(path);
        var existing = full;
        var suffix = "";
        while (!Directory.Exists(existing) && !File.Exists(existing))
        {
            var parent = Path.GetDirectoryName(existing);
            if (parent is null)
            {
                break;
            }

            suffix = Path.Combine(Path.GetFileName(existing), suffix);
            existing = parent;
        }

        var shortened = ShortPath(existing);
        var candidate = suffix.Length == 0 ? shortened : Path.Combine(shortened, suffix.TrimEnd(Path.DirectorySeparatorChar));
        return IsRepresentable(candidate)
            ? candidate
            : throw new InvalidOperationException(
                $"Le chemin « {path} » contient des caractères que MySQL ne sait pas lire sur ce système. "
                + "Choisissez un dossier d'installation sans caractères spéciaux (lettres accentuées courantes acceptées).");
    }

    private static Encoding CreateEncoding()
    {
        // Les pages de code Windows (1252…) ne sont disponibles qu'une fois ce fournisseur enregistré.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(
            CultureInfo.CurrentCulture.TextInfo.ANSICodePage, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
    }

    private static string ShortPath(string path)
    {
        var buffer = new char[32768];
        var length = GetShortPathName(path, buffer, buffer.Length);
        return length > 0 && length < buffer.Length ? new string(buffer, 0, (int)length) : path;
    }

    [LibraryImport("kernel32.dll", EntryPoint = "GetShortPathNameW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial uint GetShortPathName(string longPath, [Out] char[] shortPath, int bufferLength);
}
