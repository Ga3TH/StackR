namespace StackR.App.ViewModels;

/// <summary>Page qui sait se relire (touche F5).</summary>
public interface IRefreshable
{
    Task RefreshAsync();
}
