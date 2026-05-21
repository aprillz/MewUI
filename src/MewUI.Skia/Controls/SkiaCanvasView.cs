using System.Runtime.InteropServices;

using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

using SkiaSharp;

namespace Aprillz.MewUI.Skia.Controls;

/// <summary>
/// MewUI control that hosts Skia rendering. Resolves the active backend's
/// <see cref="ISkiaInteropProvider"/> from <see cref="SkiaInterop"/> at first render; if no
/// provider is registered or the host fails, demotes to a CPU upload path (Skia →
/// pinned <c>byte[]</c> → backend <see cref="IImage"/>) for the remainder of the
/// control's lifetime.
/// </summary>
public class SkiaCanvasView : FrameworkElement, IPixelBufferSource
{
    private byte[] _cpuBuffer = [];
    private GCHandle _cpuPin;
    private SKSurface? _cpuSurface;
    private int _cpuPixelWidth;
    private int _cpuPixelHeight;
    private int _cpuVersion;
    private IImage? _cpuImage;

    private ISkiaSurfaceHost? _gpuHost;
    private bool _gpuResolved;
    private bool _gpuDemoted;
    private IGpuInteropInvalidationSource? _gpuInteropInvalidationSource;

    /// <summary>Fires per render pass with the Skia canvas already sized to the current bounds.</summary>
    public event Action<SKCanvas, SKImageInfo>? PaintSurface;

    /// <summary>
    /// When <see langword="true"/> (default), the control invalidates itself at the end of each
    /// render so animated Skia content keeps repainting even when MewUI's AnimationManager flips
    /// the global render loop back to OnRequest mode.
    /// </summary>
    public bool ContinuousAnimation { get; set; } = true;

    public bool IsGpuPath => !_gpuDemoted;

    /// <summary>Pre-paint clear color. <see langword="null"/> = Clear(Transparent). Opaque (A=255) unlocks fast present (GDI SRCCOPY).</summary>
    public Color? Background { get; set; }

    public string PathDescription => _gpuDemoted
        ? "CPU upload (Skia → byte[] → backend)"
        : (_gpuHost is not null ? _gpuHost.Description : "Pending");

    int IRasterSource.PixelWidth => _cpuPixelWidth;
    int IRasterSource.PixelHeight => _cpuPixelHeight;
    int IPixelBufferSource.StrideBytes => _cpuPixelWidth * 4;
    bool IPixelBufferSource.IsPremultiplied => true;
    bool IPixelBufferSource.HasAlpha => true;
    int IRasterSource.Version => _cpuVersion;

    PixelBufferLock IPixelBufferSource.Lock() => new(
        _cpuBuffer, _cpuPixelWidth, _cpuPixelHeight, _cpuPixelWidth * 4,
        _cpuVersion, dirtyRegion: null, release: null);

    protected override Size MeasureContent(Size availableSize) => Size.Empty;

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        double dpiScale = context.DpiScale;
        int width = (int)Math.Max(1, Math.Ceiling(Bounds.Width * dpiScale));
        int height = (int)Math.Max(1, Math.Ceiling(Bounds.Height * dpiScale));

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
        if (ContinuousAnimation) InvalidateVisual();
    }

    private bool TryRenderGpu(IGraphicsContext context, int width, int height)
    {
        if (!_gpuResolved)
        {
            _gpuResolved = true;
            var factory = GetGraphicsFactory();
            if (SkiaInterop.TryResolve(factory.Backend, out var provider))
            {
                _gpuHost = provider.TryCreateSurfaceHost(factory);
            }
        }

        if (_gpuHost is null) return false;

        if (_gpuHost is IOpaqueAwareSurfaceHost opaqueAware)
            opaqueAware.IsOpaque = Background is { A: 255 };

        try
        {
            if (!_gpuHost.EnsureSurface(width, height)) return false;

            var image = _gpuHost.Paint(surface => InvokePainter(surface, width, height));

            if (image is null)
            {
                if (_gpuHost.SurfaceInvalidated && _gpuHost.EnsureSurface(width, height))
                {
                    image = _gpuHost.Paint(surface => InvokePainter(surface, width, height));
                }
                if (image is null) return false;
            }

            context.DrawImage(image, Bounds);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void InvokePainter(SKSurface surface, int width, int height)
    {
        var canvas = surface.Canvas;
        var clear = Background is { } bg ? new SKColor(bg.R, bg.G, bg.B, bg.A) : SKColors.Transparent;
        canvas.Clear(clear);
        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        PaintSurface?.Invoke(canvas, info);
    }

    private void DisposeGpu()
    {
        _gpuHost?.Dispose();
        _gpuHost = null;
    }

    private void RenderCpu(IGraphicsContext context, int width, int height)
    {
        EnsureCpuSurface(width, height);
        if (_cpuSurface is null) return;

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        var canvas = _cpuSurface.Canvas;
        canvas.Clear(SKColors.Transparent);
        PaintSurface?.Invoke(canvas, info);
        canvas.Flush();

        _cpuVersion++;
        _cpuImage ??= GetGraphicsFactory().CreateImageView((IPixelBufferSource)this);
        context.DrawImage(_cpuImage, Bounds);
    }

    private void EnsureCpuSurface(int width, int height)
    {
        if (_cpuSurface != null && width == _cpuPixelWidth && height == _cpuPixelHeight) return;

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
        if (_cpuPin.IsAllocated) _cpuPin.Free();
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
        if (_gpuInteropInvalidationSource is null) return;
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

        DisposeGpu();
        _gpuResolved = false;
        _gpuDemoted = false;
        InvalidateVisual();
    }
}
