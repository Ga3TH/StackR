using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Net.Http;
using CommunityToolkit.Mvvm.ComponentModel;
using StackR.App.Resources;
using StackR.Core;
using StackR.Core.Components;
using StackR.Core.Download;
using StackR.Core.Manifests;
using StackR.Core.Services;
using Wpf.Ui.Controls;

namespace StackR.App.ViewModels;

/// <summary>Une section de la page Versions : un composant et ses versions (une seule opération à la fois).</summary>
public sealed partial class ComponentSectionViewModel : ObservableObject
{
    private readonly StackREngine _engine;
    private readonly Func<string, string, bool> _confirm;
    private CancellationTokenSource? _cts;

    public ComponentSectionViewModel(
        StackREngine engine, ComponentKind kind, string name, string description, SymbolRegular icon, Func<string, string, bool> confirm)
    {
        _engine = engine;
        Kind = kind;
        Name = name;
        Description = description;
        Icon = icon;
        _confirm = confirm;
        Refresh();
    }

    public ComponentKind Kind { get; }

    public string Name { get; }

    public string Description { get; }

    public SymbolRegular Icon { get; }

    public ObservableCollection<ReleaseItemViewModel> Items { get; } = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private string _activeText = string.Empty;

    [ObservableProperty]
    private bool _isEmpty;

    [ObservableProperty]
    private bool _hasMessage;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _messageSeverity = InfoBarSeverity.Informational;

    partial void OnIsBusyChanged(bool value)
    {
        foreach (var item in Items)
        {
            item.RefreshCommands();
        }
    }

    /// <summary>Relit le manifeste et le magasin (sans effet pendant une opération : elle rafraîchit à sa fin).</summary>
    public void Refresh()
    {
        if (IsBusy)
        {
            return;
        }

        var catalog = _engine.Manifest.For(Kind).OrderByDescending(r => ParseVersion(r.Version)).ToList();
        var recommended = _engine.Manifest.Recommended(Kind)?.Version;
        var installed = _engine.Store.Installed(Kind);
        var active = _engine.ActiveDirectory(Kind) is null ? null : _engine.ActiveVersion(Kind);

        var rows = catalog
            .Select(r => (r.Version, Release: (ComponentRelease?)r))
            .Concat(installed
                .Where(v => !catalog.Any(r => string.Equals(r.Version, v, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(ParseVersion)
                .Select(v => (Version: v, Release: (ComponentRelease?)null)))
            .ToList();

        Items.Clear();
        foreach (var (version, release) in rows)
        {
            var item = new ReleaseItemViewModel(this, version, release, string.Equals(version, recommended, StringComparison.OrdinalIgnoreCase))
            {
                IsInstalled = installed.Contains(version, StringComparer.OrdinalIgnoreCase),
                IsActive = string.Equals(version, active, StringComparison.OrdinalIgnoreCase),
            };
            Items.Add(item);
        }

        IsEmpty = Items.Count == 0;
        Summary = UiFormat.Of(Strings.VerSectionSummary, installed.Count, Items.Count);
        ActiveText = active is null ? Strings.VerSectionNoActive : UiFormat.Of(Strings.VerSectionActive, active);
    }

    internal async Task InstallAsync(ReleaseItemViewModel item)
    {
        if (IsBusy || item.Release is null)
        {
            return;
        }

        IsBusy = true;
        item.IsInstalling = true;
        item.Percent = 0;
        item.ProgressText = UiFormat.Of(Strings.VerInstalling, Name, item.Version);
        using var cts = new CancellationTokenSource();
        _cts = cts;
        ClearMessage();

        var warnings = new List<string>();
        if (item.Release.Requires.Contains("vcredist-x64", StringComparer.OrdinalIgnoreCase) && !Prerequisites.HasVcRedistX64())
        {
            warnings.Add(UiFormat.Of(Strings.VerVcRedistWarning, Name, Prerequisites.VcRedistUrl));
        }

        try
        {
            var progress = new Progress<InstallProgress>(item.Report);
            var result = await _engine.InstallAsync(item.Release, progress, cts.Token);
            warnings.AddRange(result.Warnings);
            var becameActive = string.Equals(_engine.ActiveVersion(Kind), item.Version, StringComparison.OrdinalIgnoreCase);
            Show(
                warnings,
                UiFormat.Of(becameActive ? Strings.VerInstalledOkActive : Strings.VerInstalledOk, Name, item.Version),
                InfoBarSeverity.Success);
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            Show(Strings.VerInstallCancelled, InfoBarSeverity.Informational);
        }
        catch (IntegrityException ex)
        {
            Show(UiFormat.Of(Strings.VerErrIntegrity, ex.Message), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or TimeoutException)
        {
            Show(UiFormat.Of(Strings.VerErrNetwork, ex.Message), InfoBarSeverity.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or ArgumentException)
        {
            Show(UiFormat.Of(Strings.VerErrGeneric, ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            _cts = null;
            item.IsInstalling = false;
            IsBusy = false;
            Refresh();
        }
    }

    internal void CancelInstall() => _cts?.Cancel();

    internal async Task ActivateAsync(ReleaseItemViewModel item)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        ClearMessage();
        try
        {
            var result = await _engine.UseAsync(Kind, item.Version);
            Show(
                result.Warnings,
                UiFormat.Of(Strings.VerActivated, Name, item.Version),
                InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Show(UiFormat.Of(Strings.VerErrGeneric, ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    internal async Task UninstallAsync(ReleaseItemViewModel item)
    {
        if (IsBusy)
        {
            return;
        }

        ClearMessage();
        var serviceId = ServiceIdFor(Kind);
        var service = serviceId is null ? null : _engine.Services.Services.FirstOrDefault(s => s.Id == serviceId);
        if (service is { Status: ServiceStatus.Running or ServiceStatus.Starting or ServiceStatus.Stopping })
        {
            Show(UiFormat.Of(Strings.VerUninstallBlocked, Name, item.Version, service.DisplayName), InfoBarSeverity.Warning);
            return;
        }

        if (!_confirm(
                UiFormat.Of(Strings.VerUninstallTitle, Name, item.Version),
                Strings.VerUninstallBody))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var version = item.Version;
            await Task.Run(() => _engine.UninstallAsync(Kind, version));
            Show(UiFormat.Of(Strings.VerUninstalled, Name, version), InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Show(UiFormat.Of(Strings.VerErrGeneric, ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
            Refresh();
        }
    }

    private void ClearMessage()
    {
        HasMessage = false;
        Message = string.Empty;
    }

    private void Show(string text, InfoBarSeverity severity) => Show([], text, severity);

    // Les avertissements du moteur s'ajoutent au message de réussite et font passer la barre en « Avertissement ».
    private void Show(IReadOnlyList<string> warnings, string text, InfoBarSeverity severity)
    {
        Message = warnings.Count == 0 ? text : text + Environment.NewLine + string.Join(Environment.NewLine, warnings);
        MessageSeverity = warnings.Count > 0 && severity == InfoBarSeverity.Success ? InfoBarSeverity.Warning : severity;
        HasMessage = true;
    }

    private static string? ServiceIdFor(ComponentKind kind) => kind switch
    {
        ComponentKind.Apache or ComponentKind.Php => "apache",
        ComponentKind.MySql => "mysql",
        ComponentKind.Mailpit => "mailpit",
        _ => null,
    };

    private static Version ParseVersion(string v) => System.Version.TryParse(v, out var parsed) ? parsed : new Version(0, 0);
}
