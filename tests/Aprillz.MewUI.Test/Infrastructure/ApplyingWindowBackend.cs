using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Window = Aprillz.MewUI.Window;

namespace MewUI.Test.Infrastructure;

/// <summary>
/// Headless backend that applies requested client sizes back to the window (optionally clamped to
/// a platform-style minimum), so sizing-transaction behavior is observable in unit tests.
/// </summary>
internal sealed class ApplyingWindowBackend : IWindowBackend
{
    public Window? Window;
    public double MinWidth;
    public double MinHeight;
    public int SetClientSizeCount;

    public nint Handle => 1;

    public void SetClientSize(double widthDip, double heightDip)
    {
        SetClientSizeCount++;
        Window?.SetClientSizeDip(Math.Max(widthDip, MinWidth), Math.Max(heightDip, MinHeight));
    }

    public void SetResizable(bool resizable) { }
    public void PresentSurface() { }
    public void Hide() { }
    public void Close() { }
    public void Invalidate(bool erase) { }
    public void SetTitle(string title) { }
    public void SetIcon(IconSource? icon) { }
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
