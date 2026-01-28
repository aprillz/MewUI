using System.Diagnostics;

using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Native.DirectWrite;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed unsafe class Direct2DGraphicsContext : IGraphicsContext
{
    private const int D2DERR_RECREATE_TARGET = unchecked((int)0x8899000C);
    private const int D2DERR_WRONG_RESOURCE_DOMAIN = unchecked((int)0x88990015);

    private readonly nint _hwnd;
    private readonly nint _dwriteFactory;
    private readonly Action? _onRecreateTarget;

    private nint _renderTarget; // ID2D1RenderTarget* 
    private readonly int _renderTargetGeneration;
    private readonly Dictionary<uint, nint> _solidBrushes = new();
    private readonly Stack<(double tx, double ty, int clipDepth)> _states = new();
    private double _translateX;
    private double _translateY;
    private int _clipDepth;
    private bool _disposed;

    public ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;
    
    public double DpiScale { get; }

    public Direct2DGraphicsContext(nint hwnd, double dpiScale, nint renderTarget, int renderTargetGeneration, nint dwriteFactory, Action? onRecreateTarget)
    {
        _hwnd = hwnd;
        _dwriteFactory = dwriteFactory;
        _onRecreateTarget = onRecreateTarget;
        DpiScale = dpiScale;

        _renderTarget = renderTarget;
        _renderTargetGeneration = renderTargetGeneration;
        D2D1VTable.BeginDraw((ID2D1RenderTarget*)_renderTarget);
        D2D1VTable.SetTextAntialiasMode((ID2D1RenderTarget*)_renderTarget, D2D1_TEXT_ANTIALIAS_MODE.CLEARTYPE);
    }

    [Conditional("DEBUG")]
    private static void AssertHr(int hr, string op)
    {
        if (hr >= 0)
        {
            return;
        }

        string msg = $"Direct2D: {op} failed: 0x{hr:X8}";
        Debug.Fail(msg);
        DiagLog.Write(msg);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        try
        {
            if (_renderTarget != 0)
            {
                while (_clipDepth > 0)
                {
                    D2D1VTable.PopAxisAlignedClip((ID2D1RenderTarget*)_renderTarget);
                    _clipDepth--;
                }

                int hr = D2D1VTable.EndDraw((ID2D1RenderTarget*)_renderTarget);
                AssertHr(hr, "EndDraw");
                if (hr == D2DERR_RECREATE_TARGET || hr == D2DERR_WRONG_RESOURCE_DOMAIN)
                {
                    _onRecreateTarget?.Invoke();
                }
            }
        }
        finally
        {
            foreach (var (_, brush) in _solidBrushes)
            {
                ComHelpers.Release(brush);
            }

            _solidBrushes.Clear();

            _renderTarget = 0;
            _disposed = true;
        }
    }

    public void Save() => _states.Push((_translateX, _translateY, _clipDepth));

    public void Restore()
    {
        if (_states.Count == 0 || _renderTarget == 0)
        {
            return;
        }

        var state = _states.Pop();
        while (_clipDepth > state.clipDepth)
        {
            D2D1VTable.PopAxisAlignedClip((ID2D1RenderTarget*)_renderTarget);
            _clipDepth--;
        }

        _translateX = state.tx;
        _translateY = state.ty;
    }

    public void SetClip(Rect rect)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        D2D1VTable.PushAxisAlignedClip((ID2D1RenderTarget*)_renderTarget, ToRectF(rect));
        _clipDepth++;
    }

    public void Translate(double dx, double dy)
    {
        _translateX += dx;
        _translateY += dy;
    }

    public void Clear(Color color)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        D2D1VTable.Clear((ID2D1RenderTarget*)_renderTarget, ToColorF(color));
    }

    public void DrawLine(Point start, Point end, Color color, double thickness = 1)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        float stroke = QuantizeStrokeDip((float)thickness);
        var p0 = ToPoint2F(start);
        var p1 = ToPoint2F(end);

        var snap = GetHalfPixelDipForStroke(stroke);
        if (snap != 0)
        {
            // Snap axis-aligned 1px strokes to pixel centers for crisp lines.
            if (Math.Abs(p0.y - p1.y) < 0.0001f)
            {
                p0 = new D2D1_POINT_2F(p0.x, p0.y + snap);
                p1 = new D2D1_POINT_2F(p1.x, p1.y + snap);
            }
            else if (Math.Abs(p0.x - p1.x) < 0.0001f)
            {
                p0 = new D2D1_POINT_2F(p0.x + snap, p0.y);
                p1 = new D2D1_POINT_2F(p1.x + snap, p1.y);
            }
        }

        D2D1VTable.DrawLine((ID2D1RenderTarget*)_renderTarget, p0, p1, brush, stroke);
    }

    public void DrawRectangle(Rect rect, Color color, double thickness = 1)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        float stroke = QuantizeStrokeDip((float)thickness);
        D2D1VTable.DrawRectangle((ID2D1RenderTarget*)_renderTarget, ToStrokeRectF(rect, stroke), brush, stroke);
    }

    public void FillRectangle(Rect rect, Color color)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        D2D1VTable.FillRectangle((ID2D1RenderTarget*)_renderTarget, ToRectF(rect), brush);
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        float stroke = QuantizeStrokeDip((float)thickness);
        var snappedRect = ToStrokeRectF(rect, stroke);
        var snap = GetHalfPixelDipForStroke(stroke);
        var rr = new D2D1_ROUNDED_RECT(
            snappedRect,
            (float)Math.Max(0, radiusX - snap),
            (float)Math.Max(0, radiusY - snap));
        D2D1VTable.DrawRoundedRectangle((ID2D1RenderTarget*)_renderTarget, rr, brush, stroke);
    }

    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        var rr = new D2D1_ROUNDED_RECT(ToRectF(rect), (float)radiusX, (float)radiusY);
        D2D1VTable.FillRoundedRectangle((ID2D1RenderTarget*)_renderTarget, rr, brush);
    }

    public void DrawEllipse(Rect bounds, Color color, double thickness = 1)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        float stroke = QuantizeStrokeDip((float)thickness);
        var snap = GetHalfPixelDipForStroke(stroke);

        var snapped = snap != 0
            ? new Rect(bounds.X + snap, bounds.Y + snap, Math.Max(0, bounds.Width - 2 * snap), Math.Max(0, bounds.Height - 2 * snap))
            : bounds;

        var center = new D2D1_POINT_2F(
            (float)(snapped.X + snapped.Width / 2 + _translateX),
            (float)(snapped.Y + snapped.Height / 2 + _translateY));
        var ellipse = new D2D1_ELLIPSE(center, (float)(snapped.Width / 2), (float)(snapped.Height / 2));
        D2D1VTable.DrawEllipse((ID2D1RenderTarget*)_renderTarget, ellipse, brush, stroke);
    }

    public void FillEllipse(Rect bounds, Color color)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        nint brush = GetSolidBrush(color);
        var center = new D2D1_POINT_2F((float)(bounds.X + bounds.Width / 2 + _translateX), (float)(bounds.Y + bounds.Height / 2 + _translateY));
        var ellipse = new D2D1_ELLIPSE(center, (float)(bounds.Width / 2), (float)(bounds.Height / 2));
        D2D1VTable.FillEllipse((ID2D1RenderTarget*)_renderTarget, ellipse, brush);
    }

    public void DrawText(ReadOnlySpan<char> text, Point location, IFont font, Color color) =>
        DrawText(text, new Rect(location.X, location.Y, 1_000_000, 1_000_000), font, color, TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap);

    public void DrawText(ReadOnlySpan<char> text, Rect bounds, IFont font, Color color, TextAlignment horizontalAlignment = TextAlignment.Left, TextAlignment verticalAlignment = TextAlignment.Top, TextWrapping wrapping = TextWrapping.NoWrap)
    {
        if (_renderTarget == 0 || text.IsEmpty)
        {
            return;
        }

        if (font is not DirectWriteFont dwFont)
        {
            throw new ArgumentException("Font must be a DirectWriteFont", nameof(font));
        }

        nint textFormat = 0;
        try
        {
            var weight = (DWRITE_FONT_WEIGHT)(int)dwFont.Weight;
            var style = dwFont.IsItalic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL;
            int hr = DWriteVTable.CreateTextFormat((IDWriteFactory*)_dwriteFactory, dwFont.Family, weight, style, (float)dwFont.Size, out textFormat);
            if (hr < 0 || textFormat == 0)
            {
                return;
            }

            DWriteVTable.SetTextAlignment(textFormat, horizontalAlignment switch
            {
                TextAlignment.Left => DWRITE_TEXT_ALIGNMENT.LEADING,
                TextAlignment.Center => DWRITE_TEXT_ALIGNMENT.CENTER,
                TextAlignment.Right => DWRITE_TEXT_ALIGNMENT.TRAILING,
                _ => DWRITE_TEXT_ALIGNMENT.LEADING
            });

            DWriteVTable.SetParagraphAlignment(textFormat, verticalAlignment switch
            {
                TextAlignment.Top => DWRITE_PARAGRAPH_ALIGNMENT.NEAR,
                TextAlignment.Center => DWRITE_PARAGRAPH_ALIGNMENT.CENTER,
                TextAlignment.Bottom => DWRITE_PARAGRAPH_ALIGNMENT.FAR,
                _ => DWRITE_PARAGRAPH_ALIGNMENT.NEAR
            });

            DWriteVTable.SetWordWrapping(textFormat, wrapping == TextWrapping.NoWrap ? DWRITE_WORD_WRAPPING.NO_WRAP : DWRITE_WORD_WRAPPING.WRAP);

            nint brush = GetSolidBrush(color);
            D2D1VTable.DrawText((ID2D1RenderTarget*)_renderTarget, text, textFormat, ToRectF(bounds), brush);
        }
        finally
        {
            ComHelpers.Release(textFormat);
        }
    }

    public Size MeasureText(ReadOnlySpan<char> text, IFont font) => MeasureText(text, font, float.MaxValue);

    public Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        if (font is not DirectWriteFont dwFont)
        {
            throw new ArgumentException("Font must be a DirectWriteFont", nameof(font));
        }

        nint textFormat = 0;
        nint textLayout = 0;
        try
        {
            var weight = (DWRITE_FONT_WEIGHT)(int)dwFont.Weight;
            var style = dwFont.IsItalic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL;
            int hr = DWriteVTable.CreateTextFormat((IDWriteFactory*)_dwriteFactory, dwFont.Family, weight, style, (float)dwFont.Size, out textFormat);
            if (hr < 0 || textFormat == 0)
            {
                return Size.Empty;
            }

            float w = maxWidth >= float.MaxValue ? float.MaxValue : (float)Math.Max(0, maxWidth);
            hr = DWriteVTable.CreateTextLayout((IDWriteFactory*)_dwriteFactory, text, textFormat, w, float.MaxValue, out textLayout);
            if (hr < 0 || textLayout == 0)
            {
                return Size.Empty;
            }

            hr = DWriteVTable.GetMetrics(textLayout, out var metrics);
            if (hr < 0)
            {
                return Size.Empty;
            }

            return new Size(metrics.widthIncludingTrailingWhitespace, metrics.height);
        }
        finally
        {
            ComHelpers.Release(textLayout);
            ComHelpers.Release(textFormat);
        }
    }

    public void DrawImage(IImage image, Point location) =>
        DrawImage(image, new Rect(location.X, location.Y, image.PixelWidth, image.PixelHeight));

    public void DrawImage(IImage image, Rect destRect) =>
        DrawImage(image, destRect, new Rect(0, 0, image.PixelWidth, image.PixelHeight));

    public void DrawImage(IImage image, Rect destRect, Rect sourceRect) =>
        DrawImageCore(
            image as Direct2DImage ?? throw new ArgumentException("Image must be a Direct2DImage", nameof(image)),
            destRect,
            sourceRect);

    private void DrawImageCore(Direct2DImage image, Rect destRect, Rect sourceRect)
    {
        if (_renderTarget == 0)
        {
            return;
        }

        int mipLevel = 0;
        if (ImageScaleQuality == ImageScaleQuality.HighQuality)
        {
            mipLevel = SelectHighQualityMipLevel(sourceRect, destRect, DpiScale);
        }

        nint bmp = mipLevel == 0
            ? image.GetOrCreateBitmap(_renderTarget, _renderTargetGeneration)
            : image.GetOrCreateBitmapForMip(_renderTarget, _renderTargetGeneration, mipLevel);
        if (bmp == 0)
        {
            return;
        }

        // Pixel-snap the destination rect in *world space* (including translation) to avoid shimmering
        // from sub-pixel bitmap sampling. GDI tends to quantize to integer pixels, so this brings
        // D2D closer to that visual stability.
        var worldDest = new Rect(destRect.X + _translateX, destRect.Y + _translateY, destRect.Width, destRect.Height);
        var snappedWorldDest = LayoutRounding.SnapRectEdgesToPixels(worldDest, DpiScale);
        var snappedLocalDest = new Rect(
            snappedWorldDest.X - _translateX,
            snappedWorldDest.Y - _translateY,
            snappedWorldDest.Width,
            snappedWorldDest.Height);

        var dst = ToRectF(snappedLocalDest);
        var src = mipLevel == 0
            ? new D2D1_RECT_F(
                left: (float)sourceRect.X,
                top: (float)sourceRect.Y,
                right: (float)sourceRect.Right,
                bottom: (float)sourceRect.Bottom)
            : CreateMipSourceRect(sourceRect, mipLevel);

        var interpolation = ImageScaleQuality switch
        {
            ImageScaleQuality.Fast => D2D1_BITMAP_INTERPOLATION_MODE.NEAREST_NEIGHBOR,
            _ => D2D1_BITMAP_INTERPOLATION_MODE.LINEAR,
        };

        D2D1VTable.DrawBitmap((ID2D1RenderTarget*)_renderTarget, bmp, dst, opacity: 1.0f, interpolation, src);
    }

    private static int SelectHighQualityMipLevel(Rect sourceRect, Rect destRect, double dpiScale)
    {
        // NOTE:
        // - sourceRect is in *source pixel space* (1 unit == 1 source pixel).
        // - destRect is in *DIP space*, so convert to device pixels before comparing.
        double destW = Math.Max(1e-6, destRect.Width * dpiScale);
        double destH = Math.Max(1e-6, destRect.Height * dpiScale);

        double srcW = Math.Max(1.0, sourceRect.Width);
        double srcH = Math.Max(1.0, sourceRect.Height);

        double scaleX = srcW / destW;
        double scaleY = srcH / destH;
        double scale = Math.Max(scaleX, scaleY);

        if (scale <= 2.0)
        {
            return 0;
        }

        // Reduce to a mip level where the remaining downscale is <= 2x, then let D2D linear handle the rest.
        int level = 0;
        while (scale > 2.0 && level < 12)
        {
            scale *= 0.5;
            level++;
        }

        return level;
    }

    private static D2D1_RECT_F CreateMipSourceRect(Rect sourceRect, int mipLevel)
    {
        double factor = 1 << Math.Min(mipLevel, 30);
        return new D2D1_RECT_F(
            left: (float)(sourceRect.X / factor),
            top: (float)(sourceRect.Y / factor),
            right: (float)(sourceRect.Right / factor),
            bottom: (float)(sourceRect.Bottom / factor));
    }

    private nint GetSolidBrush(Color color)
    {
        uint key = color.ToArgb();
        if (_solidBrushes.TryGetValue(key, out var brush) && brush != 0)
        {
            return brush;
        }

        int hr = D2D1VTable.CreateSolidColorBrush((ID2D1RenderTarget*)_renderTarget, ToColorF(color), out brush);
        if (hr < 0 || brush == 0)
        {
            return 0;
        }

        _solidBrushes[key] = brush;
        return brush;
    }

    private static D2D1_COLOR_F ToColorF(Color color) =>
        new(color.R / 255f, color.G / 255f, color.B / 255f, color.A / 255f);

    private D2D1_POINT_2F ToPoint2F(Point point) =>
        new((float)(point.X + _translateX), (float)(point.Y + _translateY));

    private D2D1_RECT_F ToRectF(Rect rect)
    {
        float left = (float)(rect.X + _translateX);
        float top = (float)(rect.Y + _translateY);
        float right = (float)(rect.Right + _translateX);
        float bottom = (float)(rect.Bottom + _translateY);
        return new D2D1_RECT_F(left, top, right, bottom);
    }

    private D2D1_RECT_F ToStrokeRectF(Rect rect, float thickness)
    {
        var r = ToRectF(rect);
        var snap = GetHalfPixelDipForStroke(thickness);
        if (snap == 0)
        {
            return r;
        }

        // Inset by half a device pixel so the centered stroke lands on pixel centers.
        return new D2D1_RECT_F(r.left + snap, r.top + snap, r.right - snap, r.bottom - snap);
    }

    private float QuantizeStrokeDip(float thickness)
    {
        if (thickness <= 0)
        {
            return 0;
        }

        // Snap stroke width to whole device pixels for consistent borders at fractional DPI scales
        // (e.g. 125%: 1 DIP == 1.25px -> render as 1px).
        float strokePx = thickness * (float)DpiScale;
        float snappedPx = Math.Max(1, (float)Math.Round(strokePx, MidpointRounding.AwayFromZero));
        return snappedPx / (float)DpiScale;
    }

    private float GetHalfPixelDipForStroke(float thickness)
    {
        if (thickness <= 0)
        {
            return 0;
        }

        float strokePx = thickness * (float)DpiScale;
        float rounded = (float)Math.Round(strokePx);
        if (Math.Abs(strokePx - rounded) > 0.001f)
        {
            return 0;
        }

        if (((int)rounded & 1) == 0)
        {
            return 0;
        }

        return 0.5f / (float)DpiScale;
    }
}
