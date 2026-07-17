namespace Aprillz.MewUI.Platform;

/// <summary>
/// Abstracts platform-specific services such as windowing, input, clipboard, dialogs, and UI dispatching.
/// </summary>
internal interface IPlatformHost : IDisposable
{
    /// <summary>
    /// Gets the platform message box service.
    /// </summary>
    IMessageBoxService MessageBox { get; }

    /// <summary>
    /// Gets the platform file dialog service.
    /// </summary>
    IFileDialogService FileDialog { get; }

    /// <summary>
    /// Gets the platform clipboard service.
    /// </summary>
    IClipboardService Clipboard { get; }

    /// <summary>
    /// Creates a window backend for the specified <see cref="Window"/>.
    /// </summary>
    /// <param name="window">The managed window.</param>
    IWindowBackend CreateWindowBackend(Window window);

    /// <summary>
    /// Creates a UI dispatcher associated with a native window handle.
    /// </summary>
    /// <param name="windowHandle">Native window handle.</param>
    IDispatcher CreateDispatcher(nint windowHandle);

    /// <summary>
    /// Gets the platform's default font family name.
    /// </summary>
    string DefaultFontFamily { get; }

    /// <summary>
    /// Gets the platform's default font fallback chain.
    /// Fonts are tried in order when a glyph is missing from the primary font.
    /// Returns an empty array if the platform handles fallback automatically.
    /// </summary>
    IReadOnlyList<string> DefaultFontFallbacks { get; }

    /// <summary>
    /// Gets the system DPI used when no window handle is available.
    /// </summary>
    uint GetSystemDpi();

    /// <summary>
    /// Gets the current system theme variant.
    /// </summary>
    ThemeVariant GetSystemThemeVariant();

    /// <summary>
    /// Gets the DPI value for a specific window.
    /// </summary>
    /// <param name="windowHandle">Native window handle.</param>
    uint GetDpiForWindow(nint windowHandle);

    /// <summary>
    /// Enables per-monitor DPI awareness if supported by the platform.
    /// </summary>
    bool EnablePerMonitorDpiAwareness();

    /// <summary>
    /// Returns a system metric value for the specified DPI.
    /// </summary>
    /// <param name="nIndex">Metric index.</param>
    /// <param name="dpi">DPI value.</param>
    int GetSystemMetricsForDpi(int nIndex, uint dpi);

    /// <summary>
    /// Runs the platform message loop for the application.
    /// </summary>
    /// <param name="app">Application instance.</param>
    /// <param name="mainWindow">Main window to show.</param>
    void Run(Application app, Window mainWindow);

    /// <summary>
    /// Requests that the platform message loop terminates.
    /// </summary>
    /// <param name="app">Application instance.</param>
    void Quit(Application app);

    /// <summary>
    /// Processes pending messages without entering a full message loop (best effort).
    /// </summary>
    [Obsolete("DoEvents will be removed. Await asynchronous work, use the dispatcher, or run an explicit nested loop via RunNestedLoop.")]
    void DoEvents();

    /// <summary>
    /// Runs a nested message loop that pumps platform events and rendering until <paramref name="keepRunning"/>
    /// returns false (or the application quits). Reuses the same per-frame pump as <see cref="Run"/>, so timers
    /// and animation keep ticking while nested. Used to implement synchronous modal dialogs (Window.ShowDialog).
    /// </summary>
    /// <param name="keepRunning">Evaluated before each frame; the loop exits when it returns false.</param>
    void RunNestedLoop(Func<bool> keepRunning)
        => throw new NotSupportedException("This platform host does not support nested modal loops.");

    /// <summary>
    /// Gets the current cursor position in screen-pixel coordinates (Y-down).
    /// Used by framework drag-and-drop to resolve which window the cursor is over during a session.
    /// </summary>
    Point GetCursorScreenPosition() => default;

    /// <summary>
    /// Gets the DPI of the monitor that contains the given screen-pixel point (Y-down). Lets callers place a
    /// new window at a screen position with correct per-monitor scaling. Defaults to the system DPI.
    /// </summary>
    uint GetDpiForPoint(Point screenPositionPx) => GetSystemDpi();

    /// <summary>
    /// Gets the work area (screen bounds minus reserved regions such as the taskbar) of the monitor
    /// containing the given screen-pixel point (Y-down), in screen pixels. Lets placement clamp a
    /// popup to its target monitor. Returns an empty rect when the platform cannot report it yet.
    /// </summary>
    Rect GetWorkAreaForPoint(Point screenPositionPx) => default;

    /// <summary>
    /// Whether the platform supports a click-through, non-activating, transparent top-level overlay window.
    /// When true, drag-and-drop uses a single such window that follows the cursor across windows and the desktop
    /// (continuous preview); when false it falls back to a per-window overlay (hidden between windows).
    /// </summary>
    bool SupportsTransparentOverlay => false;

    /// <summary>
    /// The OS shell icon provider (file-type icons, special-folder/drive icons). Platform hosts override with
    /// their native implementation; the default falls back to bundled vector icons. See platform-seam-plan.md.
    /// </summary>
    IShellIconProvider ShellIconProvider => NullShellIconProvider.Instance;

    /// <summary>
    /// The OS mounted-volume enumerator for the dialog's Locations. Platform hosts override with their native
    /// implementation; the default returns no volumes.
    /// </summary>
    IMountedVolumeProvider MountedVolumeProvider => EmptyMountedVolumeProvider.Instance;

    /// <summary>
    /// The OS shell sidebar layout (sections + entries) for the dialog. Platform hosts override with their
    /// shell convention (Finder / Explorer / freedesktop); the default is empty. Core stays agnostic of the
    /// per-OS layout - it only consumes <see cref="IPlacesProvider"/>.
    /// </summary>
    IPlacesProvider PlacesProvider => EmptyPlacesProvider.Instance;
}
