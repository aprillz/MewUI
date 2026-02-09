namespace Aprillz.MewUI.Platform;

/// <summary>
/// Abstracts platform-specific services such as windowing, input, clipboard, dialogs, and UI dispatching.
/// </summary>
public interface IPlatformHost : IDisposable
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
    IUiDispatcher CreateDispatcher(nint windowHandle);

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
    /// <param name="hwnd">Native window handle.</param>
    uint GetDpiForWindow(nint hwnd);

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
    void DoEvents();
}
