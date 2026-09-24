using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;

namespace MewUI.MewvalonEdit.Test.Infrastructure;

/// <summary>
/// A window that lays out, renders and routes input headless: a no-op backend reports a native
/// handle so the window runs its full pipeline without an OS window.
/// </summary>
internal static class HeadlessWindow
{
    public static Window Create(double width, double height)
    {
        var window = new Window();
        window.AttachBackend(new Backend());
        window.SetClientSizeDip(width, height);
        return window;
    }

    /// <summary>Moves the pointer to <paramref name="position"/> and clicks the left button there.</summary>
    public static void SendClick(this Window window, Point position)
    {
        WindowInputRouter.MouseMove(window, position, position, leftDown: false, rightDown: false, middleDown: false);
        WindowInputRouter.MouseButton(window, position, position, MouseButton.Left, isDown: true,
            leftDown: true, rightDown: false, middleDown: false, clickCount: 1, modifiers: ModifierKeys.None);
        WindowInputRouter.MouseButton(window, position, position, MouseButton.Left, isDown: false,
            leftDown: false, rightDown: false, middleDown: false, clickCount: 1, modifiers: ModifierKeys.None);
    }

    private sealed class Backend : IWindowBackend
    {
        public nint Handle => 1;

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

        public void SetPositionPx(int leftPx, int topPx) { }

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
}
