using System.Text;
using System.Text.RegularExpressions;

namespace StackR.Core.Hosts;

/// <summary>
/// Manipulation pure (texte → texte) du bloc StackR du fichier hosts.
/// </summary>
/// <remarks>
/// Invariant de sécurité : <see cref="Apply"/> ne supprime jamais que
/// (1) les lignes marqueurs <c># &gt;&gt;&gt; StackR</c> / <c># &lt;&lt;&lt; StackR</c> et
/// (2) les lignes <c>127.0.0.1 &lt;nom .test valide&gt;</c> situées dans une région StackR.
/// Toute autre ligne est conservée à l'octet près (fin de ligne comprise).
/// Régions StackR :
/// - bloc complet : du marqueur de début au premier marqueur de fin rencontré avant un autre marqueur de début ;
/// - bloc non terminé (début sans fin) : le marqueur de début et la suite contiguë de lignes
///   <c>127.0.0.1 &lt;nom .test valide&gt;</c> qui le suit immédiatement ;
/// - marqueur de fin orphelin : cette seule ligne.
/// Les lignes étrangères trouvées dans une région sont conservées, dans l'ordre, juste après le nouveau bloc.
/// Le nouveau bloc est inséré à la place de la première région trouvée, sinon ajouté en fin de fichier.
/// </remarks>
public static partial class HostsBlock
{
    internal const string StartMarker = "# >>> StackR";
    internal const string EndMarker = "# <<< StackR";
    internal const string LoopbackAddress = "127.0.0.1";
    private const string Crlf = "\r\n";

    // \z (et non $) : en .NET, $ accepte un « \n » final, ce qui laisserait passer « a.test\n ».
    [GeneratedRegex(@"^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?\.test\z", RegexOptions.CultureInvariant)]
    private static partial Regex ValidHostRegex();

    /// <summary>Indique si <paramref name="h"/> est un nom <c>.test</c> valide (minuscules ASCII uniquement).</summary>
    public static bool IsValidHost(string h) => h is not null && ValidHostRegex().IsMatch(h);

    /// <summary>
    /// Remplace (ou insère) le bloc StackR par des lignes <c>127.0.0.1 &lt;hôte&gt;</c> triées et uniques,
    /// en CRLF, sans modifier le reste du contenu. Liste vide : le bloc est supprimé.
    /// </summary>
    /// <exception cref="ArgumentException">Un nom n'est pas un nom <c>.test</c> valide.</exception>
    public static string Apply(string existingContent, IEnumerable<string> hostnames)
    {
        ArgumentNullException.ThrowIfNull(existingContent);
        ArgumentNullException.ThrowIfNull(hostnames);

        // Validation complète AVANT toute construction : un seul nom invalide = rien n'est produit.
        var hosts = NormalizeAndValidate(hostnames);
        var newBlock = BuildBlock(hosts);

        var lines = SplitLines(existingContent);
        var remove = new bool[lines.Count];
        var firstRegion = MarkStackRRegions(lines, remove);

        var sb = new StringBuilder(existingContent.Length + newBlock.Length + 2);
        if (firstRegion < 0)
        {
            sb.Append(existingContent);
            if (newBlock.Length > 0)
            {
                if (existingContent.Length > 0 && !existingContent.EndsWith('\n'))
                {
                    sb.Append(Crlf);
                }

                sb.Append(newBlock);
            }

            return sb.ToString();
        }

        for (var i = 0; i < lines.Count; i++)
        {
            if (i == firstRegion)
            {
                sb.Append(newBlock);
            }

            if (!remove[i])
            {
                sb.Append(lines[i].Raw);
            }
        }

        return sb.ToString();
    }

    private static List<string> NormalizeAndValidate(IEnumerable<string> hostnames)
    {
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var raw in hostnames)
        {
            var host = raw is null ? null : ToLowerAscii(raw);
            if (host is null || !IsValidHost(host))
            {
                throw new ArgumentException(
                    $"Nom d'hôte refusé pour le fichier hosts : « {Describe(raw)} » (attendu : <nom>.test, lettres a-z, chiffres, tirets).",
                    nameof(hostnames));
            }

            set.Add(host);
        }

        return [.. set];
    }

    // Seules les lettres ASCII A-Z sont abaissées : ToLowerInvariant transformerait p. ex.
    // le signe Kelvin (U+212A) en « k » et rendrait valide un nom qui ne l'est pas.
    private static string ToLowerAscii(string s)
    {
        return string.Create(s.Length, s, static (span, src) =>
        {
            for (var i = 0; i < src.Length; i++)
            {
                var c = src[i];
                span[i] = c is >= 'A' and <= 'Z' ? (char)(c + ('a' - 'A')) : c;
            }
        });
    }

    // Représentation sûre d'un nom refusé (pas de retour à la ligne ni de caractère de contrôle dans le message).
    private static string Describe(string? raw)
    {
        if (raw is null)
        {
            return "(null)";
        }

        var sb = new StringBuilder();
        foreach (var c in raw.Length > 80 ? raw[..80] : raw)
        {
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '?');
        }

        return sb.ToString();
    }

    private static string BuildBlock(List<string> hosts)
    {
        if (hosts.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        sb.Append(StartMarker).Append(Crlf);
        foreach (var host in hosts)
        {
            sb.Append(LoopbackAddress).Append(' ').Append(host).Append(Crlf);
        }

        sb.Append(EndMarker).Append(Crlf);
        return sb.ToString();
    }

    /// <summary>Marque les lignes à supprimer ; renvoie l'index de la première région StackR, ou -1.</summary>
    private static int MarkStackRRegions(List<Line> lines, bool[] remove)
    {
        var first = -1;
        var i = 0;
        while (i < lines.Count)
        {
            var kind = Classify(lines[i]);
            if (kind == LineKind.End)
            {
                // Marqueur de fin orphelin.
                remove[i] = true;
                first = first < 0 ? i : first;
                i++;
                continue;
            }

            if (kind != LineKind.Start)
            {
                i++;
                continue;
            }

            first = first < 0 ? i : first;
            remove[i] = true;

            var end = FindMatchingEnd(lines, i + 1);
            if (end >= 0)
            {
                // Bloc complet : on retire marqueurs et entrées StackR, on garde les lignes étrangères.
                for (var j = i + 1; j < end; j++)
                {
                    remove[j] = IsOwnEntry(lines[j]);
                }

                remove[end] = true;
                i = end + 1;
            }
            else
            {
                // Bloc non terminé : seules les entrées StackR contiguës au marqueur sont considérées comme nôtres.
                var j = i + 1;
                while (j < lines.Count && IsOwnEntry(lines[j]))
                {
                    remove[j] = true;
                    j++;
                }

                i = j;
            }
        }

        return first;
    }

    private static int FindMatchingEnd(List<Line> lines, int from)
    {
        for (var j = from; j < lines.Count; j++)
        {
            switch (Classify(lines[j]))
            {
                case LineKind.End:
                    return j;
                case LineKind.Start:
                    return -1;
            }
        }

        return -1;
    }

    private static LineKind Classify(Line line)
    {
        var text = Trim(line.Content);
        if (text.SequenceEqual(StartMarker))
        {
            return LineKind.Start;
        }

        return text.SequenceEqual(EndMarker) ? LineKind.End : LineKind.Other;
    }

    /// <summary>Ligne de la forme <c>127.0.0.1 &lt;nom .test valide&gt;</c> (espaces/tabulations tolérés).</summary>
    private static bool IsOwnEntry(Line line)
    {
        var text = Trim(line.Content);
        if (!text.StartsWith(LoopbackAddress, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = text[LoopbackAddress.Length..];
        var name = rest.TrimStart(" \t");
        if (name.Length == rest.Length || name.IndexOfAny(' ', '\t') >= 0)
        {
            return false;
        }

        return IsValidHost(name.ToString());
    }

    private static ReadOnlySpan<char> Trim(string s) => s.AsSpan().Trim(" \t\r");

    /// <summary>Découpe sur « \n » ; chaque ligne garde son texte brut (fin de ligne incluse).</summary>
    private static List<Line> SplitLines(string content)
    {
        var lines = new List<Line>();
        var start = 0;
        while (start < content.Length)
        {
            var nl = content.IndexOf('\n', start);
            var next = nl < 0 ? content.Length : nl + 1;
            var raw = content[start..next];
            var contentEnd = raw.EndsWith('\n') ? raw.Length - 1 : raw.Length;
            if (contentEnd > 0 && raw[contentEnd - 1] == '\r')
            {
                contentEnd--;
            }

            lines.Add(new Line(raw, raw[..contentEnd]));
            start = next;
        }

        return lines;
    }

    private readonly record struct Line(string Raw, string Content);

    private enum LineKind
    {
        Other,
        Start,
        End,
    }
}
