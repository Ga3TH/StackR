using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using StackR.Core;
using StackR.Core.Manifests;
using StackR.Core.Services;
using Wpf.Ui.Controls;

namespace StackR.App.ViewModels;

/// <summary>Page « Base de données » : coordonnées de connexion MySQL, mot de passe root, client en terminal.</summary>
public sealed partial class DatabaseViewModel : PageViewModelBase, IDisposable
{
    private const string Mask = "••••••••••••";
    private static readonly TimeSpan ClipboardLifetime = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan OptionsFileLifetime = TimeSpan.FromSeconds(20);

    private readonly StackREngine _engine;
    private string? _password;
    private CancellationTokenSource? _clipboardTimer;

    public DatabaseViewModel(StackREngine engine)
        : base(Strings.NavDatabase, Strings.DatabaseSubtitle)
    {
        _engine = engine ?? throw new ArgumentNullException(nameof(engine));
        Refresh();
    }

    public string Host { get; } = "127.0.0.1";

    public string User { get; } = "root";

    [ObservableProperty]
    private int _port;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    private bool _isReady;

    [ObservableProperty]
    private string _emptyMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayedPassword), nameof(ShowHideLabel), nameof(EnvPreview))]
    private bool _isPasswordVisible;

    [ObservableProperty]
    private bool _isChanging;

    [ObservableProperty]
    private string _newPassword = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyPasswordCommand), nameof(OpenClientCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasMessage;

    [ObservableProperty]
    private string _message = string.Empty;

    [ObservableProperty]
    private InfoBarSeverity _messageSeverity = InfoBarSeverity.Informational;

    public bool IsEmpty => !IsReady;

    public string DisplayedPassword => _password is null ? string.Empty : IsPasswordVisible ? _password : Mask;

    public string ShowHideLabel => IsPasswordVisible ? Strings.DbHide : Strings.DbShow;

    /// <summary>Aperçu du bloc .env (mot de passe masqué tant qu'il n'est pas affiché).</summary>
    public string EnvPreview => BuildEnv(IsPasswordVisible ? _password : Mask);

    /// <summary>Relit l'état de MySQL : installé ? initialisé (mot de passe root présent) ?</summary>
    public void Refresh()
    {
        Port = _engine.Settings.MySqlPort;
        var installed = _engine.ActiveDirectory(ComponentKind.MySql) is not null;
        _password = installed ? _engine.GetMySqlRootPassword() : null;

        IsReady = installed && _password is not null;
        EmptyMessage = !installed ? Strings.DbEmptyNotInstalled : Strings.DbEmptyNotInitialized;
        if (!IsReady)
        {
            IsPasswordVisible = false;
            IsChanging = false;
        }

        OnPropertyChanged(nameof(DisplayedPassword));
        OnPropertyChanged(nameof(EnvPreview));
    }

    [RelayCommand]
    private void TogglePassword() => IsPasswordVisible = !IsPasswordVisible;

    [RelayCommand]
    private Task CopyPasswordAsync() => CopySensitiveAsync(_password, Strings.DbCopied);

    [RelayCommand]
    private Task CopyEnvAsync() => CopySensitiveAsync(_password is null ? null : BuildEnv(_password), Strings.DbEnvCopied);

    [RelayCommand]
    private void ToggleChange()
    {
        IsChanging = !IsChanging;
        NewPassword = string.Empty;
    }

    [RelayCommand]
    private void CancelChange()
    {
        IsChanging = false;
        NewPassword = string.Empty;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ApplyPasswordAsync()
    {
        ClearMessage();
        if (!IsMySqlRunning())
        {
            Show(Strings.DbErrNotRunning, InfoBarSeverity.Warning);
            return;
        }

        var mysqlDir = _engine.ActiveDirectory(ComponentKind.MySql);
        if (mysqlDir is null)
        {
            Refresh();
            return;
        }

        var next = string.IsNullOrEmpty(NewPassword) ? MySqlPasswordChanger.Generate() : NewPassword;
        if (MySqlPasswordChanger.Validate(next) is { } problem)
        {
            Show(problem, InfoBarSeverity.Warning);
            return;
        }

        IsBusy = true;
        try
        {
            await MySqlPasswordChanger.ChangeRootPasswordAsync(
                new ProcessRunner(_engine.Paths.Logs), _engine.Paths, _engine.Secrets, mysqlDir, _engine.Settings.MySqlPort, next, CancellationToken.None);
            NewPassword = string.Empty;
            IsChanging = false;
            Refresh();
            Show(Strings.DbPasswordChanged, InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or Win32Exception)
        {
            Show(UiFormat.Of(Strings.DbErrGeneric, ex.Message), InfoBarSeverity.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task OpenClientAsync()
    {
        ClearMessage();
        if (!IsMySqlRunning())
        {
            Show(Strings.DbClientNotRunning, InfoBarSeverity.Warning);
            return;
        }

        var mysqlDir = _engine.ActiveDirectory(ComponentKind.MySql);
        var mysqlExe = mysqlDir is null ? null : Path.Combine(mysqlDir, "bin", "mysql.exe");
        if (mysqlExe is null || !File.Exists(mysqlExe) || _password is null)
        {
            Show(UiFormat.Of(Strings.DbClientFailed, "mysql.exe"), InfoBarSeverity.Error);
            return;
        }

        string? optionsFile = null;
        try
        {
            var psi = MySqlTerminal.CreateStartInfo(_engine.Paths, mysqlExe, _engine.Settings.MySqlPort, _password, FindWindowsTerminal(), out optionsFile);
            using var process = Process.Start(psi);
            Show(Strings.DbClientOpened, InfoBarSeverity.Success);
        }
        catch (Exception ex) when (ex is Win32Exception or InvalidOperationException or IOException or UnauthorizedAccessException)
        {
            DeleteQuietly(optionsFile);
            optionsFile = null;
            Show(UiFormat.Of(Strings.DbClientFailed, ex.Message), InfoBarSeverity.Error);
        }

        if (optionsFile is not null)
        {
            // mysql lit le fichier d'options au démarrage : on le supprime peu après (il reste réservé à l'utilisateur).
            await Task.Delay(OptionsFileLifetime);
            DeleteQuietly(optionsFile);
        }
    }

    public void Dispose()
    {
        _clipboardTimer?.Cancel();
        _clipboardTimer?.Dispose();
    }

    private bool CanRun() => !IsBusy;

    private bool IsMySqlRunning() =>
        _engine.Services.Services.FirstOrDefault(s => s.Id == "mysql")?.Status == ServiceStatus.Running;

    private string BuildEnv(string? password) =>
        string.Join(
            Environment.NewLine,
            "DB_CONNECTION=mysql",
            $"DB_HOST={Host}",
            string.Create(CultureInfo.InvariantCulture, $"DB_PORT={Port}"),
            Strings.DbEnvPlaceholder,
            $"DB_USERNAME={User}",
            $"DB_PASSWORD={password}");

    /// <summary>Copie un texte sensible ; il est retiré du presse-papiers après 30 s s'il y est toujours.</summary>
    private async Task CopySensitiveAsync(string? text, string okMessage)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        ClearMessage();
        try
        {
            var data = new DataObject();
            data.SetText(text);
            // Demande à Windows de ne pas garder ce contenu dans l'historique du presse-papiers.
            data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(new byte[4]));
            Clipboard.SetDataObject(data, copy: true);
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            Show(Strings.DbCopyFailed, InfoBarSeverity.Error);
            return;
        }

        Show(okMessage, InfoBarSeverity.Success);

        _clipboardTimer?.Cancel();
        _clipboardTimer?.Dispose();
        var timer = _clipboardTimer = new CancellationTokenSource();
        try
        {
            await Task.Delay(ClipboardLifetime, timer.Token);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == text)
            {
                Clipboard.Clear();
            }
        }
        catch (Exception ex) when (ex is COMException or ExternalException)
        {
            // Presse-papiers verrouillé par une autre application : on ne force rien.
        }
    }

    private static string? FindWindowsTerminal()
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
                     .Append(Path.Combine(local, "Microsoft", "WindowsApps")))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), "wt.exe");
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch (ArgumentException)
            {
                // Entrée de PATH invalide : ignorée.
            }
        }

        return null;
    }

    private static void DeleteQuietly(string? file)
    {
        if (file is null)
        {
            return;
        }

        try
        {
            File.Delete(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Reste réservé à l'utilisateur courant ; balayé plus tard avec les fichiers temporaires.
        }
    }

    private void ClearMessage()
    {
        HasMessage = false;
        Message = string.Empty;
    }

    private void Show(string text, InfoBarSeverity severity)
    {
        Message = text;
        MessageSeverity = severity;
        HasMessage = true;
    }
}
