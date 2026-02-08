using Aprillz.MewUI.Resources;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// An image display control with scaling and alignment options.
/// </summary>
public sealed class Image : FrameworkElement
{
    private readonly Dictionary<GraphicsBackend, IImage> _cache = new();
    private INotifyImageChanged? _notifySource;

    /// <summary>
    /// Gets or sets the image scaling quality.
    /// </summary>
    public ImageScaleQuality ImageScaleQuality
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateVisual();
            }
        }
    } = ImageScaleQuality.Default;

    /// <summary>
    /// Gets or sets how the image is stretched to fill available space.
    /// </summary>
    public ImageStretch StretchMode
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    } = ImageStretch.Uniform;

    /// <summary>
    /// Gets or sets the viewbox region of the source image.
    /// </summary>
    public Rect? ViewBox
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the units for the viewbox coordinates.
    /// </summary>
    public ImageViewBoxUnits ViewBoxUnits
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    } = ImageViewBoxUnits.Pixels;

    /// <summary>
    /// Gets or sets the horizontal alignment of the image.
    /// </summary>
    public ImageAlignmentX AlignmentX
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateVisual();
            }
        }
    } = ImageAlignmentX.Center;

    /// <summary>
    /// Gets or sets the vertical alignment of the image.
    /// </summary>
    public ImageAlignmentY AlignmentY
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateVisual();
            }
        }
    } = ImageAlignmentY.Center;

    /// <summary>
    /// Gets or sets the image source.
    /// </summary>
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

    /// <summary>
    /// Tries to read the source pixel color at the given position (window-relative DIPs).
    /// </summary>
    /// <remarks>
    /// This reads pixels from the decoded <see cref="ImageSource"/> data (BGRA32) and maps the position through
    /// <see cref="ViewBox"/>, <see cref="StretchMode"/>, and alignment. Returns <see langword="false"/> if the source
    /// is not an <see cref="ImageSource"/>, decoding fails, or the position maps outside the source.
    /// </remarks>
    public bool TryPeekColor(Point positionDip, out Color color)
    {
        color = default;
        if (Source is not ImageSource imageSource)
        {
            return false;
        }

        // Do not decode in this method. Decoding happens when the source is first used for rendering
        // (ImageSource.CreateImage caches the decoded pixel buffer). If the source hasn't been used
        // yet, simply return false.
        if (!imageSource.TryGetDecodedBitmap(out var decoded))
        {
            return false;
        }

        var srcRect = GetViewBoxPixels(decoded.WidthPx, decoded.HeightPx);
        if (srcRect.Width <= 0 || srcRect.Height <= 0)
        {
            return false;
        }

        ComputeRects(srcRect, Bounds, StretchMode, AlignmentX, AlignmentY, out var dest, out var src);
        if (dest.Width <= 0 || dest.Height <= 0 || src.Width <= 0 || src.Height <= 0)
        {
            return false;
        }

        // Position is window-relative, same coordinate space as Bounds/dest.
        if (!dest.Contains(positionDip))
        {
            return false;
        }

        double u = (positionDip.X - dest.X) / dest.Width;
        double v = (positionDip.Y - dest.Y) / dest.Height;

        double sx = src.X + u * src.Width;
        double sy = src.Y + v * src.Height;

        int px = (int)System.Math.Floor(sx);
        int py = (int)System.Math.Floor(sy);

        if ((uint)px >= (uint)decoded.WidthPx || (uint)py >= (uint)decoded.HeightPx)
        {
            return false;
        }

        int index = py * decoded.StrideBytes + px * 4 + 3; // BGRA
        if ((uint)index >= (uint)decoded.Data.Length)
        {
            return false;
        }

        var data = decoded.Data;
        byte b = data[index - 3];
        byte g = data[index - 2];
        byte r = data[index - 1];
        byte a = data[index];
        color = new Color(a, r, g, b);
        return true;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var img = GetImage();
        if (img == null)
        {
            return Size.Empty;
        }

        var src = GetViewBoxPixels(img.PixelWidth, img.PixelHeight);

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

        var prevScaleQuality = context.ImageScaleQuality;
        context.ImageScaleQuality = ImageScaleQuality;

        // Always clip to the control bounds to avoid overflowing when the image's natural size
        // is larger than the arranged size.
        context.Save();
        var dpiScale = GetDpi() / 96.0;
        context.SetClip(LayoutRounding.SnapViewportRectToPixels(Bounds, dpiScale));

        try
        {
            var srcRect = GetViewBoxPixels(img.PixelWidth, img.PixelHeight);
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
            context.ImageScaleQuality = prevScaleQuality;
        }
    }

    private Rect GetViewBoxPixels(int pixelWidth, int pixelHeight)
    {
        double iw = System.Math.Max(0, pixelWidth);
        double ih = System.Math.Max(0, pixelHeight);
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
        base.OnDispose();

        if (_notifySource != null)
        {
            _notifySource.Changed -= OnSourceChanged;
            _notifySource = null;
        }

        ClearCache();
    }

    private void OnSourceChanged()
    {
        // Keep cached IImage instances; backend images are expected to refresh from the source (e.g. WriteableBitmap.Version).
        InvalidateVisual();
    }
}
