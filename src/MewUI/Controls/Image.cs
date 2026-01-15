using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Controls;

public sealed class Image : Control
{
    private readonly Dictionary<GraphicsBackend, IImage> _cache = new();

    public ImageStretch StretchMode
    {
        get;
        set { field = value; InvalidateMeasure(); InvalidateVisual(); }
    } = ImageStretch.None;

    public ImageSource? Source
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
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

        // Pixels are treated as DIPs for now (1px == 1dip at 96dpi).
        return new Size(img.PixelWidth, img.PixelHeight);
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
            var srcSize = new Size(img.PixelWidth, img.PixelHeight);
            if (srcSize.IsEmpty)
            {
                return;
            }

            ComputeRects(srcSize, Bounds, StretchMode, out var dest, out var src);
            context.DrawImage(img, dest, src);
        }
        finally
        {
            context.Restore();
        }
    }

    private static void ComputeRects(Size sourceSize, Rect bounds, ImageStretch stretch, out Rect dest, out Rect src)
    {
        src = new Rect(0, 0, sourceSize.Width, sourceSize.Height);

        double sw = Math.Max(0, sourceSize.Width);
        double sh = Math.Max(0, sourceSize.Height);
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
                double dx = bounds.X + (bounds.Width - dw) / 2;
                double dy = bounds.Y + (bounds.Height - dh) / 2;
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
                    src = new Rect(0, cropY, sw, cropH);
                }
                else if (boundsAspect < srcAspect)
                {
                    double cropW = sh * boundsAspect;
                    double cropX = (sw - cropW) / 2;
                    src = new Rect(cropX, 0, cropW, sh);
                }

                dest = bounds;
                return;
            }

            case ImageStretch.None:
            default:
            {
                // Keep pixel size; center within bounds (and clip).
                double dx = bounds.X + (bounds.Width - sw) / 2;
                double dy = bounds.Y + (bounds.Height - sh) / 2;
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

        var created = factory.CreateImageFromBytes(Source.Data);
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
        ClearCache();
        base.OnDispose();
    }
}
