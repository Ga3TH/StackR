namespace StackR.Core.Download;

/// <summary>
/// Levée lorsqu'un fichier téléchargé ou une archive ne peut pas être considéré comme fiable :
/// empreinte SHA-256 différente de celle attendue, redirection vers une URL non HTTPS,
/// entrée d'archive sortant du dossier de destination, etc.
/// </summary>
public sealed class IntegrityException : Exception
{
    public IntegrityException()
    {
    }

    public IntegrityException(string message)
        : base(message)
    {
    }

    public IntegrityException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
