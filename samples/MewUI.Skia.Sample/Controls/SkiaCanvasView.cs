using System.Runtime.InteropServices;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

using SkiaSharp;

namespace Aprillz.MewUI.Skia.Sample.Controls;

/// <summary>
/// Abstract base for the per-platform Skia canvas hosts. Owns the CPU fallback path, the
/// <see cref="PaintSurface"/> event, the self-sustaining redraw loop, and the
/// <see cref="IPixelBufferSource"/> implementation. Subclasses implement
/// <see cref="TryRenderGpu(IGraphicsContext, int, int)"/> for the GPU bridge that matches
/// their platform/backend.
/// </summary>
/// <remarks>
/// If <see cref="TryRenderGpu(IGraphicsContext, int, int)"/> ever returns <see langword="false"/>,
/// the base permanently demotes to CPU upload (Skia → pinned <c>byte[]</c> → backend
/// <see cref="IImage"/>) for the rest of the control's lifetime.
/// </remarks>
public abstract class SkiaCanvasView : FrameworkElement, IPixelBufferSource
{
    // --- CPU fallback state ---
    private byte[] _cpuBuffer = [];

    private GCHandle _cpuPin;
    private SKSurface? _cpuSurface;
    private int _cpuPixelWidth;
    private int _cpuPixelHeight;
    private int _cpuVersion;
    private IImage? _cpuImage;

    // True once the GPU path has been tried and failed, or the subclass declined the path
    // entirely (e.g. backend doesn't match). After demotion we render CPU forever.
    private bool _gpuDemoted;

    // Diagnostic: log only when the OnRender input parameters change so we don't spam the
    // debug output every frame. Useful for tracking monitor/DPI/resize events that trigger
    // surface recreation.
    private double _lastLoggedDpiScale = -1;
    private int _lastLoggedPixelWidth;
    private int _lastLoggedPixelHeight;
    private IGpuInteropInvalidationSource? _gpuInteropInvalidationSource;

    /// <summary>
    /// Fires per render pass with the Skia canvas already sized to the current bounds.
    /// Subscribers use <paramref name="info"/> for the surface dimensions in device pixels.
    /// </summary>
    public event Action<SKCanvas, SKImageInfo>? PaintSurface;

    /// <summary>
    /// When <see langword="true"/> (default), the control invalidates itself at the end of
    /// each render so the host re-enters <see cref="OnRender"/> on the next frame — required
    /// to keep continuously-animated Skia content updating. Without this, MewUI's
    /// <c>AnimationManager</c> resets the render loop to <c>OnRequest</c> mode whenever its
    /// own (non-Skia) animations are idle, freezing the PaintSurface callback.
    /// Set to <see langword="false"/> for static content that should only repaint on bounds /
    /// theme changes.
    /// </summary>
    public bool ContinuousAnimation { get; set; } = true;

    /// <summary>True while the control is rendering through its GPU path.</summary>
    public bool IsGpuPath => !_gpuDemoted;

    /// <summary>Human-readable name of the active path for status display.</summary>
    public abstract string PathDescription { get; }

    // --- IPixelBufferSource (only meaningful when CPU path is active) ---
    int IRasterSource.PixelWidth => _cpuPixelWidth;

    int IRasterSource.PixelHeight => _cpuPixelHeight;

    int IPixelBufferSource.StrideBytes => _cpuPixelWidth * 4;

    BitmapPixelFormat IPixelBufferSource.PixelFormat => BitmapPixelFormat.Bgra32;

    bool IPixelBufferSource.IsPremultiplied => true;

    bool IPixelBufferSource.HasAlpha => true;

    int IRasterSource.Version => _cpuVersion;

    PixelBufferLock IPixelBufferSource.Lock() => new(
        _cpuBuffer,
        _cpuPixelWidth,
        _cpuPixelHeight,
        _cpuPixelWidth * 4,
        BitmapPixelFormat.Bgra32,
        _cpuVersion,
        dirtyRegion: null,
        release: null);

    protected override Size MeasureContent(Size availableSize) => Size.Empty;

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        double dpiScale = context.DpiScale;
        int width = (int)Math.Max(1, Math.Ceiling(Bounds.Width * dpiScale));
        int height = (int)Math.Max(1, Math.Ceiling(Bounds.Height * dpiScale));

        if (dpiScale != _lastLoggedDpiScale || width != _lastLoggedPixelWidth || height != _lastLoggedPixelHeight)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[SkiaCanvasView] OnRender dpi={dpiScale:0.###} bounds={Bounds.Width:0.0}x{Bounds.Height:0.0} px={width}x{height} t={DateTime.Now:HH:mm:ss.fff}");
            _lastLoggedDpiScale = dpiScale;
            _lastLoggedPixelWidth = width;
            _lastLoggedPixelHeight = height;
        }

        if (!_gpuDemoted)
        {
            if (TryRenderGpu(context, width, height))
            {
                if (ContinuousAnimation) InvalidateVisual();
                return;
            }
            DisposeGpu();
            _gpuDemoted = true;
        }

        RenderCpu(context, width, height);

        // Self-sustaining redraw loop. MewUI's AnimationManager flips the global render-loop
        // back to OnRequest when its own (non-Skia) animations idle, which would otherwise
        // freeze Skia paints between frames. Invalidating from inside OnRender re-enters the
        // pipeline so the next frame is scheduled — VSync still paces it to the display
        // refresh, no busy-loop. Static content can disable via ContinuousAnimation = false.
        if (ContinuousAnimation) InvalidateVisual();
    }

    /// <summary>
    /// Subclass hook: attempt to render the Skia content through the platform-specific GPU
    /// bridge. Return <see langword="false"/> to permanently fall back to CPU upload. The
    /// implementation should invoke <see cref="InvokePaint(SKCanvas, SKImageInfo)"/> from
    /// inside its paint callback so the user delegate runs on the produced surface.
    /// </summary>
    protected abstract bool TryRenderGpu(IGraphicsContext context, int width, int height);

    /// <summary>Subclass hook: release GPU resources on demote-to-CPU or dispose.</summary>
    protected virtual void DisposeGpu() { }

    /// <summary>
    /// Subclass hook: reset platform GPU resources after backend interop affinity changes.
    /// </summary>
    protected virtual void OnGpuInteropInvalidatedCore(GpuInteropInvalidatedEventArgs e) { }

    /// <summary>
    /// Subclasses call this from inside their GPU paint callback to invoke the user delegate
    /// on the produced Skia surface.
    /// </summary>
    protected void InvokePaint(SKCanvas canvas, SKImageInfo info) => PaintSurface?.Invoke(canvas, info);

    private void RenderCpu(IGraphicsContext context, int width, int height)
    {
        EnsureCpuSurface(width, height);
        if (_cpuSurface is null) return;

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var canvas = _cpuSurface.Canvas;
        canvas.Clear(SKColors.Transparent);
        PaintSurface?.Invoke(canvas, info);
        canvas.Flush();

        // Bump version so MewUI's image cache re-uploads the buffer next sample.
        _cpuVersion++;

        var factory = GetGraphicsFactory();
        _cpuImage ??= factory.CreateImageView((IPixelBufferSource)this);

        context.DrawImage(_cpuImage, Bounds);
    }

    private void EnsureCpuSurface(int width, int height)
    {
        if (_cpuSurface != null && width == _cpuPixelWidth && height == _cpuPixelHeight)
        {
            return;
        }

        DisposeCpuSurface();

        _cpuPixelWidth = width;
        _cpuPixelHeight = height;
        _cpuBuffer = new byte[checked(width * height * 4)];
        _cpuPin = GCHandle.Alloc(_cpuBuffer, GCHandleType.Pinned);

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        _cpuSurface = SKSurface.Create(info, _cpuPin.AddrOfPinnedObject(), info.RowBytes);
    }

    private void DisposeCpuSurface()
    {
        _cpuImage?.Dispose();
        _cpuImage = null;
        _cpuSurface?.Dispose();
        _cpuSurface = null;
        if (_cpuPin.IsAllocated)
        {
            _cpuPin.Free();
        }
        _cpuBuffer = [];
        _cpuPixelWidth = 0;
        _cpuPixelHeight = 0;
    }

    protected override void OnDispose()
    {
        UnsubscribeGpuInteropInvalidation();
        DisposeGpu();
        DisposeCpuSurface();

        base.OnDispose();
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        UnsubscribeGpuInteropInvalidation();

        if (newRoot is Window window && window.GraphicsFactory is IGpuInteropInvalidationSource source)
        {
            _gpuInteropInvalidationSource = source;
            source.GpuInteropInvalidated += OnGpuInteropInvalidated;
        }
    }

    private void UnsubscribeGpuInteropInvalidation()
    {
        if (_gpuInteropInvalidationSource is null)
        {
            return;
        }

        _gpuInteropInvalidationSource.GpuInteropInvalidated -= OnGpuInteropInvalidated;
        _gpuInteropInvalidationSource = null;
    }

    private void OnGpuInteropInvalidated(object? sender, GpuInteropInvalidatedEventArgs e)
    {
        var dispatcher = Application.IsRunning ? Application.Current.Dispatcher : null;
        if (dispatcher is not null && !dispatcher.IsOnUIThread)
        {
            dispatcher.BeginInvoke(() => OnGpuInteropInvalidated(sender, e));
            return;
        }

        System.Diagnostics.Debug.WriteLine(
            $"[SkiaCanvasView] GPU interop invalidated: reason={e.Reason}, renderTargetDeviceChanged={e.RenderTargetDeviceChanged}, displayChanged={e.DisplayChanged}, externalResourceMismatch={e.ExternalResourceMismatch}");

        DisposeGpu();
        _gpuDemoted = false;
        OnGpuInteropInvalidatedCore(e);
        InvalidateVisual();
    }

    /// <summary>
    /// Instantiates the <see cref="SkiaCanvasView"/> subclass appropriate for the current OS.
    /// </summary>
    public static SkiaCanvasView CreateForCurrentPlatform()
    {
        if (OperatingSystem.IsWindows()) return new SkiaCanvasViewWin32();
        if (OperatingSystem.IsMacOS()) return new SkiaCanvasViewMacOS();
        return new SkiaCanvasViewX11();
    }
}
