using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using System.Collections.ObjectModel;
using System.Windows;
using StackR.App.Services;
using StackR.Core;
using StackR.Core.Settings;
using StackR.Core.Manifests;
using StackR.Core.Services;

namespace StackR.App.ViewModels;

/// <summary>Un service dans la barre d'état : nom et code couleur (Success, Caution, Critical, neutre).</summary>
public sealed partial class StatusItem(string name) : ObservableObject
{
    public string Name { get; } = name;

    [ObservableProperty]
    private string _tone = "Neutral";
}

/// <summary>Modèle de vue de la fenêtre principale : marque et commandes globales.</summary>
public sealed partial class MainWindowViewModel : ObservableObject
{
    private readonly AppShell _shell;

    private readonly StackREngine _engine;

    [ObservableProperty]
    private string _phpText = "";

    [ObservableProperty]
    private string _wwwText = "";

    public MainWindowViewModel(AppShell shell, UpdateBannerViewModel update, StackREngine engine)
    {
        _shell = shell;
        _engine = engine;
        Update = update;
        foreach (var service in engine.Services.Services.OrderBy(s => s.Id switch { "apache" => 0, "mysql" => 1, _ => 2 }))
        {
            var item = new StatusItem(service.DisplayName);
            Services.Add(item);
            // StatusChanged est levé hors du thread de l'interface.
            service.StatusChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(() => item.Tone = ToneOf(service.Status));
            item.Tone = ToneOf(service.Status);
        }

        engine.SettingsChanged += (_, _) => Application.Current?.Dispatcher.BeginInvoke(RefreshStatus);
        RefreshStatus();
    }

    /// <summary>Services affichés dans la barre d'état.</summary>
    public ObservableCollection<StatusItem> Services { get; } = [];

    /// <summary>Relit PHP actif et dossier www (barre d'état).</summary>
    public void RefreshStatus()
    {
        var php = _engine.ActiveDirectory(ComponentKind.Php) is null ? null : _engine.ActiveVersion(ComponentKind.Php);
        PhpText = php is null ? Strings.UiSummaryNoPhp : "PHP " + php;
        WwwText = _engine.Settings.EffectiveWww(_engine.Paths);
        foreach (var (item, service) in Services.Zip(_engine.Services.Services.OrderBy(s => s.Id switch { "apache" => 0, "mysql" => 1, _ => 2 })))
        {
            item.Tone = ToneOf(service.Status);
        }
    }

    internal static string ToneOf(ServiceStatus status) => status switch
    {
        ServiceStatus.Running => "Success",
        ServiceStatus.Starting or ServiceStatus.Stopping => "Caution",
        ServiceStatus.Error => "Critical",
        _ => "Neutral",
    };

    /// <summary>Bandeau de mise à jour.</summary>
    public UpdateBannerViewModel Update { get; }

    public string AppName { get; } = Strings.AppName;

    public string Tagline { get; } = Strings.AppTagline;

    [RelayCommand]
    private void StartAll() => _shell.RaiseStartAll();

    [RelayCommand]
    private void StopAll() => _shell.RaiseStopAll();
}
