namespace StackR.Core.Services;

// Échec de démarrage avec un message destiné à l'utilisateur (en français).
internal sealed class ServiceStartException : Exception
{
    public ServiceStartException()
    {
    }

    public ServiceStartException(string message)
        : base(message)
    {
    }

    public ServiceStartException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
