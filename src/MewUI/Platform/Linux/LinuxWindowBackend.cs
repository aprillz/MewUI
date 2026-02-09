namespace Aprillz.MewUI.Platform.Linux;

internal sealed class LinuxWindowBackend : IWindowBackend
{
    private readonly Window _window;

    public LinuxWindowBackend(Window window) => _window = window;

    public nint Handle => 0;

    public void SetResizable(bool resizable) { }

    public void Show()
        => throw new PlatformNotSupportedException("Linux window backend is not implemented yet.");

    public void Hide() { }

    public void Close() { }

    public void Invalidate(bool erase) { }

    public void SetTitle(string title) { }

    public void SetIcon(IconSource? icon) { }

    public void SetClientSize(double widthDip, double heightDip) { }

    public void CaptureMouse() { }

    public void ReleaseMouseCapture() { }

    public Point ClientToScreen(Point clientPointDip)
        => throw new PlatformNotSupportedException("Linux window backend is not implemented yet.");

    public Point ScreenToClient(Point screenPointPx)
        => throw new PlatformNotSupportedException("Linux window backend is not implemented yet.");

    public void Dispose()
    {
        // No-op for scaffolding backend.
    }

    public void EnsureTheme(bool isDark)
    {
    }
}
