using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Owns the run-scoped mutable state of a single <see cref="Application.Run(Window)"/> (the window registry and
/// main-window identity) and tears the per-run state down in a fixed order when the run ends, so no
/// window or drag reference survives into the next run.
/// </summary>
internal sealed class ApplicationRuntime : IDisposable
{
    private readonly List<Window> _windows = new();
    private readonly Action<RenderCacheMaintenanceMode> _maintainRenderCaches;
    private readonly object _maintenanceGate = new();
    private IDispatcherCore? _maintenanceDispatcher;
    private IDisposable? _scheduledMaintenance;
    private bool _disposed;

    internal ApplicationRuntime(Action<RenderCacheMaintenanceMode> maintainRenderCaches)
    {
        _maintainRenderCaches = maintainRenderCaches
            ?? throw new ArgumentNullException(nameof(maintainRenderCaches));
    }

    internal IReadOnlyList<Window> Windows => _windows;

    internal Window? MainWindow { get; set; }

    internal void StartRenderCacheMaintenance(IDispatcher? dispatcher)
    {
        // Production dispatchers implement the timer scheduler. Some embedders and lightweight
        // test dispatchers expose only IDispatcher; they still get frame/close/shutdown maintenance.
        if (dispatcher is not IDispatcherCore core)
        {
            return;
        }

        lock (_maintenanceGate)
        {
            if (_disposed || _maintenanceDispatcher != null)
            {
                return;
            }

            _maintenanceDispatcher = core;
            _scheduledMaintenance = core.Schedule(TimeSpan.FromSeconds(1), MaintainIdleRenderCaches);
        }
    }

    internal void Register(Window window)
    {
        if (!_windows.Contains(window))
        {
            _windows.Add(window);
        }
    }

    internal void Unregister(Window window, ShutdownMode shutdownMode)
    {
        bool wasMainWindow = ReferenceEquals(window, MainWindow);
        _windows.Remove(window);
        if (Application.ShouldShutdownAfterClose(shutdownMode, wasMainWindow, _windows.Count))
        {
            Application.Shutdown();
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

        lock (_maintenanceGate)
        {
            _scheduledMaintenance?.Dispose();
            _scheduledMaintenance = null;
            _maintenanceDispatcher = null;
        }
        try
        {
            _maintainRenderCaches(RenderCacheMaintenanceMode.Shutdown);
        }
        finally
        {
            // Drag reset must precede the registry clear: drag target resolution reads the live window
            // registry, so the registry stays populated until the drag session is torn down.
            WindowDragDropRouter.ResetForRuntimeEnd();
            _windows.Clear();
            MainWindow = null;
        }
    }

    private void MaintainIdleRenderCaches()
    {
        lock (_maintenanceGate)
        {
            _scheduledMaintenance = null;
            if (_disposed || _maintenanceDispatcher is null)
            {
                return;
            }
        }

        try
        {
            _maintainRenderCaches(RenderCacheMaintenanceMode.Idle);
        }
        finally
        {
            lock (_maintenanceGate)
            {
                if (!_disposed && _maintenanceDispatcher is { } dispatcher)
                {
                    _scheduledMaintenance = dispatcher.Schedule(
                        TimeSpan.FromSeconds(1),
                        MaintainIdleRenderCaches);
                }
            }
        }
    }
}
