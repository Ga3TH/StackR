using System.Globalization;
using StackR.Core.Components;

namespace StackR.Cli.Commands;

/// <summary>
/// Barre de progression texte : redessinée sur place (« \r ») sur une console,
/// une ligne par étape quand la sortie est redirigée.
/// </summary>
internal sealed class TextProgressBar(TextWriter output, bool interactive) : IProgress<InstallProgress>
{
    private const int Width = 30;

    private readonly Lock _gate = new();
    private string? _stage;
    private int _percent = -1;

    public void Report(InstallProgress value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var percent = (int)Math.Clamp(double.IsNaN(value.Percent) ? 0 : value.Percent, 0, 100);
        lock (_gate)
        {
            var newStage = !string.Equals(value.Stage, _stage, StringComparison.Ordinal);
            if (!newStage && percent == _percent)
            {
                return;
            }

            if (interactive)
            {
                if (newStage && _stage is not null)
                {
                    output.WriteLine();
                }

                var filled = percent * Width / 100;
                output.Write(string.Create(
                    CultureInfo.InvariantCulture,
                    $"\r{value.Stage,-16}[{new string('#', filled)}{new string('.', Width - filled)}] {percent,3} %"));
            }
            else if (newStage)
            {
                output.WriteLine(value.Stage + "...");
            }

            _stage = value.Stage;
            _percent = percent;
        }
    }

    /// <summary>Termine la ligne en cours (console).</summary>
    public void Complete()
    {
        lock (_gate)
        {
            if (interactive && _stage is not null)
            {
                output.WriteLine();
                _stage = null;
            }
        }
    }
}
