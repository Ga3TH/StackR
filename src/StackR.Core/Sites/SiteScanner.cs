namespace StackR.Core.Sites;

// Détecte les sites (sous-dossiers de www) et signale leurs changements.
public sealed class SiteScanner : IDisposable
{
    private const int DebounceMs = 500;

    private static readonly char[] GlobChars = ['[', ']', '*', '?'];

    private readonly string _wwwPath;
    private readonly object _gate = new();
    private FileSystemWatcher? _watcher;
    private bool _watchingParent;
    private Timer? _timer;
    private bool _disposed;
    private IReadOnlyList<string> _skipped = [];

    public SiteScanner(string wwwPath)
    {
        ArgumentException.ThrowIfNullOrEmpty(wwwPath);
        _wwwPath = wwwPath;
        _timer = new Timer(_ => Changed?.Invoke(), null, Timeout.Infinite, Timeout.Infinite);
        lock (_gate)
        {
            StartWatching();
        }
    }

    public event Action? Changed;

    /// <summary>Dossiers ignorés par le dernier <see cref="Scan"/>, avec la raison.</summary>
    public IReadOnlyList<string> Skipped
    {
        get
        {
            lock (_gate)
            {
                return _skipped;
            }
        }
    }

    // Apache interprète [ ] * ? dans <Directory> même entre guillemets : ces chemins sont refusés.
    internal static bool HasGlobCharacters(string path) => path.IndexOfAny(GlobChars) >= 0;

    /// <summary>Sites détectés ; une erreur d'accès renvoie ce qui a pu être lu, sans lever.</summary>
    public IReadOnlyList<Site> Scan()
    {
        lock (_gate)
        {
            // www peut être apparu après la construction.
            if (_watcher is null || _watchingParent)
            {
                StartWatching();
            }
        }

        var sites = new List<Site>();
        var skipped = new List<string>();
        try
        {
            if (Directory.Exists(_wwwPath))
            {
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var dir in ListFolders())
                {
                    if (!SiteName.TryFromFolder(dir.Name, out var name) || !seen.Add(name.Value))
                    {
                        continue;
                    }

                    var publicPath = Path.Combine(dir.FullName, "public");
                    var root = SafeExists(publicPath) ? publicPath : dir.FullName;
                    if (HasGlobCharacters(root))
                    {
                        seen.Remove(name.Value);
                        skipped.Add($"{dir.FullName} : le chemin contient un caractère générique Apache ([ ] * ?).");
                        continue;
                    }

                    sites.Add(new Site(name, dir.FullName, root));
                }
            }
        }
        catch (IOException)
        {
            // Dossier www illisible : on renvoie ce qui a été lu.
        }
        catch (UnauthorizedAccessException)
        {
        }

        lock (_gate)
        {
            _skipped = skipped;
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
            DisposeWatcher();
            _timer?.Dispose();
        }
    }

    private static bool SafeExists(string path)
    {
        try
        {
            return Directory.Exists(path);
        }
        catch (IOException)
        {
            return false;
        }
    }

    private IEnumerable<DirectoryInfo> ListFolders()
    {
        return new DirectoryInfo(_wwwPath).EnumerateDirectories()
            .Where(d => (d.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
            .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.Name, StringComparer.Ordinal);
    }

    private void DisposeWatcher()
    {
        if (_watcher is null)
        {
            return;
        }

        _watcher.EnableRaisingEvents = false;
        _watcher.Created -= OnFsEvent;
        _watcher.Deleted -= OnFsEvent;
        _watcher.Renamed -= OnFsEvent;
        _watcher.Error -= OnError;
        _watcher.Dispose();
        _watcher = null;
    }

    // Surveille www ; s'il n'existe pas encore, son parent (pour voir www apparaître). Appelé sous _gate.
    private void StartWatching()
    {
        if (_disposed)
        {
            return;
        }

        DisposeWatcher();
        string target;
        bool parent;
        if (Directory.Exists(_wwwPath))
        {
            target = _wwwPath;
            parent = false;
        }
        else
        {
            var up = Path.GetDirectoryName(Path.GetFullPath(_wwwPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            if (string.IsNullOrEmpty(up) || !Directory.Exists(up))
            {
                return;
            }

            target = up;
            parent = true;
        }

        try
        {
            var watcher = new FileSystemWatcher(target)
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
            };
            watcher.Created += OnFsEvent;
            watcher.Deleted += OnFsEvent;
            watcher.Renamed += OnFsEvent;
            watcher.Error += OnError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
            _watchingParent = parent;
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            // Pas de surveillance pour l'instant : une prochaine Scan() réessaiera.
            DisposeWatcher();
        }
    }

    // Dépassement de tampon ou dossier disparu : on recrée la surveillance et on notifie.
    private void OnError(object sender, ErrorEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            StartWatching();
            _timer?.Change(DebounceMs, Timeout.Infinite);
        }
    }

    private void OnFsEvent(object sender, FileSystemEventArgs e)
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            if (_watchingParent)
            {
                // Seule l'apparition de www nous intéresse.
                if (!string.Equals(Path.GetFullPath(e.FullPath), Path.GetFullPath(_wwwPath), StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                StartWatching();
                _timer?.Change(DebounceMs, Timeout.Infinite);
                return;
            }

            // Les fichiers à la racine de www ne sont pas des sites.
            if (e.ChangeType != WatcherChangeTypes.Deleted && File.Exists(e.FullPath))
            {
                return;
            }

            _timer?.Change(DebounceMs, Timeout.Infinite);
        }
    }
}
