using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using StackR.App.Services;
using StackR.Core;
using StackR.Core.Formatting;
using StackR.Core.Services;
using Wpf.Ui.Controls;

namespace StackR.App.ViewModels;

/// <summary>Carte d'un service (Apache, MySQL, Mailpit) : état en direct, version, ports, démarrage / arrêt.</summary>
public sealed partial class ServiceCardViewModel : ObservableObject
{
    private readonly ManagedService _service;
    private readonly StackREngine _engine;
    private readonly EngineBridge _bridge;
    private readonly Dispatcher _dispatcher;

    [ObservableProperty]
    private ServiceStatus _status;

    [ObservableProperty]
    private bool _isInstalled;

    [ObservableProperty]
    private string _versionText = "";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private string _pidText = Strings.UiDash;

    [ObservableProperty]
    private string _uptimeText = Strings.UiDash;

    [ObservableProperty]
    private string _memoryText = Strings.UiDash;

    private ProcessSnapshot? _snapshot;
    private bool _preview;

    public ServiceCardViewModel(ManagedService service, StackREngine engine, EngineBridge bridge, Action openVersions)
    {
        _service = service;
        _engine = engine;
        _bridge = bridge;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        OpenVersionsCommand = new RelayCommand(openVersions);

        // StatusChanged est levé hors du thread de l'interface.
        _service.StatusChanged += (_, _) => _dispatcher.BeginInvoke(Refresh);
        _bridge.BusyChanged += (_, _) =>
        {
            ToggleCommand.NotifyCanExecuteChanged();
            RestartCommand.NotifyCanExecuteChanged();
        };
        Refresh();
    }

    public string Name => _service.DisplayName;

    public string Id => _service.Id;

    public SymbolRegular Icon => _service.Id switch
    {
        "apache" => SymbolRegular.Server24,
        "mysql" => SymbolRegular.Database24,
        _ => SymbolRegular.Mail24,
    };

    /// <summary>Ports en écoute, affichés en puces.</summary>
    public IReadOnlyList<string> Ports => [.. _service.Ports.Select(p => p.ToString(CultureInfo.InvariantCulture))];

    /// <summary>Code couleur de la pastille : Success, Caution, Critical ou neutre.</summary>
    public string Tone => Status switch
    {
        ServiceStatus.Running => "Success",
        ServiceStatus.Starting or ServiceStatus.Stopping => "Caution",
        ServiceStatus.Error => "Critical",
        _ => "Neutral",
    };

    public string PortsText => Strings.Format(Strings.DashPorts, string.Join(", ", _service.Ports.Select(p => p.ToString(CultureInfo.InvariantCulture))));

    public RelayCommand OpenVersionsCommand { get; }

    public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

    public bool IsTransitioning => Status is ServiceStatus.Starting or ServiceStatus.Stopping;

    public bool IsRunning => Status == ServiceStatus.Running;

    public string StatusText => Status switch
    {
        ServiceStatus.Running => Strings.DashStatusRunning,
        ServiceStatus.Starting => Strings.DashStatusStarting,
        ServiceStatus.Stopping => Strings.DashStatusStopping,
        ServiceStatus.Error => Strings.DashStatusError,
        _ => Strings.DashStatusStopped,
    };

    public string ActionText => Status switch
    {
        ServiceStatus.Running => Strings.DashStop,
        ServiceStatus.Error => Strings.DashRetry,
        _ => Strings.DashStart,
    };

    public SymbolRegular ActionIcon => Status == ServiceStatus.Running ? SymbolRegular.Stop24 : SymbolRegular.Play24;

    /// <summary>Ouvre la page Journaux (branché par le tableau de bord).</summary>
    public RelayCommand? OpenLogsCommand { get; init; }

    public IAsyncRelayCommand RestartCommand => _restartCommand ??= new AsyncRelayCommand(RestartAsync, () => IsRunning && !_bridge.IsBusy);

    private IAsyncRelayCommand? _restartCommand;

    public IAsyncRelayCommand ToggleCommand => _toggleCommand ??= new AsyncRelayCommand(ToggleAsync, CanToggle);

    private IAsyncRelayCommand? _toggleCommand;

    /// <summary>Relit l'état du service et de son composant (à appeler sur le thread de l'interface).</summary>
    public void Refresh()
    {
        if (_preview)
        {
            return;
        }

        var kind = EngineBridge.ComponentOf(_service.Id);
        IsInstalled = _engine.ActiveDirectory(kind) is not null;
        VersionText = IsInstalled ? _engine.ActiveVersion(kind) ?? "" : Strings.DashNotInstalled;
        Status = _service.Status;
        ErrorMessage = _service.Status == ServiceStatus.Error ? _service.LastError : null;
        ToggleCommand.NotifyCanExecuteChanged();
        RestartCommand.NotifyCanExecuteChanged();
        if (Status != ServiceStatus.Running)
        {
            _snapshot = null;
        }

        UpdateMetricTexts();
    }

    /// <summary>Relit PID, mémoire et démarrage du processus (hors du thread de l'interface), puis met à jour les textes.</summary>
    public async Task RefreshMetricsAsync()
    {
        if (_preview)
        {
            UpdateMetricTexts();
            return;
        }

        var pid = _service.ProcessId;
        _snapshot = Status == ServiceStatus.Running && pid is { } id ? await Task.Run(() => ProcessMetrics.TryRead(id)) : null;
        UpdateMetricTexts();
    }

    /// <summary>Données factices (outil de captures) : l'état réel n'est plus relu.</summary>
    internal void ApplyPreview(ServiceStatus status, ProcessSnapshot? snapshot, string? error = null)
    {
        _preview = true;
        IsInstalled = true;
        var kind = EngineBridge.ComponentOf(_service.Id);
        VersionText = _engine.ActiveVersion(kind) ?? "";
        Status = status;
        ErrorMessage = error;
        _snapshot = snapshot;
        UpdateMetricTexts();
    }

    private void UpdateMetricTexts()
    {
        var culture = CultureInfo.CurrentUICulture;
        if (_snapshot is { } s && Status == ServiceStatus.Running)
        {
            PidText = s.Pid.ToString(CultureInfo.InvariantCulture);
            UptimeText = HumanFormat.Duration(DateTimeOffset.Now - s.StartedAt, culture);
            MemoryText = HumanFormat.Bytes(s.WorkingSetBytes, culture);
        }
        else
        {
            PidText = UptimeText = MemoryText = Strings.UiDash;
        }
    }

    private async Task RestartAsync()
    {
        await _bridge.StopServiceAsync(_service);
        await _bridge.StartServiceAsync(_service);
        Refresh();
        await RefreshMetricsAsync();
    }

    partial void OnStatusChanged(ServiceStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(ActionIcon));
        OnPropertyChanged(nameof(IsTransitioning));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(Tone));
    }

    partial void OnErrorMessageChanged(string? value) => OnPropertyChanged(nameof(HasError));

    private bool CanToggle() => IsInstalled && !IsTransitioning && !_bridge.IsBusy;

    private async Task ToggleAsync()
    {
        if (_service.Status == ServiceStatus.Running)
        {
            await _bridge.StopServiceAsync(_service);
        }
        else
        {
            await _bridge.StartServiceAsync(_service);
        }

        Refresh();
    }
}
