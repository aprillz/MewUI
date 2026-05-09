using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.WriteableBitmapSample.Controls;

/// <summary>
/// A simple line chart control demonstrating data visualization with WriteableBitmap.
/// Pixel cache holds background/grid/fill; anti-aliased line + points are drawn as
/// per-frame overlay on top of the cached image.
/// </summary>
public class SimpleChart : FrameworkElement
{
    private readonly Color _gridColor = new(255, 230, 230, 230);
    private readonly Color _axisColor = new(255, 180, 180, 180);
    private const int _padding = 20;

    private WriteableBitmap? _bitmap;
    private IImage? _image;
    private bool _dirty = true;

    // Cached line points in DIPs for overlay drawing.
    private Point[]? _linePoints;

    public double[] Data
    {
        get;
        set
        {
            field = value ?? [];
            Invalidate();
        }
    } = [];

    public Color LineColor
    {
        get;
        set
        {
            field = value;
            InvalidateVisual(); // line color affects only the overlay
        }
    } = new(255, 66, 133, 244);

    public Color FillColor
    {
        get;
        set
        {
            field = value;
            Invalidate();
        }
    } = new(64, 66, 133, 244);

    public bool ShowFill
    {
        get;
        set
        {
            field = value;
            Invalidate();
        }
    } = true;

    public bool ShowGrid
    {
        get;
        set
        {
            field = value;
            Invalidate();
        }
    } = true;

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
            RenderChartPixels();
            _dirty = false;
        }

        if (_image != null)
        {
            context.DrawImage(_image, bounds);
        }

        // Anti-aliased line + data points overlay (not cached into the bitmap).
        // _linePoints are in bitmap-relative DIPs; translate by bounds origin for the screen pass.
        if (_linePoints != null && _linePoints.Length >= 2)
        {
            double ox = bounds.X;
            double oy = bounds.Y;

            for (int i = 0; i < _linePoints.Length - 1; i++)
            {
                var p1 = new Point(ox + _linePoints[i].X, oy + _linePoints[i].Y);
                var p2 = new Point(ox + _linePoints[i + 1].X, oy + _linePoints[i + 1].Y);
                context.DrawLine(p1, p2, LineColor, 2);
            }

            const double pointRadius = 3;
            foreach (var pt in _linePoints)
            {
                double cx = ox + pt.X;
                double cy = oy + pt.Y;
                context.FillEllipse(new Rect(cx - pointRadius, cy - pointRadius, pointRadius * 2, pointRadius * 2), LineColor);
            }
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
        _image = GetGraphicsFactory().AsRenderDevice().CreateImageView(_bitmap);
        _dirty = true;
    }

    private void RenderChartPixels()
    {
        if (_bitmap == null) return;

        using var ctx = _bitmap.LockForWrite();
        int w = ctx.Width;
        int h = ctx.Height;
        var pixels = ctx.PixelsUInt32;

        ctx.Clear(Color.White);

        double scale = GetDpi() / 96.0;
        int pad = (int)(_padding * scale);

        int chartLeft = pad;
        int chartRight = w - pad;
        int chartTop = pad;
        int chartBottom = h - pad;
        int chartWidth = chartRight - chartLeft;
        int chartHeight = chartBottom - chartTop;

        if (chartWidth <= 0 || chartHeight <= 0)
        {
            _linePoints = null;
            return;
        }

        uint gridBgra = ColorToBgra(_gridColor);
        uint axisBgra = ColorToBgra(_axisColor);
        uint fillBgra = ColorToBgra(FillColor);

        if (ShowGrid)
        {
            for (int i = 0; i <= 4; i++)
            {
                int y = chartTop + chartHeight * i / 4;
                DrawHLine(pixels, w, h, chartLeft, chartRight, y, gridBgra);
            }

            int gridLines = Math.Min(Data.Length, 10);
            for (int i = 0; i <= gridLines; i++)
            {
                int x = chartLeft + chartWidth * i / Math.Max(1, gridLines);
                DrawVLine(pixels, w, h, x, chartTop, chartBottom, gridBgra);
            }
        }

        DrawHLine(pixels, w, h, chartLeft, chartRight, chartBottom, axisBgra);
        DrawVLine(pixels, w, h, chartLeft, chartTop, chartBottom, axisBgra);

        if (Data.Length < 2)
        {
            _linePoints = null;
            return;
        }

        double minVal = Data.Min();
        double maxVal = Data.Max();
        double range = maxVal - minVal;
        if (range < 0.0001) range = 1;

        minVal -= range * 0.05;
        maxVal += range * 0.05;
        range = maxVal - minVal;

        int[] lineYs = new int[chartWidth + 1];
        for (int x = 0; x <= chartWidth; x++)
        {
            double t = (double)x / chartWidth;
            double dataIndex = t * (Data.Length - 1);
            int i0 = (int)dataIndex;
            int i1 = Math.Min(i0 + 1, Data.Length - 1);
            double frac = dataIndex - i0;

            double value = Data[i0] + (Data[i1] - Data[i0]) * frac;
            double normalizedValue = (value - minVal) / range;
            lineYs[x] = chartBottom - (int)(normalizedValue * chartHeight);
        }

        if (ShowFill)
        {
            for (int x = 0; x <= chartWidth; x++)
            {
                int px = chartLeft + x;
                int lineY = lineYs[x];

                for (int py = lineY; py < chartBottom; py++)
                {
                    if (py >= 0 && py < h)
                    {
                        pixels[py * w + px] = fillBgra;
                    }
                }
            }
        }

        // Cache line points in DIPs for the per-frame overlay.
        double dipScale = 96.0 / GetDpi();
        _linePoints = new Point[Data.Length];
        for (int i = 0; i < Data.Length; i++)
        {
            double t = (double)i / (Data.Length - 1);
            double normalizedValue = (Data[i] - minVal) / range;
            _linePoints[i] = new Point(
                (chartLeft + t * chartWidth) * dipScale,
                (chartBottom - normalizedValue * chartHeight) * dipScale);
        }
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        _image?.Dispose();
        _image = null;
        _bitmap?.Dispose();
        _bitmap = null;
    }

    private static uint ColorToBgra(Color c) =>
        (uint)(c.B | (c.G << 8) | (c.R << 16) | (c.A << 24));

    private static void DrawHLine(Span<uint> pixels, int w, int h, int x1, int x2, int y, uint color)
    {
        if ((uint)y >= (uint)h) return;
        x1 = Math.Max(0, x1);
        x2 = Math.Min(w - 1, x2);
        int rowStart = y * w;
        for (int x = x1; x <= x2; x++)
            pixels[rowStart + x] = color;
    }

    private static void DrawVLine(Span<uint> pixels, int w, int h, int x, int y1, int y2, uint color)
    {
        if ((uint)x >= (uint)w) return;
        y1 = Math.Max(0, y1);
        y2 = Math.Min(h - 1, y2);
        for (int y = y1; y <= y2; y++)
            pixels[y * w + x] = color;
    }
}

public static class SimpleChartExtensions
{
    public static SimpleChart Data(this SimpleChart chart, params double[] data)
    {
        chart.Data = data;
        return chart;
    }

    public static SimpleChart Data(this SimpleChart chart, IEnumerable<double> data)
    {
        chart.Data = data.ToArray();
        return chart;
    }

    public static SimpleChart LineColor(this SimpleChart chart, Color color)
    {
        chart.LineColor = color;
        return chart;
    }

    public static SimpleChart FillColor(this SimpleChart chart, Color color)
    {
        chart.FillColor = color;
        return chart;
    }

    public static SimpleChart ShowFill(this SimpleChart chart, bool show = true)
    {
        chart.ShowFill = show;
        return chart;
    }

    public static SimpleChart ShowGrid(this SimpleChart chart, bool show = true)
    {
        chart.ShowGrid = show;
        return chart;
    }
}
