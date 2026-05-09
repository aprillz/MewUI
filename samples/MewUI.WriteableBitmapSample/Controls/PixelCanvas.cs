using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.WriteableBitmapSample.Controls;

/// <summary>
/// A simple pixel canvas that allows drawing with mouse.
/// Demonstrates basic WriteableBitmap usage for interactive drawing.
/// </summary>
public class PixelCanvas : FrameworkElement
{
    private WriteableBitmap? _bitmap;
    private IImage? _image;

    private bool _isDrawing;
    private int _lastX = -1;
    private int _lastY = -1;

    public Color BrushColor { get; set; } = Color.Black;

    public int BrushSize { get; set
        {
            field = Math.Max(1, Math.Min(50, value));
        } } = 3;

    public void Clear()
    {
        if (_bitmap == null) return;
        using (var ctx = _bitmap.LockForWrite())
        {
            ctx.Clear(Color.White);
        }
        InvalidateVisual();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        var bounds = Bounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;

        EnsureBitmap();
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

        using var ctx = _bitmap.LockForWrite();
        ctx.Clear(Color.White);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (e.Button == MouseButton.Left)
        {
            _isDrawing = true;

            // Capture mouse through Window
            if (FindVisualRoot() is Window window)
            {
                window.CaptureMouse(this);
            }

            var (px, py) = ToPixelCoords(e.GetPosition(this));
            DrawBrush(px, py);
            _lastX = px;
            _lastY = py;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (_isDrawing && _bitmap != null)
        {
            var (px, py) = ToPixelCoords(e.GetPosition(this));

            if (_lastX >= 0 && _lastY >= 0)
            {
                DrawLineBrush(_lastX, _lastY, px, py);
            }
            else
            {
                DrawBrush(px, py);
            }

            _lastX = px;
            _lastY = py;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (e.Button == MouseButton.Left && _isDrawing)
        {
            _isDrawing = false;

            // Release mouse capture through Window
            if (FindVisualRoot() is Window window)
            {
                window.ReleaseMouseCapture();
            }

            _lastX = -1;
            _lastY = -1;
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

    private (int x, int y) ToPixelCoords(Point localPosition)
    {
        var size = RenderSize;
        if (_bitmap == null || size.Width <= 0 || size.Height <= 0)
            return (0, 0);

        double scaleX = _bitmap.PixelWidth / size.Width;
        double scaleY = _bitmap.PixelHeight / size.Height;

        int px = (int)(localPosition.X * scaleX);
        int py = (int)(localPosition.Y * scaleY);

        return (px, py);
    }

    private void DrawBrush(int cx, int cy)
    {
        if (_bitmap == null) return;

        using (var ctx = _bitmap.LockForWrite())
        {
            int radius = BrushSize / 2;
            FillCircle(ctx, cx, cy, radius, BrushColor);
        }
        InvalidateVisual();
    }

    private void DrawLineBrush(int x0, int y0, int x1, int y1)
    {
        if (_bitmap == null) return;

        using (var ctx = _bitmap.LockForWrite())
        {
            int radius = BrushSize / 2;

            // Draw circles along the line for smooth brush stroke
            int dx = Math.Abs(x1 - x0);
            int dy = Math.Abs(y1 - y0);
            int steps = Math.Max(dx, dy);

            if (steps == 0)
            {
                FillCircle(ctx, x0, y0, radius, BrushColor);
            }
            else
            {
                for (int i = 0; i <= steps; i++)
                {
                    int x = x0 + (x1 - x0) * i / steps;
                    int y = y0 + (y1 - y0) * i / steps;
                    FillCircle(ctx, x, y, radius, BrushColor);
                }
            }
        }
        InvalidateVisual();
    }

    private static void FillCircle(WriteableBitmap.WriteContext ctx, int cx, int cy, int radius, Color color)
    {
        if (radius <= 0)
        {
            // Single pixel
            if ((uint)cx < (uint)ctx.Width && (uint)cy < (uint)ctx.Height)
            {
                ctx.PixelsUInt32[cy * ctx.Width + cx] =
                    (uint)(color.B | (color.G << 8) | (color.R << 16) | (color.A << 24));
            }
            return;
        }

        int w = ctx.Width;
        int h = ctx.Height;
        var pixels = ctx.PixelsUInt32;
        uint bgra = (uint)(color.B | (color.G << 8) | (color.R << 16) | (color.A << 24));

        // Scanline fill using circle equation
        int y1 = Math.Max(0, cy - radius);
        int y2 = Math.Min(h - 1, cy + radius);
        int rsq = radius * radius;

        for (int y = y1; y <= y2; y++)
        {
            int dy = y - cy;
            int span = (int)Math.Sqrt(rsq - dy * dy);
            int x1 = Math.Max(0, cx - span);
            int x2 = Math.Min(w - 1, cx + span);
            int rowStart = y * w;

            for (int x = x1; x <= x2; x++)
            {
                pixels[rowStart + x] = bgra;
            }
        }
    }
}

public static class PixelCanvasExtensions
{
    public static PixelCanvas BrushColor(this PixelCanvas canvas, Color color)
    {
        canvas.BrushColor = color;
        return canvas;
    }

    public static PixelCanvas BrushSize(this PixelCanvas canvas, int size)
    {
        canvas.BrushSize = size;
        return canvas;
    }
}
