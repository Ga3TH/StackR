using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using StackR.App.Services;
using StackR.Core;
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

    public ServiceCardViewModel(ManagedService service, StackREngine engine, EngineBridge bridge, Action openVersions)
    {
        _service = service;
        _engine = engine;
        _bridge = bridge;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
        OpenVersionsCommand = new RelayCommand(openVersions);

        // StatusChanged est levé hors du thread de l'interface.
        _service.StatusChanged += (_, _) => _dispatcher.BeginInvoke(Refresh);
        _bridge.BusyChanged += (_, _) => ToggleCommand.NotifyCanExecuteChanged();
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

    public IAsyncRelayCommand ToggleCommand => _toggleCommand ??= new AsyncRelayCommand(ToggleAsync, CanToggle);

    private IAsyncRelayCommand? _toggleCommand;

    /// <summary>Relit l'état du service et de son composant (à appeler sur le thread de l'interface).</summary>
    public void Refresh()
    {
        var kind = EngineBridge.ComponentOf(_service.Id);
        IsInstalled = _engine.ActiveDirectory(kind) is not null;
        VersionText = IsInstalled
            ? Strings.Format(Strings.DashVersion, _engine.ActiveVersion(kind))
            : Strings.DashNotInstalled;
        Status = _service.Status;
        ErrorMessage = _service.Status == ServiceStatus.Error ? _service.LastError : null;
        ToggleCommand.NotifyCanExecuteChanged();
    }

    partial void OnStatusChanged(ServiceStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(ActionText));
        OnPropertyChanged(nameof(ActionIcon));
        OnPropertyChanged(nameof(IsTransitioning));
        OnPropertyChanged(nameof(IsRunning));
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
