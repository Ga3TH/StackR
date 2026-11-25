using CommunityToolkit.Mvvm.ComponentModel;

namespace StackR.App.ViewModels;

/// <summary>Base des modèles de vue de pages : titre et sous-titre affichés en en-tête.</summary>
public abstract class PageViewModelBase : ObservableObject
{
    protected PageViewModelBase(string title, string subtitle)
    {
        Title = title;
        Subtitle = subtitle;
    }

    public string Title { get; }

    public string Subtitle { get; }
}
