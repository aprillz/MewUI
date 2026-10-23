using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Headless;

/// <summary>
/// The kind of file dialog a headless session was asked for.
/// </summary>
public enum HeadlessFileDialogKind
{
    /// <summary>An open-file dialog.</summary>
    Open,

    /// <summary>A save-file dialog.</summary>
    Save,

    /// <summary>A folder selection dialog.</summary>
    Folder,
}

/// <summary>
/// One file dialog request a headless session answered.
/// </summary>
/// <param name="Kind">Which dialog was requested.</param>
/// <param name="Title">The title the caller asked for.</param>
/// <param name="Paths">The paths the session answered with; empty when it answered cancel.</param>
public sealed record HeadlessFileDialogCall(HeadlessFileDialogKind Kind, string Title, IReadOnlyList<string> Paths);

/// <summary>
/// Answers native file dialogs from queues of scripted paths and records every request.
/// </summary>
/// <remarks>
/// A request whose queue is empty is answered with cancel; it is still recorded.
/// </remarks>
public sealed class HeadlessFileDialogs : IFileDialogService
{
    private readonly object _gate = new();
    private readonly Queue<string[]> _open = new();
    private readonly Queue<string> _save = new();
    private readonly Queue<string> _folder = new();
    private readonly List<HeadlessFileDialogCall> _calls = new();

    internal HeadlessFileDialogs()
    {
    }

    /// <summary>Gets the requests answered so far, oldest first.</summary>
    public IReadOnlyList<HeadlessFileDialogCall> Calls
    {
        get
        {
            lock (_gate)
            {
                return _calls.ToArray();
            }
        }
    }

    /// <summary>Queues the paths the next open-file dialog returns; a single-select dialog takes the first.</summary>
    public void EnqueueOpen(params string[] paths)
    {
        ArgumentNullException.ThrowIfNull(paths);
        lock (_gate)
        {
            _open.Enqueue((string[])paths.Clone());
        }
    }

    /// <summary>Queues the path the next save-file dialog returns.</summary>
    public void EnqueueSave(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_gate)
        {
            _save.Enqueue(path);
        }
    }

    /// <summary>Queues the path the next folder dialog returns.</summary>
    public void EnqueueFolder(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        lock (_gate)
        {
            _folder.Enqueue(path);
        }
    }

    /// <summary>Drops queued answers and recorded requests.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _open.Clear();
            _save.Clear();
            _folder.Clear();
            _calls.Clear();
        }
    }

    bool IFileDialogService.IsNativeDialogAvailable() => true;

    string[]? IFileDialogService.OpenFile(OpenFileDialogOptions options)
    {
        lock (_gate)
        {
            string[]? paths = _open.Count > 0 ? _open.Dequeue() : null;
            if (paths != null && !options.Multiselect && paths.Length > 1)
            {
                paths = [paths[0]];
            }

            _calls.Add(new HeadlessFileDialogCall(HeadlessFileDialogKind.Open, options.Title, paths ?? []));
            return paths;
        }
    }

    string? IFileDialogService.SaveFile(SaveFileDialogOptions options)
    {
        lock (_gate)
        {
            string? path = _save.Count > 0 ? _save.Dequeue() : null;
            _calls.Add(new HeadlessFileDialogCall(HeadlessFileDialogKind.Save, options.Title, path == null ? [] : [path]));
            return path;
        }
    }

    string? IFileDialogService.SelectFolder(FolderDialogOptions options)
    {
        lock (_gate)
        {
            string? path = _folder.Count > 0 ? _folder.Dequeue() : null;
            _calls.Add(new HeadlessFileDialogCall(HeadlessFileDialogKind.Folder, options.Title, path == null ? [] : [path]));
            return path;
        }
    }
}
