using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using StackR.App.Services;
using StackR.Core.Download;
using StackR.Core.Updates;
using Wpf.Ui.Controls;

namespace StackR.App.ViewModels;

/// <summary>Bandeau « Mise à jour disponible » en haut de la fenêtre principale, et recherches planifiées.</summary>
public sealed partial class UpdateBannerViewModel : ObservableObject
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private readonly UpdateManager _manager;
    private readonly AppShell _shell;
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _timer;
    private bool _started;

    public UpdateBannerViewModel(UpdateManager manager, AppShell shell)
    {
        _manager = manager;
        _shell = shell;
        _dispatcher = Dispatcher.CurrentDispatcher;
        _manager.Changed += (_, _) => _dispatcher.BeginInvoke(Refresh);
        Refresh();
    }

    [ObservableProperty]
    public partial bool IsVisible { get; set; }

    [ObservableProperty]
    public partial string Title { get; set; } = "";

    [ObservableProperty]
    public partial string Hint { get; set; } = "";

    [ObservableProperty]
    public partial string ReleaseNotes { get; set; } = "";

    [ObservableProperty]
    public partial string ButtonText { get; set; } = Strings.UpdButton;

    [ObservableProperty]
    public partial bool ShowNotes { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial double ProgressPercent { get; set; }

    [ObservableProperty]
    public partial bool IsProgressIndeterminate { get; set; }

    [ObservableProperty]
    public partial string ProgressText { get; set; } = "";

    [ObservableProperty]
    public partial string ErrorText { get; set; } = "";

    public bool HasHint => Hint.Length > 0;

    public bool HasError => ErrorText.Length > 0;

    public string NotesToggleText => ShowNotes ? Strings.UpdHideNotes : Strings.UpdWhatsNew;

    /// <summary>Première recherche (en arrière-plan, sans bloquer) puis une toutes les 6 h tant que l'application tourne.</summary>
    public void StartBackgroundChecks()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        _ = RunCheckAsync();
        _timer = new DispatcherTimer(CheckInterval, DispatcherPriority.Background, (_, _) => _ = RunCheckAsync(), _dispatcher);
        _timer.Start();
    }

    partial void OnHintChanged(string value) => OnPropertyChanged(nameof(HasHint));

    partial void OnErrorTextChanged(string value) => OnPropertyChanged(nameof(HasError));

    partial void OnShowNotesChanged(bool value) => OnPropertyChanged(nameof(NotesToggleText));

    private async Task RunCheckAsync()
    {
        // Jamais d'exception vers l'interface : le vérificateur répond par un statut.
        await _manager.CheckAsync(manual: false);
    }

    private void Refresh()
    {
        if (_manager.Available is not { } info)
        {
            IsVisible = false;
            return;
        }

        Title = Strings.Fill(Strings.UpdBannerTitle, info.Version.ToString(3));
        ReleaseNotes = string.IsNullOrWhiteSpace(info.ReleaseNotes) ? Strings.UpdNoNotes : info.ReleaseNotes.Trim();
        if (_manager.CanInstallHere)
        {
            Hint = "";
            ButtonText = Strings.UpdButton;
        }
        else
        {
            Hint = _manager.IsInstalledCopy ? Strings.UpdBannerNoInstaller : Strings.UpdBannerPortable;
            ButtonText = Strings.UpdViewRelease;
        }

        IsVisible = _manager.IsBannerVisible;
    }

    [RelayCommand]
    private void ToggleNotes() => ShowNotes = !ShowNotes;

    [RelayCommand]
    private void Dismiss() => _manager.Dismiss();

    [RelayCommand]
    private async Task UpdateAsync()
    {
        if (IsBusy || _manager.Available is not { } info)
        {
            return;
        }

        ErrorText = "";
        if (!_manager.CanInstallHere)
        {
            OpenReleasePage(info);
            return;
        }

        var version = info.Version.ToString(3);
        var dialog = new Wpf.Ui.Controls.MessageBox
        {
            Title = Strings.UpdConfirmTitle,
            Content = Strings.Fill(Strings.UpdConfirmMessage, version),
            PrimaryButtonText = Strings.UpdConfirmYes,
            CloseButtonText = Strings.UpdConfirmNo,
            PrimaryButtonAppearance = ControlAppearance.Primary,
            Topmost = true,
        };
        if (await dialog.ShowDialogAsync() != Wpf.Ui.Controls.MessageBoxResult.Primary)
        {
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = true;
        ProgressPercent = 0;
        ProgressText = Strings.Fill(Strings.UpdDownloading, "");
        try
        {
            var progress = new Progress<DownloadProgress>(OnProgress);
            var path = await UpdateInstallation.DownloadInstallerAsync(
                new SecureDownloader(UpdateHttp.Client),
                info,
                UpdateInstallation.DefaultWorkDir(),
                progress,
                CancellationToken.None);

            using var process = Process.Start(new ProcessStartInfo(path, UpdateInstallation.InstallerArguments)
            {
                UseShellExecute = false,
                WorkingDirectory = Path.GetDirectoryName(path)!,
            });

            // L'installateur est lancé : on se ferme (les services restent actifs), il relancera StackR.
            _shell.RaiseExitForUpdate();
        }
        catch (Exception ex) when (ex is IOException or HttpRequestException or IntegrityException or TimeoutException
                                       or UnauthorizedAccessException or Win32Exception or InvalidOperationException
                                       or ArgumentException)
        {
            ErrorText = Strings.Fill(Strings.UpdFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
        }
    }

    private void OnProgress(DownloadProgress p)
    {
        if (p.TotalBytes is not > 0)
        {
            IsProgressIndeterminate = true;
            return;
        }

        IsProgressIndeterminate = false;
        ProgressPercent = 100.0 * p.BytesReceived / p.TotalBytes.Value;
        ProgressText = Strings.Fill(Strings.UpdDownloading, ProgressPercent.ToString("0", CultureInfo.CurrentCulture) + " %");
    }

    private static void OpenReleasePage(UpdateInfo info)
    {
        try
        {
            Process.Start(new ProcessStartInfo(info.HtmlUrl.AbsoluteUri) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
        {
            // Aucun navigateur disponible : rien d'autre à faire.
        }
    }
}
