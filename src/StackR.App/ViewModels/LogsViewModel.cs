using System.Collections.ObjectModel;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StackR.App.Resources;
using StackR.Core;
using StackR.Core.IO;

namespace StackR.App.ViewModels;

/// <summary>Fichier journal proposé dans le sélecteur.</summary>
public sealed record LogFileOption(string Name, string Path);

/// <summary>
/// Page « Journaux » : suivi en direct des 500 dernières lignes du journal choisi.
/// Le suivi (minuteur) ne tourne que pendant que la page est affichée (<see cref="Start"/> / <see cref="Stop"/>).
/// </summary>
public sealed partial class LogsViewModel : PageViewModelBase
{
    private const int MaxLines = 500;

    private readonly StackREngine _engine;
    private readonly DispatcherTimer _timer;
    private readonly List<string> _lines = [];
    private LogTail? _tail;
    private bool _polling;

    public LogsViewModel(StackREngine engine)
        : base(Strings.NavLogs, Strings.LogsSubtitle)
    {
        _engine = engine;
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(700) };
        _timer.Tick += async (_, _) => await PollAsync();
    }

    public ObservableCollection<LogFileOption> Files { get; } = [];

    [ObservableProperty]
    public partial LogFileOption? SelectedFile { get; set; }

    [ObservableProperty]
    public partial string LogText { get; set; } = "";

    [ObservableProperty]
    public partial bool Follow { get; set; } = true;

    [ObservableProperty]
    public partial bool HasFiles { get; set; }

    public bool HasNoFiles => !HasFiles;

    [ObservableProperty]
    public partial bool IsLogEmpty { get; set; } = true;

    [ObservableProperty]
    public partial string Status { get; set; } = "";

    /// <summary>Démarre le suivi (page affichée).</summary>
    public void Start()
    {
        RefreshFiles();
        if (!_timer.IsEnabled)
        {
            _timer.Start();
        }

        _ = PollAsync();
    }

    /// <summary>Arrête le suivi (page masquée) : plus de minuteur actif.</summary>
    public void Stop() => _timer.Stop();

    partial void OnHasFilesChanged(bool value) => OnPropertyChanged(nameof(HasNoFiles));

    partial void OnSelectedFileChanged(LogFileOption? value)
    {
        _tail = value is null ? null : new LogTail(value.Path, MaxLines);
        _lines.Clear();
        UpdateText();
        if (_tail is not null && _timer.IsEnabled)
        {
            _ = PollAsync();
        }
    }

    private IEnumerable<LogFileOption> Candidates()
    {
        var logs = _engine.Paths.Logs;
        yield return new(Strings.LogsApacheError, Path.Combine(logs, "apache-error.log"));
        yield return new(Strings.LogsApacheAccess, Path.Combine(logs, "apache-access.log"));
        yield return new(Strings.LogsApacheOutput, Path.Combine(logs, "httpd.log"));
        yield return new(Strings.LogsMySqlError, Path.Combine(logs, "mysql-error.log"));
        yield return new(Strings.LogsMySqlOutput, Path.Combine(logs, "mysqld.log"));
        yield return new(Strings.LogsMailpit, Path.Combine(logs, "mailpit.log"));
    }

    /// <summary>Met la liste à jour (fichiers existants seulement) sans toucher à la sélection si rien n'a changé.</summary>
    private void RefreshFiles()
    {
        var existing = Candidates().Where(c => File.Exists(c.Path)).ToList();
        if (!existing.Select(f => f.Path).SequenceEqual(Files.Select(f => f.Path)))
        {
            var currentPath = SelectedFile?.Path;
            Files.Clear();
            foreach (var file in existing)
            {
                Files.Add(file);
            }

            SelectedFile = Files.FirstOrDefault(f => f.Path == currentPath) ?? Files.FirstOrDefault();
        }

        HasFiles = Files.Count > 0;
    }

    private async Task PollAsync()
    {
        if (_polling)
        {
            return;
        }

        _polling = true;
        try
        {
            RefreshFiles();
            var tail = _tail;
            if (tail is null)
            {
                return;
            }

            var result = await Task.Run(tail.Poll);
            if (!ReferenceEquals(tail, _tail))
            {
                return; // Sélection changée pendant la lecture.
            }

            if (result.Reset)
            {
                _lines.Clear();
            }

            if (result.Reset || result.Lines.Count > 0)
            {
                _lines.AddRange(result.Lines);
                if (_lines.Count > MaxLines)
                {
                    _lines.RemoveRange(0, _lines.Count - MaxLines);
                }

                UpdateText();
            }
        }
        finally
        {
            _polling = false;
        }
    }

    private void UpdateText()
    {
        LogText = string.Join(Environment.NewLine, _lines);
        IsLogEmpty = _lines.Count == 0;
        Status = Strings.Fill(Strings.LogsLineCount, _lines.Count);
    }

    [RelayCommand]
    private void Clear()
    {
        _lines.Clear();
        UpdateText();
    }

    [RelayCommand]
    private void Copy()
    {
        if (_lines.Count == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(LogText);
            Status = Strings.LogsCopied;
        }
        catch (COMException)
        {
            Status = Strings.LogsCopyFailed;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            Directory.CreateDirectory(_engine.Paths.Logs);
            Process.Start(new ProcessStartInfo(_engine.Paths.Logs) { UseShellExecute = true })?.Dispose();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            Status = Strings.Fill(Strings.LogsOpenFailed, ex.Message);
        }
    }
}
