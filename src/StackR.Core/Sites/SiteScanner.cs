namespace StackR.Core.Sites;

// Détecte les sites (sous-dossiers de www) et signale leurs changements.
public sealed class SiteScanner : IDisposable
{
    private const int DebounceMs = 500;

    private readonly string _wwwPath;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private Timer? _timer;
    private bool _disposed;

    public SiteScanner(string wwwPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(wwwPath);
        _wwwPath = wwwPath;
        StartWatching();
    }

    public event Action? Changed;

    public IReadOnlyList<Site> Scan()
    {
        if (!Directory.Exists(_wwwPath))
        {
            return [];
        }

        var folders = new DirectoryInfo(_wwwPath).EnumerateDirectories()
            .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Name, StringComparer.Ordinal);

        var sites = new List<Site>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var dir in folders)
        {
            if (!SiteName.TryFromFolder(dir.Name, out var name) || !seen.Add(name.Value))
            {
                continue;
            }

            var publicPath = Path.Combine(dir.FullName, "public");
            sites.Add(new Site(name, dir.FullName, Directory.Exists(publicPath) ? publicPath : dir.FullName));
        }

        return sites;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _watcher?.Dispose();
            _timer?.Dispose();
        }
    }

    private void StartWatching()
    {
        if (!Directory.Exists(_wwwPath))
        {
            return;
        }

        _timer = new Timer(_ => Changed?.Invoke(), null, Timeout.Infinite, Timeout.Infinite);
        _watcher = new FileSystemWatcher(_wwwPath)
        {
            NotifyFilter = NotifyFilters.DirectoryName,
            IncludeSubdirectories = false,
        };
        _watcher.Created += OnFsEvent;
        _watcher.Deleted += OnFsEvent;
        _watcher.Renamed += OnFsEvent;
        _watcher.EnableRaisingEvents = true;
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        // Les fichiers à la racine de www ne sont pas des sites.
        if (e.ChangeType != WatcherChangeTypes.Deleted && File.Exists(e.FullPath))
        {
            return;
        }

        lock (_gate)
        {
            if (!_disposed)
            {
                _timer?.Change(DebounceMs, Timeout.Infinite);
            }
        }
    }
}
