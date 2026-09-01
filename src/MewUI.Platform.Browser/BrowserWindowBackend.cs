using System.Diagnostics;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;

namespace Aprillz.MewUI.Platform.Browser;

internal sealed class BrowserWindowBackend : IWindowBackend
{
    private readonly BrowserPlatformHost _host;
    private bool _disposed;
    private bool _shown;
    private bool _mouseCaptured;
    // Element that scrolled for the active touch pan, kept so the gesture cannot change owner.
    private UIElement? _panTarget;

    // Stand-in line height for a text control that has not been laid out yet.
    private const double DEFAULT_CARET_HEIGHT_DIP = 16;

    // Movement too small for the scroll controller to act on, held over to the next pan event.
    private const double MIN_PAN_STEP_DIP = 0.5;
    private double _panBankX;
    private double _panBankY;

    // Where the pan routes and what it carries, kept so coasting can keep sending after the finger
    // is gone.
    private Point _panPoint;
    private Point _panScreenPoint;
    private ModifierKeys _panModifiers;

    // Coasting after the finger lifts. Speed decays as v0 * DECAY^seconds, and the distance covered
    // by a given time has a closed form, so each frame sends the gap between that and what it has
    // already sent; integrating per frame instead would make the travel depend on the frame
    // interval. Total travel is v0 / -ln(DECAY), so squaring the 0.15 that Avalonia's
    // ScrollGestureRecognizer and UIScrollView use halves the distance while the coast still leaves
    // the finger at the speed the finger had.
    private const double FLING_DECAY_PER_SECOND = 0.0225;
    private const double FLING_END_SPEED_DIP = 5;
    private const double FLING_MAX_SPEED_DIP = 4000;
    private const double FLING_SAMPLE_WINDOW_SECONDS = 0.1;
    private const int FLING_SAMPLE_CAPACITY = 8;

    // One finger movement and the interval it covered. A single last delta is too noisy on a touch
    // screen to read a throw from, so the release averages the trailing window of these.
    private readonly record struct PanSample(double DeltaX, double DeltaY, double Seconds);

    private readonly PanSample[] _panSamples = new PanSample[FLING_SAMPLE_CAPACITY];
    private int _panSampleCount;
    private int _panSampleNext;
    private long _panSampleTicks;

    private bool _flinging;
    private double _flingSpeed;
    private double _flingDirectionX;
    private double _flingDirectionY;
    private double _flingSentDistance;
    private long _flingStartTicks;
    private bool _advancingFling;
    private bool _swallowTouchRelease;

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
    // The page cancels its own composition on the hidden field; the host clears what the control
    // is showing so a discarded pre-edit does not stay on screen.
    public void CancelImeComposition() => CompositionEnd(string.Empty);

    internal void CompositionStart()
    {
        if (!_shown || _disposed) return;
        var args = new TextCompositionEventArgs();
        Window.RaisePreviewTextCompositionStart(args);
        if (!args.Handled && Window.FocusManager.FocusedElement is ITextCompositionClient client)
        {
            client.HandleTextCompositionStart(args);
        }
    }

    internal void CompositionUpdate(string text)
    {
        if (!_shown || _disposed) return;
        var args = new TextCompositionEventArgs(text);
        Window.RaisePreviewTextCompositionUpdate(args);
        if (!args.Handled && Window.FocusManager.FocusedElement is ITextCompositionClient client)
        {
            client.HandleTextCompositionUpdate(args);
        }
    }

    internal void CompositionEnd(string text)
    {
        if (!_shown || _disposed) return;
        var args = new TextCompositionEventArgs(text);
        Window.RaisePreviewTextCompositionEnd(args);
        if (!args.Handled && Window.FocusManager.FocusedElement is ITextCompositionClient client)
        {
            client.HandleTextCompositionEnd(args);
        }
    }

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
        bool isDown, int clickCount, ModifierKeys modifiers, PointerType pointerType)
    {
        if (!_shown || _disposed) return false;

        if (isDown)
        {
            if (_flinging)
            {
                // A press during coasting is spent stopping it. Letting the same tap through would
                // also activate whatever it landed on, which no touch platform does.
                StopFling();
                _swallowTouchRelease = true;
                return false;
            }

            _swallowTouchRelease = false;
        }
        else if (_swallowTouchRelease)
        {
            // Its press never reached a control, so a release on its own would be read against
            // whatever the previous gesture pressed.
            _swallowTouchRelease = false;
            return false;
        }

        _panTarget = null;
        _panBankX = 0;
        _panBankY = 0;
        ClearPanSamples();
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
            modifiers,
            pointerType);
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

    internal bool CaptureConsumesDrag()
    {
        if (!_shown || _disposed) return false;

        // A command source captures only to decide whether the release counts as a click, so a
        // touch drag may take the gesture away from it. Everything else that captures (sliders,
        // scroll bars, splitters, text selection, drag and drop, popup dismiss watches) captures
        // to consume the movement itself and has to keep it.
        return _mouseCaptured && Window.CapturedElement is not CommandSourceControl;
    }

    internal bool WantsTextInput()
        => _shown && !_disposed && Window.FocusManager.FocusedElement is Input.ITextInputClient;

    /// <summary>Reports the caret in surface coordinates (DIPs); false when nothing holds one.</summary>
    internal bool TryGetCaretRect(out double x, out double y, out double height)
    {
        x = 0;
        y = 0;
        height = 0;
        if (!_shown || _disposed || Window.FocusManager.FocusedElement is not ITextCompositionClient client)
        {
            return false;
        }

        int caretIndex = client is ITextCompositionEditor editor ? editor.CaretPosition : client.CompositionStartIndex;
        var rect = client.GetCharRectInWindow(caretIndex);
        x = rect.X;
        y = rect.Y;

        // Layout may not have run for the control yet, and a target with no height would let the
        // candidate list land on top of the line it belongs to instead of below it.
        height = rect.Height > 0 ? rect.Height : DEFAULT_CARET_HEIGHT_DIP;
        return true;
    }

    internal void PointerPan(double x, double y, double screenX, double screenY,
        double deltaXDip, double deltaYDip, ModifierKeys modifiers)
    {
        if (!_shown || _disposed) return;

        StopFling();
        _panPoint = new Point(x, y);
        _panScreenPoint = new Point(screenX, screenY);
        _panModifiers = modifiers;
        RecordPanSample(deltaXDip, deltaYDip);
        SendPan(deltaXDip, deltaYDip);
    }

    /// <summary>Scrolls by a finger movement; false means nothing took it and there is no room left.</summary>
    private bool SendPan(double deltaXDip, double deltaYDip)
    {
        double step = Application.Current.Theme.Metrics.ScrollWheelStep;
        if (step <= 0) return false;

        // Scrolling ignores a step worth less than half a DIP, so a slow drag or a high refresh rate
        // would have most of its movement thrown away one event at a time. Movement is banked until
        // it is worth forwarding, and only the part actually sent is taken out of the bank.
        _panBankX += deltaXDip;
        _panBankY += deltaYDip;
        double sendX = Math.Abs(_panBankX) >= MIN_PAN_STEP_DIP ? _panBankX : 0;
        double sendY = Math.Abs(_panBankY) >= MIN_PAN_STEP_DIP ? _panBankY : 0;
        if (sendX == 0 && sendY == 0)
        {
            // Banked, not refused: the movement is still owed and the next call carries it.
            return true;
        }

        _panBankX -= sendX;
        _panBankY -= sendY;

        // A notch is worth ScrollWheelStep DIPs, and notches may be fractional, so dividing the
        // finger delta by the step makes the content track the finger one to one.
        var delta = new Vector(sendX / step, sendY / step);

        // The gesture stays with whatever first scrolled for it. Hit-testing the start point again
        // every move would hand the gesture to any other scrollable that the scrolling brought under
        // it. A pinned target that stops handling (it ran out of room, or it was virtualized away)
        // falls back to the point, which is also how the gesture reaches an outer scrollable.
        var handler = WindowInputRouter.MouseWheel(
            Window, _panPoint, _panScreenPoint, delta, false, false, false, _panModifiers, _panTarget);
        if (handler == null && _panTarget != null)
        {
            handler = WindowInputRouter.MouseWheel(
                Window, _panPoint, _panScreenPoint, delta, false, false, false, _panModifiers, routeFrom: null);
        }

        if (handler != null)
        {
            _panTarget = handler;
        }

        return handler != null;
    }

    private void RecordPanSample(double deltaXDip, double deltaYDip)
    {
        long now = Stopwatch.GetTimestamp();
        double seconds = _panSampleTicks == 0 ? 0 : (now - _panSampleTicks) / (double)Stopwatch.Frequency;
        _panSampleTicks = now;
        _panSamples[_panSampleNext] = new PanSample(deltaXDip, deltaYDip, seconds);
        _panSampleNext = (_panSampleNext + 1) % FLING_SAMPLE_CAPACITY;
        if (_panSampleCount < FLING_SAMPLE_CAPACITY)
        {
            _panSampleCount++;
        }
    }

    private void ClearPanSamples()
    {
        _panSampleCount = 0;
        _panSampleNext = 0;
        _panSampleTicks = 0;
    }

    /// <summary>Lets a finished pan coast, from the speed the finger left it with.</summary>
    internal void StartFling()
    {
        if (!_shown || _disposed) return;

        bool estimated = TryEstimateVelocity(out double velocityX, out double velocityY);
        ClearPanSamples();
        if (!estimated)
        {
            return;
        }

        double speed = Math.Sqrt((velocityX * velocityX) + (velocityY * velocityY));
        if (speed < FLING_END_SPEED_DIP)
        {
            return;
        }

        _flinging = true;
        _flingSpeed = Math.Min(speed, FLING_MAX_SPEED_DIP);
        _flingDirectionX = velocityX / speed;
        _flingDirectionY = velocityY / speed;
        _flingSentDistance = 0;
        _flingStartTicks = Stopwatch.GetTimestamp();
        _host.RequestFrame();
    }

    private bool TryEstimateVelocity(out double velocityX, out double velocityY)
    {
        velocityX = 0;
        velocityY = 0;
        if (_panSampleCount < 2)
        {
            return false;
        }

        // A finger that came to rest before lifting leaves nothing but stale samples. That is a
        // hold, and reading the earlier movement would throw content the user had already parked.
        double idle = (Stopwatch.GetTimestamp() - _panSampleTicks) / (double)Stopwatch.Frequency;
        if (idle > FLING_SAMPLE_WINDOW_SECONDS)
        {
            return false;
        }

        double sumX = 0;
        double sumY = 0;
        double seconds = 0;
        for (int i = 0; i < _panSampleCount && seconds < FLING_SAMPLE_WINDOW_SECONDS; i++)
        {
            int index = ((_panSampleNext - 1 - i) % FLING_SAMPLE_CAPACITY + FLING_SAMPLE_CAPACITY) % FLING_SAMPLE_CAPACITY;
            var sample = _panSamples[index];
            if (sample.Seconds <= 0)
            {
                break;
            }

            sumX += sample.DeltaX;
            sumY += sample.DeltaY;
            seconds += sample.Seconds;
        }

        if (seconds <= 0)
        {
            return false;
        }

        velocityX = sumX / seconds;
        velocityY = sumY / seconds;
        return true;
    }

    /// <summary>Moves an active coast on for this frame; false means it is over.</summary>
    internal bool AdvanceFling()
    {
        // Driving the scroll runs application code, and a frame started from inside that would read
        // the distance already sent before this call finishes writing it.
        if (!_flinging || _advancingFling) return false;
        if (!_shown || _disposed)
        {
            StopFling();
            return false;
        }

        _advancingFling = true;
        try
        {
            return AdvanceFlingStep();
        }
        catch
        {
            // A throw out of the scroll would otherwise be repeated every frame for the rest of the
            // coast, which turns one failure into a stuck loop.
            StopFling();
            throw;
        }
        finally
        {
            _advancingFling = false;
        }
    }

    private bool AdvanceFlingStep()
    {
        double elapsed = (Stopwatch.GetTimestamp() - _flingStartTicks) / (double)Stopwatch.Frequency;
        double remaining = Math.Pow(FLING_DECAY_PER_SECOND, elapsed);
        if (_flingSpeed * remaining < FLING_END_SPEED_DIP)
        {
            StopFling();
            return false;
        }

        // The integral of the decay curve, so a long frame and two short ones cover the same ground
        // and a dropped frame loses no movement.
        double travelled = _flingSpeed * (remaining - 1) / Math.Log(FLING_DECAY_PER_SECOND);
        double step = travelled - _flingSentDistance;
        _flingSentDistance = travelled;
        if (step <= 0)
        {
            return true;
        }

        if (!SendPan(_flingDirectionX * step, _flingDirectionY * step))
        {
            // Nothing scrolled, so the content hit its end and there is nowhere left to coast.
            StopFling();
            return false;
        }

        return true;
    }

    private void StopFling()
    {
        if (!_flinging) return;

        _flinging = false;
        _flingSentDistance = 0;
        _panBankX = 0;
        _panBankY = 0;
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
        StopFling();
        ClearPanSamples();
        _mouseCaptured = false;
        _panTarget = null;
        _panBankX = 0;
        _panBankY = 0;
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
