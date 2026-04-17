using Aprillz.MewUI.Rendering;

using SkiaSharp;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// GPU-backed SkiaSharp element for MewUI.
/// The API intentionally mirrors SkiaSharp's reusable SKElement surface model.
/// </summary>
public class SKElement : Control
{
    public static readonly MewProperty<bool> IgnorePixelScalingProperty =
        MewProperty<bool>.Register<SKElement>(
            nameof(IgnorePixelScaling),
            false,
            MewPropertyOptions.AffectsRender);

    static SKElement()
    {
        IsHitTestVisibleProperty.OverrideDefaultValue<SKElement>(false);
    }

    private ISkiaGpuControlSurface? _surface;
    private IGraphicsFactory? _surfaceFactory;
    private nint _surfaceWindowHandle;
    private bool _needsRedraw = true;
    private bool _lastIgnorePixelScaling;

    /// <summary>
    /// Gets the current canvas size exposed to paint callbacks.
    /// </summary>
    public SKSize CanvasSize { get; private set; }

    /// <summary>
    /// Gets or sets whether Skia callbacks use unscaled logical pixels instead
    /// of raw device pixels.
    /// </summary>
    public bool IgnorePixelScaling
    {
        get => GetValue(IgnorePixelScalingProperty);
        set => SetValue(IgnorePixelScalingProperty, value);
    }

    /// <summary>
    /// Raised when the Skia surface needs to be painted.
    /// </summary>
    public event EventHandler<SKPaintSurfaceEventArgs>? PaintSurface;

    /// <summary>
    /// Marks the Skia surface as dirty and schedules a repaint.
    /// </summary>
    public void InvalidateSurface()
    {
        _needsRedraw = true;
        InvalidateVisual();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            CanvasSize = SKSize.Empty;
            return;
        }

        var root = FindVisualRoot() as Window;
        var factory = GetGraphicsFactory();
        var skiaFactory = factory.TryGetGraphicsService<ISkiaGpuControlFactory>();
        if (root == null || root.Handle == 0 || skiaFactory == null)
        {
            DisposeSurface();
            CanvasSize = SKSize.Empty;
            return;
        }

        var dpiScale = GetDpi() / 96.0;
        int rawPixelWidth = Math.Max(1, (int)Math.Ceiling(bounds.Width * dpiScale));
        int rawPixelHeight = Math.Max(1, (int)Math.Ceiling(bounds.Height * dpiScale));

        bool ignorePixelScaling = IgnorePixelScaling;
        int visibleWidth = ignorePixelScaling
            ? Math.Max(1, (int)Math.Ceiling(bounds.Width))
            : rawPixelWidth;
        int visibleHeight = ignorePixelScaling
            ? Math.Max(1, (int)Math.Ceiling(bounds.Height))
            : rawPixelHeight;

        CanvasSize = new SKSize(visibleWidth, visibleHeight);

        EnsureSurface(skiaFactory, factory, root.Handle, rawPixelWidth, rawPixelHeight, dpiScale);

        if (_surface == null)
        {
            return;
        }

        if (_lastIgnorePixelScaling != ignorePixelScaling)
        {
            _lastIgnorePixelScaling = ignorePixelScaling;
            _needsRedraw = true;
        }

        float scaleX = ignorePixelScaling
            ? rawPixelWidth / Math.Max(1f, visibleWidth)
            : 1f;
        float scaleY = ignorePixelScaling
            ? rawPixelHeight / Math.Max(1f, visibleHeight)
            : 1f;

        bool redraw = _needsRedraw;
        _surface.Draw(context, bounds, redraw, surface =>
        {
            var rawInfo = new SKImageInfo(
                _surface.PixelWidth,
                _surface.PixelHeight,
                _surface.ColorType,
                _surface.AlphaType);

            var info = new SKImageInfo(
                visibleWidth,
                visibleHeight,
                rawInfo.ColorType,
                rawInfo.AlphaType,
                rawInfo.ColorSpace);

            var canvas = surface.Canvas;
            int restoreCount = canvas.Save();

            if (ignorePixelScaling)
            {
                canvas.Scale(scaleX, scaleY);
            }

            OnPaintSurface(new SKPaintSurfaceEventArgs(surface, info, rawInfo));
            canvas.RestoreToCount(restoreCount);
        });

        _needsRedraw = false;
    }

    protected virtual void OnPaintSurface(SKPaintSurfaceEventArgs e)
        => PaintSurface?.Invoke(this, e);

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        InvalidateSurface();
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        InvalidateSurface();
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);
        DisposeSurface();
        InvalidateSurface();
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        DisposeSurface();
    }

    private void EnsureSurface(
        ISkiaGpuControlFactory skiaFactory,
        IGraphicsFactory graphicsFactory,
        nint windowHandle,
        int pixelWidth,
        int pixelHeight,
        double dpiScale)
    {
        if (_surface == null ||
            !ReferenceEquals(_surfaceFactory, graphicsFactory) ||
            _surfaceWindowHandle != windowHandle)
        {
            DisposeSurface();
            _surface = skiaFactory.CreateSkiaGpuControlSurface(windowHandle, pixelWidth, pixelHeight, dpiScale);
            _surfaceFactory = graphicsFactory;
            _surfaceWindowHandle = windowHandle;
            _needsRedraw = true;
            return;
        }

        if (_surface.PixelWidth != pixelWidth ||
            _surface.PixelHeight != pixelHeight ||
            !AreClose(_surface.DpiScale, dpiScale))
        {
            _surface.Resize(pixelWidth, pixelHeight, dpiScale);
            _needsRedraw = true;
        }
    }

    private void DisposeSurface()
    {
        _surface?.Dispose();
        _surface = null;
        _surfaceFactory = null;
        _surfaceWindowHandle = 0;
    }

    private static bool AreClose(double left, double right)
        => Math.Abs(left - right) <= 0.0001;
}
