using System.Globalization;
using System.IO;
using System.Text;

namespace StackR.App.Services;

/// <summary>
/// Journal des erreurs inattendues de l'interface : <c>%LOCALAPPDATA%\StackR\crash.log</c>
/// (ancien journal renommé en <c>crash.old.log</c> au-delà de 1 Mo). N'échoue jamais.
/// </summary>
public static class CrashLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly Lock Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StackR", "crash.log");

    /// <summary>Ajoute une entrée horodatée (contexte, type, message, pile) au journal.</summary>
    public static void Write(string context, Exception? exception)
    {
        try
        {
            var entry = new StringBuilder()
                .Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture))
                .Append(" [").Append(context).Append("] ")
                .AppendLine(exception?.ToString() ?? "(aucune exception)")
                .AppendLine()
                .ToString();

            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                var info = new FileInfo(FilePath);
                if (info.Exists && info.Length > MaxBytes)
                {
                    File.Move(FilePath, Path.ChangeExtension(FilePath, ".old.log"), overwrite: true);
                }

                File.AppendAllText(FilePath, entry, new UTF8Encoding(false));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            // Le journal est un confort : on ne masque jamais l'erreur d'origine par une erreur d'écriture.
        }
    }
}
