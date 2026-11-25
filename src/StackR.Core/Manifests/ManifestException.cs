namespace StackR.Core.Manifests;

/// <summary>Manifeste invalide (canal, URL, SHA-256 ou version incorrects).</summary>
public sealed class ManifestException : Exception
{
    public ManifestException() { }
    public ManifestException(string message) : base(message) { }
    public ManifestException(string message, Exception innerException) : base(message, innerException) { }
}
