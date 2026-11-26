using System.Text;

namespace StackR.Core.IO;

/// <summary>Lignes lues par <see cref="LogTail.Poll"/>.</summary>
/// <param name="Lines">Nouvelles lignes (sans saut de ligne final).</param>
/// <param name="Reset">Vrai si l'affichage doit repartir de zéro (première lecture, troncature, rotation, fichier supprimé).</param>
public sealed record LogTailResult(IReadOnlyList<string> Lines, bool Reset);

/// <summary>
/// Suivi de la fin d'un fichier journal : la première lecture renvoie les dernières lignes, les suivantes
/// seulement ce qui a été ajouté. Le fichier est ouvert en partage complet (jamais verrouillé) et
/// n'est jamais relu en entier. Une troncature (taille inférieure à la position lue) relance la lecture.
/// </summary>
public sealed class LogTail
{
    private const int MaxCatchUpBytes = 1024 * 1024;
    private const int ChunkSize = 64 * 1024;

    private readonly string _path;
    private readonly int _maxLines;
    private long _position;
    private bool _started;
    private string _pending = "";
    private Decoder _decoder = new UTF8Encoding(false).GetDecoder();

    public LogTail(string path, int maxLines = 500)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxLines, 1);
        _path = path;
        _maxLines = maxLines;
    }

    /// <summary>Lit ce qui est nouveau depuis le dernier appel. Les erreurs d'accès passagères renvoient un résultat vide.</summary>
    public LogTailResult Poll()
    {
        try
        {
            using var stream = new FileStream(
                _path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 4096, FileOptions.SequentialScan);
            var length = stream.Length;

            if (!_started)
            {
                _started = true;
                return ReadTail(stream, length);
            }

            if (length < _position)
            {
                // Troncature ou rotation : on repart du début du nouveau contenu.
                return ReadTail(stream, length);
            }

            if (length == _position)
            {
                return new LogTailResult([], false);
            }

            if (length - _position > MaxCatchUpBytes)
            {
                return ReadTail(stream, length);
            }

            stream.Seek(_position, SeekOrigin.Begin);
            var lines = ReadFrom(stream, length - _position);
            _position = length;
            return new LogTailResult(lines, false);
        }
        catch (FileNotFoundException)
        {
            return Missing();
        }
        catch (DirectoryNotFoundException)
        {
            return Missing();
        }
        catch (IOException)
        {
            return new LogTailResult([], false);
        }
        catch (UnauthorizedAccessException)
        {
            return new LogTailResult([], false);
        }
    }

    private LogTailResult Missing()
    {
        // Fichier supprimé après lecture : l'affichage est vidé, la lecture repart du début s'il revient.
        var wasRead = _position > 0 || _pending.Length > 0;
        _started = false;
        _position = 0;
        _pending = "";
        _decoder = new UTF8Encoding(false).GetDecoder();
        return new LogTailResult([], wasRead);
    }

    // Lit les dernières lignes en remontant par blocs depuis la fin.
    private LogTailResult ReadTail(FileStream stream, long length)
    {
        _decoder = new UTF8Encoding(false).GetDecoder();
        _pending = "";

        var start = length;
        var newlines = 0;
        var buffer = new byte[ChunkSize];
        while (start > 0 && newlines <= _maxLines)
        {
            var size = (int)Math.Min(ChunkSize, start);
            start -= size;
            stream.Seek(start, SeekOrigin.Begin);
            stream.ReadExactly(buffer, 0, size);
            newlines += buffer.AsSpan(0, size).Count((byte)'\n');
        }

        stream.Seek(start, SeekOrigin.Begin);
        var lines = ReadFrom(stream, length - start);
        if (start > 0 && lines.Count > 0)
        {
            // Le premier fragment peut être une ligne coupée en son milieu.
            lines.RemoveAt(0);
        }

        if (lines.Count > _maxLines)
        {
            lines.RemoveRange(0, lines.Count - _maxLines);
        }

        _position = length;
        return new LogTailResult(lines, true);
    }

    private List<string> ReadFrom(FileStream stream, long count)
    {
        var bytes = new byte[count];
        stream.ReadExactly(bytes, 0, bytes.Length);
        var chars = new char[_decoder.GetCharCount(bytes, 0, bytes.Length)];
        var written = _decoder.GetChars(bytes, 0, bytes.Length, chars, 0);
        var text = _pending + new string(chars, 0, written);

        var parts = text.Split('\n');
        // Le dernier fragment n'est pas terminé : on le garde pour la lecture suivante.
        _pending = parts[^1];
        var lines = new List<string>(parts.Length - 1);
        for (var i = 0; i < parts.Length - 1; i++)
        {
            lines.Add(parts[i].TrimEnd('\r'));
        }

        return lines;
    }
}
