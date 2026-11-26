namespace StackR.Cli.Commands;

/// <summary>Tableau texte à colonnes alignées (sans couleur ni caractère de dessin : sûr une fois redirigé).</summary>
internal sealed class TextTable(params string[] headers)
{
    private readonly List<string[]> _rows = [];

    public void Add(params string[] cells) => _rows.Add(cells);

    public void WriteTo(TextWriter output)
    {
        var widths = headers.Select((h, i) => Math.Max(h.Length, _rows.Count == 0 ? 0 : _rows.Max(r => r[i].Length))).ToArray();
        WriteRow(output, headers, widths);
        WriteRow(output, [.. widths.Select(w => new string('-', w))], widths);
        foreach (var row in _rows)
        {
            WriteRow(output, row, widths);
        }
    }

    private static void WriteRow(TextWriter output, string[] cells, int[] widths)
    {
        var padded = cells.Select((c, i) => i == cells.Length - 1 ? c : c.PadRight(widths[i]));
        output.WriteLine(string.Join("  ", padded).TrimEnd());
    }
}
