using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using StackR.Core.Components;
using StackR.Core.Manifests;

namespace StackR.App.ViewModels;

/// <summary>Une version d'un composant : badges, progression d'installation et commandes.</summary>
public sealed partial class ReleaseItemViewModel : ObservableObject
{
    private readonly ComponentSectionViewModel _section;

    internal ReleaseItemViewModel(ComponentSectionViewModel section, string version, ComponentRelease? release, bool isRecommended)
    {
        _section = section;
        Version = version;
        Release = release;
        IsRecommended = isRecommended;
        SizeText = release is { SizeBytes: > 0 }
            ? UiFormat.Of(Strings.VerSize, Math.Max(1, (long)Math.Round(release.SizeBytes / 1048576.0)))
            : string.Empty;
    }

    /// <summary>Entrée du manifeste, ou null pour une version installée hors catalogue.</summary>
    public ComponentRelease? Release { get; }

    public string Version { get; }

    public bool IsRecommended { get; }

    public bool IsOffCatalog => Release is null;

    public string SizeText { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstall), nameof(ShowActivate), nameof(ShowUninstall))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowActivate))]
    private bool _isActive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowInstall), nameof(ShowActivate), nameof(ShowUninstall))]
    private bool _isInstalling;

    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private string _progressText = string.Empty;

    public bool ShowInstall => !IsInstalled && !IsInstalling && Release is not null;

    public bool ShowActivate => IsInstalled && !IsActive && !IsInstalling;

    public bool ShowUninstall => IsInstalled && !IsInstalling;

    /// <summary>Appelé depuis le thread de l'interface (<see cref="Progress{T}"/> créé dessus).</summary>
    internal void Report(InstallProgress progress)
    {
        var stage = progress.Stage switch
        {
            "Téléchargement" => Strings.VerStageDownload,
            "Vérification" => Strings.VerStageVerify,
            "Extraction" => Strings.VerStageExtract,
            "Terminé" => Strings.VerStageDone,
            _ => progress.Stage,
        };
        Percent = Math.Clamp(progress.Percent, 0, 100);
        ProgressText = UiFormat.Of(
            Strings.VerStagePercent, stage, Math.Round(Percent).ToString("0", CultureInfo.CurrentCulture));
    }

    internal void RefreshCommands()
    {
        InstallCommand.NotifyCanExecuteChanged();
        ActivateCommand.NotifyCanExecuteChanged();
        UninstallCommand.NotifyCanExecuteChanged();
    }

    private bool CanInstall() => ShowInstall && !_section.IsBusy;

    private bool CanActivate() => ShowActivate && !_section.IsBusy;

    private bool CanUninstall() => ShowUninstall && !IsActive && !_section.IsBusy;

    [RelayCommand(CanExecute = nameof(CanInstall))]
    private Task InstallAsync() => _section.InstallAsync(this);

    [RelayCommand]
    private void CancelInstall() => _section.CancelInstall();

    [RelayCommand(CanExecute = nameof(CanActivate))]
    private Task ActivateAsync() => _section.ActivateAsync(this);

    [RelayCommand(CanExecute = nameof(CanUninstall))]
    private Task UninstallAsync() => _section.UninstallAsync(this);
}
