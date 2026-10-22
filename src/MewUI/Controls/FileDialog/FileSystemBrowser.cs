namespace Aprillz.MewUI.Platform;

/// <summary>
/// Pure file-system model for the managed dialog; enumeration is synchronous for now.
/// </summary>
internal sealed class FileSystemBrowser
{
    private static string[] UNITS = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];
    private readonly List<string> _history = new();
    private List<FileSystemEntry> _entries = new();

    // Unfiltered enumeration result (hidden already applied). Filter changes re-filter this without re-enumerating.
    private List<FileSystemEntry> _rawEntries = new();

    private int _historyIndex = -1;

    // Each navigation bumps the epoch; a background load completing on a stale epoch is discarded.
    private int _epoch;

    private CancellationTokenSource? _cts;
    private bool _showHidden;
    private FileFilter? _activeFilter;

    public event Action? Changed;

    // Fired (UI thread) when a navigation starts, before the async load. Lets the view drop the stale
    // selection immediately so an inaccessible/slow drive click doesn't leave the previous item selected.
    public event Action? Navigating;

    public string CurrentDirectory { get; private set; } = string.Empty;

    public IReadOnlyList<FileSystemEntry> Entries => _entries;

    // True while a background enumeration is in flight (UI can show a spinner/overlay).
    public ObservableValue<bool> IsLoading { get; } = new(false);

    public bool ShowHidden
    {
        get => _showHidden;
        set
        {
            if (_showHidden == value)
            {
                return;
            }
            _showHidden = value;
            // Hidden inclusion is decided during enumeration, so a toggle must re-enumerate.
            Reload();
        }
    }

    public bool DirectoriesOnly { get; set; }

    public FileFilter? ActiveFilter
    {
        get => _activeFilter;
        set
        {
            _activeFilter = value;
            // Filter only affects which files are shown - re-filter the cached enumeration, no disk access.
            ApplyFilter();
            Changed?.Invoke();
        }
    }

    public bool CanGoBack => _historyIndex > 0;

    public bool CanGoForward => _historyIndex >= 0 && _historyIndex < _history.Count - 1;

    public static string FormatSize(long bytes)
    {
        double size = bytes;
        int unit = 0;
        while (size >= 1024 && unit < UNITS.Length - 1)
        {
            size /= 1024;
            unit++;
        }
        return unit == 0 ? $"{bytes} B" : $"{size:0.#} {UNITS[unit]}";
    }

    public void Navigate(string directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        Load(directory, resolve: true, pushHistory: true);
    }

    public void GoBack()
    {
        if (CanGoBack)
        {
            _historyIndex--;
            Load(_history[_historyIndex], resolve: false, pushHistory: false);
        }
    }

    public void GoForward()
    {
        if (CanGoForward)
        {
            _historyIndex++;
            Load(_history[_historyIndex], resolve: false, pushHistory: false);
        }
    }

    public void GoParent()
    {
        // Path.GetDirectoryName is pure string manipulation (no disk access), unlike Directory.GetParent which
        // calls Path.GetFullPath and can block on UNC paths.
        var parent = Path.GetDirectoryName(CurrentDirectory);
        if (!string.IsNullOrEmpty(parent) && !string.Equals(parent, CurrentDirectory, StringComparison.OrdinalIgnoreCase))
        {
            Load(parent, resolve: true, pushHistory: true);
        }
    }

    public void Reload() => Load(CurrentDirectory, resolve: false, pushHistory: false);

    // Cancels any in-flight enumeration and prevents stale results from being applied (call on dialog close).
    public void Cancel()
    {
        _epoch++;
        _cts?.Cancel();
    }

    // Background only. Normalizes and walks up to the nearest existing ancestor (or null if none).
    private static string? ResolveExisting(string directory, CancellationToken token)
    {
        try
        {
            directory = Path.GetFullPath(directory);
            while (!Directory.Exists(directory))
            {
                token.ThrowIfCancellationRequested();
                var parent = Path.GetDirectoryName(directory);
                if (string.IsNullOrEmpty(parent) || parent == directory)
                {
                    return null;
                }
                directory = parent;
            }
            return directory;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Lists the immediate subdirectory names of <paramref name="path"/> (honoring <see cref="ShowHidden"/>),
    /// sorted case-insensitively. Returns an empty list for an inaccessible path. Runs synchronously on the
    /// caller's thread; intended for small on-demand lists (breadcrumb dropdown).
    /// </summary>
    public IReadOnlyList<string> ListSubdirectories(string path)
    {
        var names = new List<string>();
        try
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                try
                {
                    if (!ShowHidden && (new DirectoryInfo(dir).Attributes & FileAttributes.Hidden) != 0)
                    {
                        continue;
                    }
                    names.Add(Path.GetFileName(dir));
                }
                catch
                {
                    // Skip entries we cannot stat.
                }
            }
        }
        catch
        {
            // Inaccessible directory: return whatever was gathered (possibly empty).
        }

        names.Sort(static (a, b) => string.Compare(a, b, StringComparison.OrdinalIgnoreCase));
        return names;
    }

    // Background only. Enumerates + stats entries; hidden is filtered here (a hidden toggle re-enumerates).
    private static List<FileSystemEntry> Enumerate(string directory, bool showHidden, CancellationToken token)
    {
        var directories = new List<FileSystemEntry>();
        var files = new List<FileSystemEntry>();

        try
        {
            foreach (var path in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    var info = new FileInfo(path);
                    bool isDir = (info.Attributes & FileAttributes.Directory) != 0;
                    if (!showHidden && (info.Attributes & FileAttributes.Hidden) != 0)
                    {
                        continue;
                    }
                    var entry = new FileSystemEntry(path, Path.GetFileName(path), isDir, isDir ? 0 : info.Length, info.LastWriteTime);
                    (isDir ? directories : files).Add(entry);
                }
                catch
                {
                    // Skip entries we cannot stat.
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // Inaccessible directory: return whatever was gathered (possibly empty).
        }

        directories.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        files.Sort(static (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));

        var result = new List<FileSystemEntry>(directories.Count + files.Count);
        result.AddRange(directories);
        result.AddRange(files);
        return result;
    }

    // All filesystem access (path normalization, existence checks, enumeration, stat) runs on a background thread
    // with cancellation + an epoch guard, so a slow/unresponsive mount never freezes the UI thread.
    private void Load(string directory, bool resolve, bool pushHistory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return;
        }

        _epoch++;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();

        int epoch = _epoch;
        var token = _cts.Token;
        bool showHidden = _showHidden;
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;

        IsLoading.Value = true;
        // Clear the current listing up front so an inaccessible / still-loading drive doesn't keep showing the
        // previous location's files and selection. Repopulated on success; stays empty if the load fails.
        _entries = new List<FileSystemEntry>();
        Navigating?.Invoke();

        Task.Run(() =>
        {
            var dir = resolve ? ResolveExisting(directory, token) : directory;
            if (dir == null)
            {
                return (Path: (string?)null, Raw: (List<FileSystemEntry>?)null);
            }

            var raw = Enumerate(dir, showHidden, token);
            return (Path: dir, Raw: raw);
        }, token).ContinueWith(task =>
        {
            void Apply()
            {
                if (epoch != _epoch)
                {
                    return; // a newer navigation superseded this one
                }
                if (task.IsCanceled || task.IsFaulted)
                {
                    IsLoading.Value = false;
                    return;
                }

                var (path, raw) = task.Result;
                if (path == null || raw == null)
                {
                    IsLoading.Value = false;
                    return;
                }

                CurrentDirectory = path;
                if (pushHistory)
                {
                    if (_historyIndex < _history.Count - 1)
                    {
                        _history.RemoveRange(_historyIndex + 1, _history.Count - _historyIndex - 1);
                    }
                    if (_history.Count == 0 || !string.Equals(_history[^1], path, StringComparison.OrdinalIgnoreCase))
                    {
                        _history.Add(path);
                        _historyIndex = _history.Count - 1;
                    }
                }

                _rawEntries = raw;
                ApplyFilter();
                IsLoading.Value = false;
                Changed?.Invoke();
            }

            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(Apply);
            }
            else
            {
                Apply();
            }
        }, CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    // UI thread, no disk access: directories always shown; files filtered by the active filter / mode.
    private void ApplyFilter()
    {
        var list = new List<FileSystemEntry>(_rawEntries.Count);
        foreach (var entry in _rawEntries)
        {
            if (entry.IsDirectory)
            {
                list.Add(entry);
            }
            else if (!DirectoriesOnly && (_activeFilter == null || _activeFilter.Matches(entry.Name)))
            {
                list.Add(entry);
            }
        }
        _entries = list;
    }
}

internal readonly record struct FileSystemEntry(string FullPath, string Name, bool IsDirectory, long Size, DateTime Modified);

/// <summary>One entry of the file dialog's places sidebar.</summary>
/// <param name="Label">Text shown for the entry.</param>
/// <param name="Path">File system path the entry opens; empty for a header.</param>
/// <param name="Kind">Icon kind drawn when no shell icon is available.</param>
/// <param name="Place">Semantic place used to resolve a distinctive shell icon.</param>
/// <param name="IsHeader">Whether the entry is a section header rather than a navigable place.</param>
public readonly record struct PlaceItem(string Label, string Path, FileIconKind Kind, ShellPlaceKind Place = ShellPlaceKind.Folder, bool IsHeader = false);
