using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;

namespace MewUI.Test.Infrastructure;

/// <summary>
/// No-op window backend reporting a fake native handle, so Window.PerformLayout and the
/// popup pipeline (which gate on Handle != 0) run headless in unit tests.
/// </summary>
internal sealed class HeadlessWindowBackend : IWindowBackend
{
    public nint Handle => 1;

    public CursorType LastCursor { get; private set; } = CursorType.Arrow;

    public void SetResizable(bool resizable) { }

    public void PresentSurface() { }

    public void Hide() { }

    public void Close() { }

    public void Invalidate(bool erase) { }

    public void SetTitle(string title) { }

    public void SetIcon(IconSource? icon) { }

    public void SetClientSize(double widthDip, double heightDip) { }

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

    public void SetCursor(CursorType cursorType) => LastCursor = cursorType;

    public void SetImeMode(ImeMode mode) { }

    public void CancelImeComposition() { }

    public void Dispose() { }
}
