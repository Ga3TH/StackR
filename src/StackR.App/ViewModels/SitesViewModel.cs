using StackR.App.Resources;

namespace StackR.App.ViewModels;

/// <summary>Modèle de vue de la page « Sites » (contenu à venir).</summary>
public sealed class SitesViewModel : PageViewModelBase
{
    public SitesViewModel()
        : base(Strings.NavSites, Strings.SitesSubtitle)
    {
    }
}
