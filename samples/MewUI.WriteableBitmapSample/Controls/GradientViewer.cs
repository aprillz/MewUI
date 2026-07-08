using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.WriteableBitmapSample.Controls;

/// <summary>
/// A gradient visualization control demonstrating smooth color interpolation.
/// Uses direct pixel manipulation for efficient gradient rendering.
/// </summary>
public class GradientViewer : FrameworkElement
{
    private WriteableBitmap? _bitmap;
    private IImage? _image;
    private bool _dirty = true;

    public GradientType GradientType
    {
        get;
        set
        {
            field = value;
            Invalidate();
        }
    } = GradientType.Linear;

    public Color StartColor
    {
        get;
        set
        {
            field = value;
            Invalidate();
        }
    } = new(255, 255, 0, 0);

    public Color EndColor
    {
        get;
        set
        {
            field = value;
            Invalidate();
        }
    } = new(255, 0, 0, 255);

    public Color? MiddleColor
    {
        get;
        set
        {
            field = value;
            Invalidate();
        }
    }

    private void Invalidate()
    {
        _dirty = true;
        InvalidateVisual();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        EnsureBitmap();
        if (_dirty && _bitmap != null)
        {
            RenderGradient();
            _dirty = false;
        }

        if (_image != null)
        {
            context.DrawImage(_image, bounds);
        }
    }

    private void EnsureBitmap()
    {
        var bounds = Bounds;
        double scale = GetDpi() / 96.0;
        int pw = Math.Max(1, (int)Math.Ceiling(bounds.Width * scale));
        int ph = Math.Max(1, (int)Math.Ceiling(bounds.Height * scale));

        if (_bitmap != null && _bitmap.PixelWidth == pw && _bitmap.PixelHeight == ph)
        {
            return;
        }

        _image?.Dispose();
        _bitmap?.Dispose();
        _bitmap = new WriteableBitmap(pw, ph);
        _image = GetGraphicsFactory().CreateImageView(_bitmap);
        _dirty = true;
    }

    private void RenderGradient()
    {
        if (_bitmap == null) return;

        using var ctx = _bitmap.LockForWrite();

        switch (GradientType)
        {
            case GradientType.Linear:
                RenderLinearGradient(ctx);
                break;
            case GradientType.Radial:
                RenderRadialGradient(ctx);
                break;
            case GradientType.Angular:
                RenderAngularGradient(ctx);
                break;
            case GradientType.Diamond:
                RenderDiamondGradient(ctx);
                break;
        }
    }

    private void RenderLinearGradient(WriteableBitmap.WriteContext ctx)
    {
        int w = ctx.Width;
        int h = ctx.Height;
        var pixels = ctx.PixelsUInt32;

        for (int y = 0; y < h; y++)
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                double t = (double)x / (w - 1);
                var color = LerpColor(t);
                pixels[rowOffset + x] = ColorToBgra(color);
            }
        }
    }

    private void RenderRadialGradient(WriteableBitmap.WriteContext ctx)
    {
        int w = ctx.Width;
        int h = ctx.Height;
        var pixels = ctx.PixelsUInt32;

        double cx = w / 2.0;
        double cy = h / 2.0;
        double maxDist = Math.Sqrt(cx * cx + cy * cy);

        for (int y = 0; y < h; y++)
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                double dx = x - cx;
                double dy = y - cy;
                double dist = Math.Sqrt(dx * dx + dy * dy);
                double t = Math.Min(1.0, dist / maxDist);
                var color = LerpColor(t);
                pixels[rowOffset + x] = ColorToBgra(color);
            }
        }
    }

    private void RenderAngularGradient(WriteableBitmap.WriteContext ctx)
    {
        int w = ctx.Width;
        int h = ctx.Height;
        var pixels = ctx.PixelsUInt32;

        double cx = w / 2.0;
        double cy = h / 2.0;

        for (int y = 0; y < h; y++)
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                double dx = x - cx;
                double dy = y - cy;
                double angle = Math.Atan2(dy, dx);
                double t = (angle + Math.PI) / (2 * Math.PI);
                var color = LerpColor(t);
                pixels[rowOffset + x] = ColorToBgra(color);
            }
        }
    }

    private void RenderDiamondGradient(WriteableBitmap.WriteContext ctx)
    {
        int w = ctx.Width;
        int h = ctx.Height;
        var pixels = ctx.PixelsUInt32;

        double cx = w / 2.0;
        double cy = h / 2.0;
        double maxDist = cx + cy;

        for (int y = 0; y < h; y++)
        {
            int rowOffset = y * w;
            for (int x = 0; x < w; x++)
            {
                double dx = Math.Abs(x - cx);
                double dy = Math.Abs(y - cy);
                double dist = dx + dy;
                double t = Math.Min(1.0, dist / maxDist);
                var color = LerpColor(t);
                pixels[rowOffset + x] = ColorToBgra(color);
            }
        }
    }

    private Color LerpColor(double t)
    {
        if (MiddleColor.HasValue)
        {
            if (t < 0.5)
            {
                return LerpTwoColors(StartColor, MiddleColor.Value, t * 2);
            }
            else
            {
                return LerpTwoColors(MiddleColor.Value, EndColor, (t - 0.5) * 2);
            }
        }
        else
        {
            return LerpTwoColors(StartColor, EndColor, t);
        }
    }

    private static Color LerpTwoColors(Color c1, Color c2, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Color(
            (byte)(c1.A + (c2.A - c1.A) * t),
            (byte)(c1.R + (c2.R - c1.R) * t),
            (byte)(c1.G + (c2.G - c1.G) * t),
            (byte)(c1.B + (c2.B - c1.B) * t));
    }

    private static uint ColorToBgra(Color c)
    {
        return (uint)(c.B | (c.G << 8) | (c.R << 16) | (c.A << 24));
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _image?.Dispose();
        _image = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }
}

public enum GradientType
{
    Linear,
    Radial,
    Angular,
    Diamond
}

public static class GradientViewerExtensions
{
    public static GradientViewer GradientType(this GradientViewer gv, GradientType type)
    {
        gv.GradientType = type;
        return gv;
    }

    public static GradientViewer StartColor(this GradientViewer gv, Color color)
    {
        gv.StartColor = color;
        return gv;
    }

    public static GradientViewer EndColor(this GradientViewer gv, Color color)
    {
        gv.EndColor = color;
        return gv;
    }

    public static GradientViewer MiddleColor(this GradientViewer gv, Color color)
    {
        gv.MiddleColor = color;
        return gv;
    }

    public static GradientViewer Colors(this GradientViewer gv, Color start, Color end)
    {
        gv.StartColor = start;
        gv.EndColor = end;
        gv.MiddleColor = null;
        return gv;
    }

    public static GradientViewer Colors(this GradientViewer gv, Color start, Color middle, Color end)
    {
        gv.StartColor = start;
        gv.MiddleColor = middle;
        gv.EndColor = end;
        return gv;
    }
}
