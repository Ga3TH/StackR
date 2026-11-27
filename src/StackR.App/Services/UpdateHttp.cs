using System.Net.Http;

namespace StackR.App.Services;

/// <summary>Client HTTP unique des mises à jour (recherche et téléchargement), vivant autant que l'application.</summary>
internal static class UpdateHttp
{
    public static readonly HttpClient Client = new();
}
