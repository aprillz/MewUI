using Aprillz.MewUI.Input;

namespace Aprillz.MewUI;

/// <summary>
/// Owns the run-scoped mutable state of a single <see cref="Application.Run"/> (the window registry and
/// main-window identity) and tears the per-run state down in a fixed order when the run ends, so no
/// window or drag reference survives into the next run.
/// </summary>
internal sealed class ApplicationRuntime : IDisposable
{
    private readonly List<Window> _windows = new();
    private Window? _mainWindow;
    private bool _disposed;

    internal IReadOnlyList<Window> Windows => _windows;

    internal void SetMainWindow(Window window) => _mainWindow = window;

    internal void Register(Window window)
    {
        if (!_windows.Contains(window))
        {
            _windows.Add(window);
        }
    }

    internal void Unregister(Window window)
    {
        bool wasMainWindow = ReferenceEquals(window, _mainWindow);
        _windows.Remove(window);
        if (Application.ShouldShutdownAfterClose(Application.ShutdownMode, wasMainWindow, _windows.Count))
        {
            Application.Quit();
        }
    }

    // Theme broadcast iterates a snapshot so a handler that registers or unregisters a window mid-broadcast
    // neither adds nor skips a notification.
    internal Window[] SnapshotWindows() => _windows.ToArray();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // Drag reset must precede the registry clear: drag target resolution reads the live window
        // registry, so the registry stays populated until the drag session is torn down.
        WindowDragDropRouter.ResetForRuntimeEnd();
        _windows.Clear();
        _mainWindow = null;
    }
}
