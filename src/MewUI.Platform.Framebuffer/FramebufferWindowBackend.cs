using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>
/// The single window of the framebuffer host. It always covers the whole screen, so the window properties a
/// desktop backend forwards to a window manager have nothing to act on here.
/// </summary>
internal sealed class FramebufferWindowBackend : IWindowBackend
{
    /// <summary>Handle of the one window; there is no native window, so any non-zero value identifies it.</summary>
    internal const nint WINDOW_HANDLE = 1;

    // Presses chain into a double click inside this time and distance, as on Windows and X11.
    private const uint MULTI_CLICK_DELAY_MS = 500;
    private const double MULTI_CLICK_SLOP_DIP = 4;

    private readonly FramebufferPlatformHost _host;
    private readonly IFramebufferOutput _output;
    private readonly ClickCountTracker _clickCountTracker = new();
    private readonly int[] _lastPressClickCounts = new int[5];
    private bool _shown;
    private bool _closedRaised;
    private bool _cleanupDone;
    private bool _disposed;
    private bool _leftDown;
    private bool _rightDown;
    private bool _middleDown;

    // Touch slots in progress with their last position; libinput reports no position on release.
    private readonly Dictionary<int, Point> _touches = new();

    // With touch off the first finger alone stands in for the mouse, as the X server and Windows do.
    private int _mouseTouchSlot = -1;

    internal FramebufferWindowBackend(FramebufferPlatformHost host, Window window, IFramebufferOutput output)
    {
        _host = host;
        _output = output;
        Window = window;

        // The screen belongs to this window alone and nothing else draws over it, so a presented frame stays on
        // screen until the next one; no lost frame ever has to be reported.
        window.PlatformReportsLostFrames = true;
    }

    internal Window Window { get; }

    /// <summary>Set by invalidation, cleared when the frame is drawn.</summary>
    internal bool NeedsRender { get; private set; } = true;

    internal bool IsShown => _shown && !_disposed;

    public nint Handle { get; private set; } = WINDOW_HANDLE;

    private double DpiScale => _output.WindowSurface.DpiScale;

    public void CreateSurface()
    {
        Window.SetDpi((uint)Math.Max(1, Math.Round(DpiScale * 96)));
        Window.SetClientSizeDip(_output.PixelWidth / DpiScale, _output.PixelHeight / DpiScale);
    }

    public void PresentSurface()
    {
        if (_shown || _disposed)
        {
            return;
        }

        _shown = true;
        NeedsRender = true;
        Render();
        SetWindowActive(true);
    }

    public void Hide()
    {
        CancelTouches();
        _shown = false;
    }

    public void Close()
    {
        if (Handle == 0 || !Window.RequestClose())
        {
            return;
        }

        RaiseClosedOnce();
        Cleanup();
    }

    public void Invalidate(bool erase)
    {
        if (Handle == 0)
        {
            return;
        }

        NeedsRender = true;
        _host.RequestWake();
    }

    /// <summary>Lays out and draws one frame onto the output's surface.</summary>
    internal void Render()
    {
        if (!IsShown || Handle == 0)
        {
            return;
        }

        NeedsRender = false;
        double widthDip = _output.PixelWidth / DpiScale;
        double heightDip = _output.PixelHeight / DpiScale;
        var oldSize = Window.ClientSize;
        bool sizeChanged = Math.Abs(oldSize.Width - widthDip) > 0.01 || Math.Abs(oldSize.Height - heightDip) > 0.01;
        if (sizeChanged)
        {
            Window.SetClientSizeDip(widthDip, heightDip);
            Window.InvalidateSizingTransaction();
        }

        Window.PerformLayout();
        if (sizeChanged)
        {
            Window.RaiseClientSizeChanged(widthDip, heightDip);
        }

        Window.RenderFrame(_output.WindowSurface);
    }

    internal void RoutePointerMove(Point screenPx, PointerType pointerType, ModifierKeys modifiers)
    {
        if (!IsShown)
        {
            return;
        }

        var position = ScreenToClient(screenPx);
        WindowInputRouter.MouseMove(Window, position, screenPx, _leftDown, _rightDown, _middleDown, modifiers, pointerType);
    }

    /// <summary>Routes one libinput touch sample; the core reads taps, scrolls and holds from the contacts.</summary>
    internal void RouteTouch(int slot, TouchPhase phase, Point screenPx, long timestamp, ModifierKeys modifiers)
    {
        if (!IsShown)
        {
            return;
        }

        switch (phase)
        {
            case TouchPhase.Down:
                _touches[slot] = screenPx;
                break;

            case TouchPhase.Move:
                if (!_touches.ContainsKey(slot))
                {
                    return;
                }

                _touches[slot] = screenPx;
                break;

            default:
                if (!_touches.Remove(slot, out screenPx))
                {
                    return;
                }

                break;
        }

        if (!TouchInputGate.IsSupported)
        {
            RouteTouchAsMouse(slot, phase, screenPx, modifiers);
            return;
        }

        var position = ScreenToClient(screenPx);
        switch (phase)
        {
            case TouchPhase.Down:
                WindowInputRouter.PointerDown(Window, slot, PointerType.Touch, position, screenPx, timestamp, modifiers);
                break;

            case TouchPhase.Move:
                WindowInputRouter.PointerMove(Window, slot, PointerType.Touch, position, screenPx, timestamp, modifiers);
                break;

            case TouchPhase.Up:
                WindowInputRouter.PointerUp(Window, slot, PointerType.Touch, position, screenPx, timestamp, modifiers);
                break;

            default:
                WindowInputRouter.PointerCancel(Window, slot);
                break;
        }
    }

    private void RouteTouchAsMouse(int slot, TouchPhase phase, Point screenPx, ModifierKeys modifiers)
    {
        if (phase == TouchPhase.Down)
        {
            if (_mouseTouchSlot < 0)
            {
                _mouseTouchSlot = slot;
                RoutePointerMove(screenPx, PointerType.Touch, modifiers);
                RoutePointerButton(screenPx, MouseButton.Left, isDown: true, PointerType.Touch, modifiers);
            }
        }
        else if (slot == _mouseTouchSlot)
        {
            if (phase == TouchPhase.Move)
            {
                RoutePointerMove(screenPx, PointerType.Touch, modifiers);
            }
            else
            {
                _mouseTouchSlot = -1;
                RoutePointerButton(screenPx, MouseButton.Left, isDown: false, PointerType.Touch, modifiers);
            }
        }
    }

    /// <summary>Ends every contact in progress; libinput sends nothing more for them once the window is gone.</summary>
    private void CancelTouches()
    {
        if (_touches.Count == 0)
        {
            return;
        }

        var slots = _touches.Keys.ToArray();
        _touches.Clear();
        if (TouchInputGate.IsSupported)
        {
            foreach (int slot in slots)
            {
                WindowInputRouter.PointerCancel(Window, slot);
            }
        }
        else if (_mouseTouchSlot >= 0)
        {
            _mouseTouchSlot = -1;
            WindowInputRouter.PointerCancel(Window);
        }
    }

    internal void RoutePointerButton(Point screenPx, MouseButton button, bool isDown, PointerType pointerType, ModifierKeys modifiers)
    {
        if (!IsShown)
        {
            return;
        }

        switch (button)
        {
            case MouseButton.Left:
                _leftDown = isDown;
                break;
            case MouseButton.Right:
                _rightDown = isDown;
                break;
            case MouseButton.Middle:
                _middleDown = isDown;
                break;
        }

        int clickCount = 1;
        int buttonIndex = (int)button;
        if ((uint)buttonIndex < (uint)_lastPressClickCounts.Length)
        {
            if (isDown)
            {
                int maxDistance = (int)Math.Round(MULTI_CLICK_SLOP_DIP * DpiScale);
                clickCount = _clickCountTracker.Update(button, (int)screenPx.X, (int)screenPx.Y,
                    unchecked((uint)Environment.TickCount64), MULTI_CLICK_DELAY_MS, maxDistance, maxDistance);
                _lastPressClickCounts[buttonIndex] = clickCount;
            }
            else
            {
                clickCount = Math.Max(1, _lastPressClickCounts[buttonIndex]);
            }
        }

        WindowInputRouter.MouseButton(Window, ScreenToClient(screenPx), screenPx, button, isDown,
            _leftDown, _rightDown, _middleDown, clickCount, modifiers, pointerType);
    }

    internal void RouteWheel(Point screenPx, Vector notches, ModifierKeys modifiers)
    {
        if (!IsShown)
        {
            return;
        }

        var position = ScreenToClient(screenPx);
        WindowInputRouter.UpdateMouseOver(Window, WindowInputRouter.HitTest(Window, position));
        WindowInputRouter.MouseWheel(Window, position, screenPx, notches, _leftDown, _rightDown, _middleDown, modifiers);
    }

    /// <summary>Routes a key the way the desktop backends do: preview, bubbling, access keys, then Tab navigation.</summary>
    internal void RouteKey(FramebufferKeyStroke stroke, bool isDown, bool isRepeat)
    {
        if (!IsShown || Window.EffectiveVisualRoot == null)
        {
            return;
        }

        var args = new KeyEventArgs(stroke.Key, stroke.PlatformKey, stroke.Modifiers, isRepeat);
        if (!isDown)
        {
            Window.RaisePreviewKeyUp(args);
            if (!args.Handled)
            {
                WindowInputRouter.KeyUp(Window, args);
            }

            Window.ProcessAccessKeyUp(args);
            Window.RequerySuggested();
            return;
        }

        Window.RaisePreviewKeyDown(args);
        if (!args.Handled)
        {
            WindowInputRouter.KeyDown(Window, args);
        }

        Window.ProcessAccessKeyDown(args);

        if (!args.Handled && args.Key == Key.Tab)
        {
            if (args.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                Window.FocusManager.MoveFocusPrevious();
            }
            else
            {
                Window.FocusManager.MoveFocusNext();
            }

            return;
        }

        if (!args.Handled
            && !string.IsNullOrEmpty(stroke.Text)
            && !args.Modifiers.HasFlag(ModifierKeys.Control)
            && !args.Modifiers.HasFlag(ModifierKeys.Alt)
            && !IsFilteredControlText(stroke.Text))
        {
            WindowInputRouter.TextInput(Window, new TextInputEventArgs(stroke.Text));
        }
    }

    // A lone control character (Backspace, Escape, Tab) is a key, not text; line breaks still type.
    private static bool IsFilteredControlText(string text)
        => text.Length == 1 && char.IsControl(text[0]) && text[0] != '\r' && text[0] != '\n';

    public void SetResizable(bool resizable) { }

    public void SetTitle(string title) { }

    public void SetIcon(IconSource? icon) { }

    public void SetClientSize(double widthDip, double heightDip) { }

    public Point GetPosition() => default;

    public void SetPosition(double leftDip, double topDip) { }

    public void SetPositionPx(int leftPx, int topPx) { }

    public void CaptureMouse() { }

    public void ReleaseMouseCapture() { }

    public Point ClientToScreen(Point clientPointDip) => new(clientPointDip.X * DpiScale, clientPointDip.Y * DpiScale);

    public Point ScreenToClient(Point screenPointPx) => new(screenPointPx.X / DpiScale, screenPointPx.Y / DpiScale);

    public void EnsureTheme(bool isDark) { }

    public void CenterOnOwner() { }

    public void Activate() => SetWindowActive(true);

    public void SetOwner(nint ownerHandle) { }

    public void SetEnabled(bool enabled) { }

    public void SetOpacity(double opacity) { }

    public void SetAllowsTransparency(bool allowsTransparency) { }

    public void SetCursor(CursorType cursorType) => _host.SetCursorVisible(cursorType != CursorType.None);

    public void SetImeMode(ImeMode mode) { }

    public void CancelImeComposition() { }

    private void SetWindowActive(bool active)
    {
        if (Window.IsActive == active)
        {
            return;
        }

        Window.SetIsActive(active);
        if (active)
        {
            Window.RaiseActivated();
        }
        else
        {
            Window.RaiseDeactivated();
        }
    }

    private void RaiseClosedOnce()
    {
        if (_closedRaised)
        {
            return;
        }

        _closedRaised = true;
        try { Window.RaiseClosed(); }
        catch (Exception ex) { Application.RouteLifecycleException(ex); }
    }

    private void Cleanup()
    {
        if (_cleanupDone)
        {
            return;
        }

        _cleanupDone = true;
        _shown = false;
        _touches.Clear();
        _mouseTouchSlot = -1;
        nint handle = Handle;

        try { Window.ReleaseWindowGraphicsResources(handle); }
        catch (Exception ex) { Application.RouteLifecycleException(ex); }

        try { _host.UnregisterWindow(this); }
        catch (Exception ex) { Application.RouteLifecycleException(ex); }

        try { Window.DisposeVisualTree(); }
        catch (Exception ex) { Application.RouteLifecycleException(ex); }

        Handle = 0;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Window.ClearMouseOverState();
        Window.ClearMouseCaptureState();
        RaiseClosedOnce();
        Cleanup();
    }
}
