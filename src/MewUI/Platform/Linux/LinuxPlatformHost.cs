namespace Aprillz.MewUI.Platform.Linux;

/// <summary>
/// Experimental Linux platform host.
/// This currently provides the scaffolding required for future X11/Wayland backends.
/// </summary>
public sealed class LinuxPlatformHost : IPlatformHost
{
    public IMessageBoxService MessageBox { get; } = new LinuxMessageBoxService();

    public IFileDialogService FileDialog { get; } = new LinuxFileDialogService();

    public IClipboardService Clipboard { get; } = new LinuxClipboardService();

    public IWindowBackend CreateWindowBackend(Window window) => new LinuxWindowBackend(window);

    public IUiDispatcher CreateDispatcher(nint windowHandle) => new LinuxUiDispatcher();

    public uint GetSystemDpi() => 96u;

    public ThemeVariant GetSystemThemeVariant() => LinuxThemeDetector.DetectSystemThemeVariant();

    public uint GetDpiForWindow(nint hwnd) => 96u;

    public bool EnablePerMonitorDpiAwareness() => false;

    public int GetSystemMetricsForDpi(int nIndex, uint dpi) => 0;

    public void Run(Application app, Window mainWindow)
        => throw new PlatformNotSupportedException("Linux platform host is not implemented yet. (X11/Wayland + rendering backend work pending)");

    public void Quit(Application app)
    { }

    public void DoEvents()
    { }

    public void Dispose()
    { }
}
