using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class Image : Control
{
    private readonly Dictionary<GraphicsBackend, IImage> _cache = new();
    private INotifyImageChanged? _notifySource;

    public ImageStretch StretchMode
    {
        get;
        set { field = value; InvalidateMeasure(); InvalidateVisual(); }
    } = ImageStretch.None;

    public Rect? ViewBox
    {
        get;
        set { field = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    public ImageViewBoxUnits ViewBoxUnits
    {
        get;
        set { field = value; InvalidateMeasure(); InvalidateVisual(); }
    } = ImageViewBoxUnits.Pixels;

    public ImageAlignmentX AlignmentX
    {
        get;
        set { field = value; InvalidateVisual(); }
    } = ImageAlignmentX.Center;

    public ImageAlignmentY AlignmentY
    {
        get;
        set { field = value; InvalidateVisual(); }
    } = ImageAlignmentY.Center;

    public IImageSource? Source
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            if (_notifySource != null)
            {
                _notifySource.Changed -= OnSourceChanged;
                _notifySource = null;
            }

            field = value;

            _notifySource = value as INotifyImageChanged;
            if (_notifySource != null)
            {
                _notifySource.Changed += OnSourceChanged;
            }

            ClearCache();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var img = GetImage();
        if (img == null)
        {
            return Size.Empty;
        }

        var src = GetViewBoxPixels(img);

        // Pixels are treated as DIPs for now (1px == 1dip at 96dpi).
        return new Size(src.Width, src.Height);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var img = GetImage();
        if (img == null)
        {
            return;
        }

        // Always clip to the control bounds to avoid overflowing when the image's natural size
        // is larger than the arranged size.
        context.Save();
        context.SetClip(Bounds);

        try
        {
            var srcRect = GetViewBoxPixels(img);
            var srcSize = srcRect.Size;
            if (srcSize.IsEmpty)
            {
                return;
            }

            ComputeRects(srcRect, Bounds, StretchMode, AlignmentX, AlignmentY, out var dest, out var src);
            if (dest.Width > 0 && dest.Height > 0 && src.Width > 0 && src.Height > 0)
            {
                context.DrawImage(img, dest, src);
            }
        }
        finally
        {
            context.Restore();
        }
    }

    private Rect GetViewBoxPixels(IImage img)
    {
        double iw = Math.Max(0, img.PixelWidth);
        double ih = Math.Max(0, img.PixelHeight);
        var full = new Rect(0, 0, iw, ih);
        if (ViewBox is not Rect vb)
        {
            return full;
        }

        double x = vb.X;
        double y = vb.Y;
        double w = vb.Width;
        double h = vb.Height;

        if (double.IsNaN(x) || double.IsInfinity(x) ||
            double.IsNaN(y) || double.IsInfinity(y) ||
            double.IsNaN(w) || double.IsInfinity(w) ||
            double.IsNaN(h) || double.IsInfinity(h))
        {
            return full;
        }

        if (ViewBoxUnits == ImageViewBoxUnits.RelativeToBoundingBox)
        {
            x *= iw;
            y *= ih;
            w *= iw;
            h *= ih;
        }

        if (w <= 0 || h <= 0)
        {
            return full;
        }

        // Clamp into image bounds.
        if (x < 0) { w += x; x = 0; }
        if (y < 0) { h += y; y = 0; }

        if (x > iw || y > ih)
        {
            return new Rect(0, 0, 0, 0);
        }

        if (x + w > iw) { w = iw - x; }
        if (y + h > ih) { h = ih - y; }

        if (w <= 0 || h <= 0)
        {
            return new Rect(0, 0, 0, 0);
        }

        return new Rect(x, y, w, h);
    }

    private static void ComputeRects(
        Rect sourceRect,
        Rect bounds,
        ImageStretch stretch,
        ImageAlignmentX alignX,
        ImageAlignmentY alignY,
        out Rect dest,
        out Rect src)
    {
        src = sourceRect;

        double sw = Math.Max(0, sourceRect.Width);
        double sh = Math.Max(0, sourceRect.Height);
        if (sw <= 0 || sh <= 0 || bounds.Width <= 0 || bounds.Height <= 0)
        {
            dest = new Rect(bounds.X, bounds.Y, 0, 0);
            return;
        }

        switch (stretch)
        {
            case ImageStretch.Fill:
                dest = bounds;
                return;

            case ImageStretch.Uniform:
            {
                double scale = Math.Min(bounds.Width / sw, bounds.Height / sh);
                double dw = sw * scale;
                double dh = sh * scale;
                double ax = alignX == ImageAlignmentX.Left ? 0 : alignX == ImageAlignmentX.Right ? 1 : 0.5;
                double ay = alignY == ImageAlignmentY.Top ? 0 : alignY == ImageAlignmentY.Bottom ? 1 : 0.5;
                double dx = bounds.X + (bounds.Width - dw) * ax;
                double dy = bounds.Y + (bounds.Height - dh) * ay;
                dest = new Rect(dx, dy, dw, dh);
                return;
            }

            case ImageStretch.UniformToFill:
            {
                double boundsAspect = bounds.Width / bounds.Height;
                double srcAspect = sw / sh;

                // Fill the bounds and crop the source to preserve aspect ratio.
                if (boundsAspect > srcAspect)
                {
                    double cropH = sw / boundsAspect;
                    double cropY = (sh - cropH) / 2;
                    src = new Rect(sourceRect.X, sourceRect.Y + cropY, sw, cropH);
                }
                else if (boundsAspect < srcAspect)
                {
                    double cropW = sh * boundsAspect;
                    double cropX = (sw - cropW) / 2;
                    src = new Rect(sourceRect.X + cropX, sourceRect.Y, cropW, sh);
                }

                dest = bounds;
                return;
            }

            case ImageStretch.None:
            default:
            {
                // Keep pixel size; center within bounds (and clip).
                double ax = alignX == ImageAlignmentX.Left ? 0 : alignX == ImageAlignmentX.Right ? 1 : 0.5;
                double ay = alignY == ImageAlignmentY.Top ? 0 : alignY == ImageAlignmentY.Bottom ? 1 : 0.5;
                double dx = bounds.X + (bounds.Width - sw) * ax;
                double dy = bounds.Y + (bounds.Height - sh) * ay;
                dest = new Rect(dx, dy, sw, sh);
                return;
            }
        }
    }

    private IImage? GetImage()
    {
        if (Source == null)
        {
            return null;
        }

        var factory = Application.IsRunning ? Application.Current.GraphicsFactory : Application.DefaultGraphicsFactory;
        var backend = factory.Backend;

        if (_cache.TryGetValue(backend, out var cached))
        {
            return cached;
        }

        var created = Source.CreateImage(factory);
        _cache[backend] = created;
        return created;
    }

    private void ClearCache()
    {
        foreach (var kvp in _cache)
        {
            kvp.Value.Dispose();
        }

        _cache.Clear();
    }

    protected override void OnDispose()
    {
        if (_notifySource != null)
        {
            _notifySource.Changed -= OnSourceChanged;
            _notifySource = null;
        }

        ClearCache();
        base.OnDispose();
    }

    private void OnSourceChanged()
    {
        // Keep cached IImage instances; backend images are expected to refresh from the source (e.g. WritableBitmap.Version).
        InvalidateVisual();
    }
}
