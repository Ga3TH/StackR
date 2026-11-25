namespace StackR.Core.Services;

/// <summary>État d'un service géré par StackR.</summary>
public enum ServiceStatus
{
    Stopped,
    Starting,
    Running,
    Stopping,
    Error,
}
