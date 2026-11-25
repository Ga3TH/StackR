using StackR.App.Resources;

namespace StackR.App.ViewModels;

/// <summary>Modèle de vue de la page « Versions » (contenu à venir).</summary>
public sealed class VersionsViewModel : PageViewModelBase
{
    public VersionsViewModel()
        : base(Strings.NavVersions, Strings.VersionsSubtitle)
    {
    }
}
