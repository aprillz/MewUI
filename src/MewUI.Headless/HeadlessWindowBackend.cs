using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Headless;

/// <summary>
/// Window backend of a headless session: no native window exists, the size and cursor are only
/// remembered, invalidation asks the host for a frame, and close runs the framework close sequence.
/// </summary>
internal sealed class HeadlessWindowBackend : IWindowBackend
{
    // Distinct fake handles keep Handle-keyed framework paths (popup routing, owner checks) unambiguous.
    private static int _nextHandle;

    private readonly Window _window;
    private readonly HeadlessPlatformHost _host;
    private IRenderSurface? _surface;
    private bool _surfaceCreated;
    private bool _disposed;

    public HeadlessWindowBackend(Window window, HeadlessPlatformHost host)
    {
        _window = window;
        _host = host;
        Handle = Interlocked.Increment(ref _nextHandle);
    }

    public nint Handle { get; }

    public Window Window => _window;

    public bool NeedsRender { get; private set; }

    public bool IsLive => _surfaceCreated && !_disposed;

    public CursorType LastCursor { get; private set; } = CursorType.Arrow;

    public void CreateSurface()
    {
        if (_surfaceCreated)
        {
            return;
        }

        _surfaceCreated = true;
        _window.SetDpi(_host.Dpi);
        // Native backends attach from their surface creation; mirroring that here applies the
        // window's spec (WindowSize, title, opacity) through the one shared path.
        _window.AttachBackend(this);
        _host.Register(this);
    }

    public void SetResizable(bool resizable) { }

    public void PresentSurface() => Invalidate(erase: false);

    public void Hide() { }

    public void Close()
    {
        // Mirror the native close sequence (close request -> Closing -> destroy) synchronously.
        if (!_window.RequestClose())
        {
            return;
        }

        _window.RaiseClosed();
        _window.ReleaseWindowGraphicsResources(Handle);
        Dispose();
    }

    public void Invalidate(bool erase)
    {
        NeedsRender = true;
        _host.RequestRender();
    }

    public void SetTitle(string title) { }

    public void SetIcon(IconSource? icon) { }

    public void SetClientSize(double widthDip, double heightDip)
    {
        // No native resize round-trip exists; commit the size directly and lay out on the next frame.
        _window.SetClientSizeDip(widthDip, heightDip);
        Invalidate(erase: false);
    }

    public Point GetPosition() => default;

    public void SetPosition(double leftDip, double topDip) { }

    public void SetPositionPx(int leftPx, int topPx) { }

    public void CaptureMouse() { }

    public void ReleaseMouseCapture() { }

    public Point ClientToScreen(Point clientPointDip) => clientPointDip;

    public Point ScreenToClient(Point screenPointPx) => screenPointPx;

    public void CenterOnOwner() { }

    public void EnsureTheme(bool isDark) { }

    public void Activate() => _window.SetIsActive(true);

    public void SetOwner(nint ownerHandle) { }

    public void SetEnabled(bool enabled) { }

    public void SetOpacity(double opacity) { }

    public void SetAllowsTransparency(bool allowsTransparency) { }

    public void SetCursor(CursorType cursorType) => LastCursor = cursorType;

    public void SetImeMode(ImeMode mode) { }

    public void CancelImeComposition() { }

    /// <summary>Lays the window out and draws one frame, into the offscreen surface in render mode.</summary>
    public void RenderNow()
    {
        NeedsRender = false;
        if (!IsLive || _window.Handle == 0)
        {
            return;
        }

        // GPU backends need a current context on this thread for surface creation and rendering;
        // CPU backends return a no-op scope.
        using var renderScope = _window.GraphicsFactory.AcquireBackgroundRenderScope();
        _window.PerformLayout();
        _window.RenderFrameToSurface(EnsureSurface());
    }

    /// <summary>Returns the pixels of the last frame, drawing one first when none is current.</summary>
    public HeadlessFrame CaptureFrame()
    {
        if (NeedsRender || _surface == null)
        {
            RenderNow();
        }

        return ReadPixels(_surface ?? EnsureSurface());
    }

    /// <summary>Draws the window straight from its visuals into a surface of its own and returns the pixels.</summary>
    public HeadlessFrame CaptureReferenceFrame()
    {
        using var renderScope = _window.GraphicsFactory.AcquireBackgroundRenderScope();
        _window.PerformLayout();
        var current = EnsureSurface();
        using var reference = _window.GraphicsFactory.CreateSurface(DescribeSurface(current.PixelWidth, current.PixelHeight, current.DpiScale));
        _window.RenderReferenceFrameToSurface(reference);
        return ReadPixels(reference);
    }

    private IRenderSurface EnsureSurface()
    {
        double dpiScale = _window.DpiScale > 0 ? _window.DpiScale : 1.0;
        var clientSize = _window.ClientSize;
        int widthPx = Math.Max(1, (int)Math.Round(clientSize.Width * dpiScale));
        int heightPx = Math.Max(1, (int)Math.Round(clientSize.Height * dpiScale));
        if (_surface != null && _surface.PixelWidth == widthPx && _surface.PixelHeight == heightPx && _surface.DpiScale == dpiScale)
        {
            return _surface;
        }

        _surface?.Dispose();
        if (_host.Rendering == HeadlessRendering.Offscreen)
        {
            _surface = _window.GraphicsFactory.CreateSurface(DescribeSurface(widthPx, heightPx, dpiScale));
        }
        else
        {
            _surface = new NoOpRenderSurface(widthPx, heightPx, dpiScale);
        }

        return _surface;
    }

    private static RenderSurfaceDescriptor DescribeSurface(int widthPx, int heightPx, double dpiScale)
        => RenderSurfaceDescriptor.Offscreen(widthPx, heightPx, dpiScale, hasAlpha: false, debugName: "Headless window frame");

    private HeadlessFrame ReadPixels(IRenderSurface surface)
    {
        if (_host.Rendering != HeadlessRendering.Offscreen)
        {
            throw new InvalidOperationException("Frames can only be captured from a session started with HeadlessRendering.Offscreen.");
        }

        int width = surface.PixelWidth;
        int height = surface.PixelHeight;
        int stride = width * 4;
        var pixels = new byte[stride * height];

        // A GL backend reads its framebuffer back on the thread that holds the context.
        using var renderScope = _window.GraphicsFactory.AcquireBackgroundRenderScope();
        if (surface is IDeferredCpuReadableSurface deferred)
        {
            using var readback = deferred.RequestReadback();
            readback.Wait();
            deferred.TryFlushReadback();
        }

        if (surface is ICpuPixelSurface cpu)
        {
            var source = cpu.GetReadOnlyPixelSpan();
            for (int row = 0; row < height; row++)
            {
                source.Slice(row * cpu.StrideBytes, stride).CopyTo(pixels.AsSpan(row * stride, stride));
            }
        }
        else if (!_window.GraphicsFactory.TryReadPixels(surface, pixels, stride))
        {
            throw new NotSupportedException($"The {_window.GraphicsFactory.Backend} backend cannot read the pixels of its offscreen surface.");
        }

        return new HeadlessFrame(width, height, stride, surface.DpiScale, pixels);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _host.Unregister(this);
        _surface?.Dispose();
        _surface = null;
    }
}
