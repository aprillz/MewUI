using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.Browser;

internal sealed class BrowserWindowBackend : IWindowBackend
{
    private readonly BrowserPlatformHost _host;
    private bool _disposed;
    private bool _shown;
    private bool _mouseCaptured;
    private readonly BrowserWindowSurface _surface = new();

    internal BrowserWindowBackend(BrowserPlatformHost host, Window window)
    {
        _host = host;
        Window = window;
    }

    internal Window Window { get; }

    /// <summary>Set by invalidation, cleared once the frame is drawn.</summary>
    internal bool NeedsRender { get; private set; } = true;
    internal uint Dpi => (uint)Math.Max(96, Math.Round(_surface.DpiScale * 96));
    public nint Handle => _disposed ? 0 : 1;

    public void CreateSurface()
    {
        ThrowIfDisposed();
        Window.SetDpi(Dpi);
    }

    public void PresentSurface()
    {
        ThrowIfDisposed();
        _shown = true;
        NeedsRender = true;
        RenderNow();
    }

    public void Hide() => _shown = false;
    public void Close() => Application.Shutdown();
    public void SetResizable(bool resizable) { }
    public void Invalidate(bool erase)
    {
        // Coalesce invalidations; the host loop decides when the next frame runs.
        NeedsRender = true;
        _host.RequestFrame();
    }
    public void SetTitle(string title) { }
    public void SetIcon(IconSource? icon) { }
    public void SetClientSize(double widthDip, double heightDip) { }
    public Point GetPosition() => default;
    public void SetPosition(double leftDip, double topDip) { }
    public void SetPositionPx(int leftPx, int topPx) { }
    public void CaptureMouse() => _mouseCaptured = true;
    public void ReleaseMouseCapture() => _mouseCaptured = false;
    public Point ClientToScreen(Point clientPointDip) => clientPointDip;
    public Point ScreenToClient(Point screenPointPx) => screenPointPx;
    public void EnsureTheme(bool isDark) { }
    public void CenterOnOwner() { }
    public void Activate() => Window.SetIsActive(true);
    public void SetOwner(nint ownerHandle) { }
    public void SetEnabled(bool enabled) { }
    public void SetOpacity(double opacity) { }
    public void SetAllowsTransparency(bool allowsTransparency) { }
    public void SetCursor(CursorType cursorType) { }
    public void SetImeMode(Input.ImeMode mode) { }
    public void CancelImeComposition() { }

    internal bool PointerMove(double x, double y, double screenX, double screenY, int buttons, ModifierKeys modifiers)
    {
        if (!_shown || _disposed) return false;
        WindowInputRouter.MouseMove(
            Window,
            new Point(x, y),
            new Point(screenX, screenY),
            leftDown: (buttons & 1) != 0,
            rightDown: (buttons & 2) != 0,
            middleDown: (buttons & 4) != 0,
            modifiers);
        return _mouseCaptured;
    }

    internal bool PointerButton(double x, double y, double screenX, double screenY, int button, int buttons,
        bool isDown, int clickCount, ModifierKeys modifiers)
    {
        if (!_shown || _disposed) return false;
        var mappedButton = button switch
        {
            1 => MouseButton.Middle,
            2 => MouseButton.Right,
            3 => MouseButton.XButton1,
            4 => MouseButton.XButton2,
            _ => MouseButton.Left,
        };
        WindowInputRouter.MouseButton(
            Window,
            new Point(x, y),
            new Point(screenX, screenY),
            mappedButton,
            isDown,
            leftDown: (buttons & 1) != 0,
            rightDown: (buttons & 2) != 0,
            middleDown: (buttons & 4) != 0,
            Math.Max(1, clickCount),
            modifiers);
        return _mouseCaptured;
    }

    internal void PointerWheel(double x, double y, double screenX, double screenY,
        double deltaX, double deltaY, int buttons, ModifierKeys modifiers)
    {
        if (!_shown || _disposed) return;
        WindowInputRouter.MouseWheel(
            Window,
            new Point(x, y),
            new Point(screenX, screenY),
            new Vector(deltaX, deltaY),
            leftDown: (buttons & 1) != 0,
            rightDown: (buttons & 2) != 0,
            middleDown: (buttons & 4) != 0,
            modifiers);
    }

    internal void PointerLeave()
    {
        if (!_mouseCaptured && !_disposed)
        {
            WindowInputRouter.UpdateMouseOver(Window, null);
        }
    }

    internal void PointerCancel()
    {
        if (_disposed) return;
        _mouseCaptured = false;
        Window.ReleaseMouseCapture();
        WindowInputRouter.UpdateMouseOver(Window, null);
    }

    internal bool KeyDown(string code, int platformKey, ModifierKeys modifiers, bool isRepeat)
    {
        if (!_shown || _disposed) return false;
        var args = new KeyEventArgs(BrowserKeyMap.Map(code), platformKey, modifiers, isRepeat);
        WindowInputRouter.KeyDown(Window, args);
        return args.Handled;
    }

    internal bool KeyUp(string code, int platformKey, ModifierKeys modifiers)
    {
        if (!_shown || _disposed) return false;
        var args = new KeyEventArgs(BrowserKeyMap.Map(code), platformKey, modifiers);
        WindowInputRouter.KeyUp(Window, args);
        return args.Handled;
    }

    internal bool TextInput(string text)
    {
        if (!_shown || _disposed || string.IsNullOrEmpty(text)) return false;

        var args = new TextInputEventArgs(text);
        Window.RaisePreviewTextInput(args);
        if (args.Handled)
        {
            return true;
        }

        if (Window.FocusManager.FocusedElement is ITextInputClient client)
        {
            client.HandleTextInput(args);
        }

        return args.Handled;
    }

    internal void SetFocus(bool focused)
    {
        if (_disposed) return;
        Window.SetIsActive(focused);
        if (!focused)
        {
            _mouseCaptured = false;
            Window.ClearMouseOverState();
        }
    }

    internal void RenderFrame(double cssWidth, double cssHeight, double devicePixelRatio, int pixelWidth, int pixelHeight)
    {
        if (!_shown || _disposed)
        {
            return;
        }

        double scale = devicePixelRatio > 0 ? devicePixelRatio : 1;
        uint dpi = (uint)Math.Max(96, Math.Round(scale * 96));
        bool resized = _surface.Update(pixelWidth, pixelHeight, scale);
        if (Window.Dpi != dpi)
        {
            Window.SetDpi(dpi);
            resized = true;
        }

        var widthDip = cssWidth > 0 ? cssWidth : pixelWidth / scale;
        var heightDip = cssHeight > 0 ? cssHeight : pixelHeight / scale;
        var oldSize = Window.ClientSize;
        if (Math.Abs(oldSize.Width - widthDip) > 0.01 || Math.Abs(oldSize.Height - heightDip) > 0.01)
        {
            Window.SetClientSizeDip(widthDip, heightDip);
            Window.InvalidateSizingTransaction();
            resized = true;
            Console.WriteLine($"[resize] surface={pixelWidth}x{pixelHeight} dip={widthDip:F1}x{heightDip:F1} was={oldSize.Width:F1}x{oldSize.Height:F1} dpi={dpi}");
        }

        if (resized)
        {
            NeedsRender = true;
        }

        RenderNow();
    }

    private void RenderNow()
    {
        if (!_shown || _disposed)
        {
            return;
        }

        NeedsRender = false;
        Window.PerformLayout();
        Window.RenderFrame(_surface);
    }

    public void Dispose()
    {
        _disposed = true;
        _shown = false;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

    private sealed class BrowserWindowSurface : IWindowSurface
    {
        public nint Handle => 1;
        public int PixelWidth { get; private set; } = 1356;
        public int PixelHeight { get; private set; } = 720;
        public double DpiScale { get; private set; } = 1;

        internal bool Update(int pixelWidth, int pixelHeight, double dpiScale)
        {
            pixelWidth = Math.Max(1, pixelWidth);
            pixelHeight = Math.Max(1, pixelHeight);
            dpiScale = dpiScale > 0 ? dpiScale : 1;
            bool changed = PixelWidth != pixelWidth || PixelHeight != pixelHeight || Math.Abs(DpiScale - dpiScale) > 0.001;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScale = dpiScale;
            return changed;
        }
    }
}
