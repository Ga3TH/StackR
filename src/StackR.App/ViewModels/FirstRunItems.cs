using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using StackR.App.Resources;
using StackR.Core.Manifests;

namespace StackR.App.ViewModels;

/// <summary>Étape affichée dans la colonne de progression de l'assistant.</summary>
public sealed partial class FirstRunStepItem : ObservableObject
{
    public FirstRunStepItem(int index, string title)
    {
        Index = index;
        Title = title;
    }

    public int Index { get; }

    public string Number => (Index + 1).ToString(CultureInfo.CurrentCulture);

    public string Title { get; }

    [ObservableProperty]
    private bool _isCurrent;

    [ObservableProperty]
    private bool _isDone;
}

/// <summary>Résultat de la vérification d'un port (libre, ou processus et PID qui l'occupent).</summary>
public sealed record PortCheckItem(int Port, string Usage, bool IsFree, string Status)
{
    public string PortText => Port.ToString(CultureInfo.InvariantCulture);
}

/// <summary>Composant proposé à l'étape « Composants » (version recommandée du manifeste).</summary>
public sealed partial class ComponentChoice : ObservableObject
{
    public ComponentChoice(ComponentRelease release, string name, string description)
    {
        Release = release;
        Name = name;
        Description = description;
    }

    public ComponentRelease Release { get; }

    public ComponentKind Kind => Release.Component;

    public string Name { get; }

    public string Description { get; }

    public string Version => Release.Version;

    public string SizeText => FirstRunFormat.Size(Release.SizeBytes);

    [ObservableProperty]
    private bool _isSelected = true;
}

public enum InstallItemState
{
    Pending,
    Running,
    Done,
    Failed,
    Cancelled,
}

/// <summary>Installation d'un composant à l'étape « Téléchargement vérifié ».</summary>
public sealed partial class InstallItem : ObservableObject
{
    public InstallItem(ComponentChoice choice)
    {
        Choice = choice;
        StageText = Strings.FrInstallPending;
    }

    public ComponentChoice Choice { get; }

    public ComponentRelease Release => Choice.Release;

    public string Title => Choice.Name + " " + Choice.Version;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsRunning), nameof(IsDone), nameof(IsFailed), nameof(CanRetry), nameof(IsWaiting))]
    private InstallItemState _state;

    [ObservableProperty]
    private double _percent;

    [ObservableProperty]
    private string _stageText;

    [ObservableProperty]
    private string? _errorText;

    public bool IsRunning => State == InstallItemState.Running;

    public bool IsDone => State == InstallItemState.Done;

    public bool IsFailed => State == InstallItemState.Failed;

    public bool IsWaiting => State is InstallItemState.Pending or InstallItemState.Cancelled;

    public bool CanRetry => State is InstallItemState.Failed or InstallItemState.Cancelled;
}

/// <summary>Mises en forme partagées de l'assistant et du démarrage.</summary>
internal static class FirstRunFormat
{
    public static string Format(string format, params object?[] args) =>
        string.Format(CultureInfo.CurrentCulture, format, args);

    public static string Size(long bytes) =>
        Format(Strings.FrSizeMegabytes, bytes / (1024.0 * 1024.0));
}
