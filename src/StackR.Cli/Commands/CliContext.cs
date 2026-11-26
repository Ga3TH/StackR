using StackR.Core;

namespace StackR.Cli.Commands;

/// <summary>Codes de sortie du CLI.</summary>
internal static class ExitCodes
{
    public const int Ok = 0;
    public const int Error = 1;
    public const int InvalidArguments = 2;
    public const int NotElevated = 5;
}

/// <summary>
/// Environnement d'exécution d'une commande. Le moteur n'est créé qu'à la demande :
/// <c>hosts sync --names …</c>, lancé élevé (parfois sous un autre compte), n'en a pas besoin.
/// </summary>
internal sealed class CliContext(Func<StackREngine> engineFactory, TextWriter output, TextWriter error) : IDisposable
{
    private readonly Lazy<StackREngine> _engine = new(engineFactory);

    public TextWriter Out { get; } = output;

    public TextWriter Error { get; } = error;

    /// <summary>Sortie sur une vraie console (barre de progression redessinée avec « \r »).</summary>
    public bool Interactive { get; init; }

    /// <summary>Droits administrateur du processus.</summary>
    public required Func<bool> IsElevated { get; init; }

    /// <summary>Écriture du bloc StackR du fichier hosts.</summary>
    public required Action<IReadOnlyCollection<string>> WriteHosts { get; init; }

    public Func<string, Task<bool>> CopyToClipboard { get; init; } = Clipboard.CopyAsync;

    /// <summary>Le moteur doit-il être libéré par ce contexte (créé par lui).</summary>
    public bool OwnsEngine { get; init; }

    public StackREngine Engine => _engine.Value;

    public void Dispose()
    {
        if (OwnsEngine && _engine.IsValueCreated)
        {
            _engine.Value.Dispose();
        }
    }

    public void PrintWarnings(ApplyResult result)
    {
        foreach (var warning in result.Warnings)
        {
            Out.WriteLine("Attention : " + warning);
        }
    }

    // Texte venant de la ligne de commande, affiché sans caractère de contrôle ni non-ASCII.
    public static string Sanitize(string? value)
    {
        if (value is null)
        {
            return "";
        }

        var chars = (value.Length > 80 ? value[..80] : value).Select(c => c is >= ' ' and <= '~' ? c : '?');
        return new string([.. chars]);
    }
}
