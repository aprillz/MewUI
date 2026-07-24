using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;

namespace Aprillz.MewUI.Preview;

/// <summary>
/// Headless window backend for preview sessions: no native window exists, invalidation feeds the
/// session's frame scheduler, and close runs the framework close sequence directly.
/// </summary>
internal sealed class PreviewWindowBackend : IWindowBackend
{
    // Distinct fake handles keep Handle-keyed framework paths (popup routing, owner checks) unambiguous.
    private static int _nextHandle;

    private readonly Window _window;
    private readonly PreviewSession _session;

    public PreviewWindowBackend(Window window, PreviewSession session)
    {
        _window = window;
        _session = session;
        Handle = Interlocked.Increment(ref _nextHandle);
    }

    public nint Handle { get; }

    public void CreateSurface()
    {
        // Native backends attach from their surface creation; mirroring that here applies the
        // window's spec (WindowSize, title, opacity) through the one shared path.
        _window.AttachBackend(this);
    }

    public void SetResizable(bool resizable) { }

    public void PresentSurface() => _session.NotifyPresented(_window);

    public void Hide() { }

    public void Close()
    {
        // Mirror the native close sequence (WM_CLOSE -> Closing -> destroy) synchronously.
        if (_window.RequestClose())
        {
            _window.RaiseClosed();
            _session.NotifyClosed(_window);
        }
    }

    public void Invalidate(bool erase) => _session.MarkDirty(_window);

    public void SetTitle(string title) { }

    public void SetIcon(IconSource? icon) { }

    public void SetClientSize(double widthDip, double heightDip)
    {
        // No native resize round-trip exists; commit the size directly and relayout on next frame.
        _window.SetClientSizeDip(widthDip, heightDip);
        _session.MarkDirty(_window);
    }

    public Point GetPosition() => default;

    public void SetPosition(double leftDip, double topDip) { }

    public void CaptureMouse() { }

    public void ReleaseMouseCapture() { }

    public Point ClientToScreen(Point clientPointDip) => clientPointDip;

    public Point ScreenToClient(Point screenPointPx) => screenPointPx;

    public void CenterOnOwner() { }

    public void EnsureTheme(bool isDark) { }

    public void Activate() { }

    public void SetOwner(nint ownerHandle) { }

    public void SetEnabled(bool enabled) { }

    public void SetOpacity(double opacity) { }

    public void SetAllowsTransparency(bool allowsTransparency) { }

    public void SetCursor(CursorType cursorType) { }

    public void SetImeMode(ImeMode mode) { }

    public void CancelImeComposition() { }

    public void Dispose() { }
}
