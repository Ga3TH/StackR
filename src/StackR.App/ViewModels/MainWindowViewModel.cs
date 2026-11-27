using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using StackR.App.Services;

namespace StackR.App.ViewModels;

/// <summary>Modèle de vue de la fenêtre principale : marque et commandes globales.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly AppShell _shell;

    public MainWindowViewModel(AppShell shell, UpdateBannerViewModel update)
    {
        _shell = shell;
        Update = update;
    }

    /// <summary>Bandeau de mise à jour.</summary>
    public UpdateBannerViewModel Update { get; }

    public string AppName { get; } = Strings.AppName;

    public string Tagline { get; } = Strings.AppTagline;

    [RelayCommand]
    private void StartAll() => _shell.RaiseStartAll();

    [RelayCommand]
    private void StopAll() => _shell.RaiseStopAll();
}
