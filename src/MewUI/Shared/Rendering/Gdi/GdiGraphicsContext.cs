using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Rendering.Gdi.Core;
using Aprillz.MewUI.Rendering.Gdi.Rendering;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI graphics context implementation with hybrid SDF/SSAA anti-aliasing.
/// Provides high-quality rendering while maintaining good performance.
/// </summary>
internal sealed class GdiGraphicsContext : IGraphicsContext
{
    private const byte OpaqueAlphaThreshold = 250;

    private readonly nint _hwnd;
    private readonly bool _ownsDc;
    private readonly GdiCurveQuality _curveQuality;
    private readonly ImageScaleQuality _imageScaleQuality;
    private readonly int _supersampleFactor;
    private readonly GdiBitmapRenderTarget? _bitmapTarget;

    // For bitmap rendering without hwnd
    private readonly int _pixelWidth;
    private readonly int _pixelHeight;

    private readonly GdiStateManager _stateManager;
    private readonly GdiPrimitiveRenderer _primitiveRenderer;
    private HybridShapeRenderer? _shapeRenderer;
    private readonly AaSurfacePool _surfacePool;
    private readonly AaSurface _alphaPixelSurface;
    private uint _alphaPixel;
    private bool _disposed;

    public double DpiScale => _stateManager.DpiScale;

    public ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    internal nint Hdc { get; }

    public GdiGraphicsContext(
        nint hwnd,
        nint hdc,
        double dpiScale,
        GdiCurveQuality curveQuality,
        ImageScaleQuality imageScaleQuality,
        bool ownsDc = false)
        : this(hwnd, hdc, 0, 0, dpiScale, curveQuality, imageScaleQuality, ownsDc)
    {
    }

    internal GdiGraphicsContext(
        nint hwnd,
        nint hdc,
        int pixelWidth,
        int pixelHeight,
        double dpiScale,
        GdiCurveQuality curveQuality,
        ImageScaleQuality imageScaleQuality,
        bool ownsDc = false,
        GdiBitmapRenderTarget? bitmapTarget = null)
    {
        _hwnd = hwnd;
        Hdc = hdc;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _ownsDc = ownsDc;
        _curveQuality = curveQuality;
        _imageScaleQuality = imageScaleQuality;
        _supersampleFactor = (int)curveQuality switch { 2 => 2, 3 => 3, _ => 1 };
        _bitmapTarget = bitmapTarget;

        _stateManager = new GdiStateManager(hdc, dpiScale);
        _primitiveRenderer = new GdiPrimitiveRenderer(hdc, _stateManager);
        _surfacePool = new AaSurfacePool();
        _alphaPixelSurface = new AaSurface(hdc, 1, 1);

        if (curveQuality != GdiCurveQuality.Fast)
        {
            _shapeRenderer = new HybridShapeRenderer(_surfacePool, _supersampleFactor);
        }

        // Set default modes
        Gdi32.SetBkMode(Hdc, GdiConstants.TRANSPARENT);
    }

    private HybridShapeRenderer GetShapeRendererForAlpha()
    {
        // GDI primitives don't support per-pixel alpha. For A<255 we render into a 32bpp surface and composite.
        // If the user selected Fast, use a 1x supersample factor (still supports alpha, just without extra SSAA).
        _shapeRenderer ??= new HybridShapeRenderer(_surfacePool, 1);
        return _shapeRenderer;
    }

    private static uint GetPremultipliedBgraPixel(Color color)
    {
        // Premultiply for AlphaBlend with AC_SRC_ALPHA.
        uint a = color.A;
        uint r = (uint)(color.R * a + 127) / 255;
        uint g = (uint)(color.G * a + 127) / 255;
        uint b = (uint)(color.B * a + 127) / 255;

        return (a << 24) | (r << 16) | (g << 8) | b;
    }

    private unsafe void AlphaFillRectangleFast(int destX, int destY, int width, int height, Color color)
    {
        if (width <= 0 || height <= 0 || color.A == 0)
        {
            return;
        }

        // Fast fill via stretching a cached 1x1 premultiplied pixel surface.
        // This avoids writing WxH pixels on CPU; AlphaBlend still covers the target area but with far less setup overhead.
        if (_alphaPixelSurface.IsValid)
        {
            uint pixel = GetPremultipliedBgraPixel(color);
            if (_alphaPixel != pixel)
            {
                byte* p = _alphaPixelSurface.GetRowPointer(0);
                if (p == null)
                {
                    return;
                }

                *(uint*)p = pixel;
                _alphaPixel = pixel;
            }

            _alphaPixelSurface.AlphaBlendToStretch(Hdc, destX, destY, width, height);
            return;
        }

        // Fallback: render into a dedicated surface and alpha blend.
        var surface = _surfacePool.Rent(Hdc, width, height);
        if (!surface.IsValid)
        {
            return;
        }

        try
        {
            uint pixel = GetPremultipliedBgraPixel(color);

            for (int y = 0; y < height; y++)
            {
                byte* rowPtr = surface.GetRowPointer(y);
                if (rowPtr == null)
                {
                    return;
                }

                new Span<uint>((void*)rowPtr, width).Fill(pixel);
            }

            surface.AlphaBlendTo(Hdc, destX, destY, width, height, 0, 0);
        }
        finally
        {
            _surfacePool.Return(surface);
        }
    }

    private unsafe void AlphaDrawRectangleFast(int destX, int destY, int width, int height, Color color, int thicknessPx)
    {
        if (width <= 0 || height <= 0 || color.A == 0 || thicknessPx <= 0)
        {
            return;
        }

        thicknessPx = Math.Min(thicknessPx, Math.Min(width / 2, height / 2));
        if (thicknessPx <= 0)
        {
            return;
        }

        // AlphaBlend is expensive; avoid blending a full WxH area for a thin border.
        // Render only the non-overlapping strips (top/bottom + left/right excluding corners).
        int topH = Math.Min(thicknessPx, height);
        int bottomStart = Math.Max(topH, height - thicknessPx);
        int bottomH = height - bottomStart;

        int middleY = topH;
        int middleH = Math.Max(0, bottomStart - topH);

        int sideW = Math.Min(thicknessPx, width);
        int rightX = width - sideW;

        long fullArea = (long)width * height;
        long borderArea = (long)width * topH + (long)width * bottomH;
        if (middleH > 0 && sideW > 0)
        {
            // Left + right borders (excluding top/bottom already counted).
            borderArea += (long)sideW * middleH;
            if (rightX != 0)
            {
                borderArea += (long)sideW * middleH;
            }
        }

        // Heuristic: for small rects or thick borders, a single AlphaBlend can be cheaper than 3-4 calls.
        bool useSingleBlend = fullArea <= 4096 || borderArea * 2 >= fullArea;

        if (useSingleBlend || !_alphaPixelSurface.IsValid)
        {
            var surface = _surfacePool.Rent(Hdc, width, height);
            if (!surface.IsValid)
            {
                return;
            }

            try
            {
                uint pixel = GetPremultipliedBgraPixel(color);

                for (int y = 0; y < height; y++)
                {
                    byte* rowPtr = surface.GetRowPointer(y);
                    if (rowPtr == null)
                    {
                        return;
                    }

                    var row = new Span<uint>((void*)rowPtr, width);

                    if (y < topH || y >= bottomStart)
                    {
                        row.Fill(pixel);
                        continue;
                    }

                    row.Clear();
                    if (sideW > 0)
                    {
                        row.Slice(0, sideW).Fill(pixel);
                        if (rightX != 0)
                        {
                            row.Slice(rightX, sideW).Fill(pixel);
                        }
                    }
                }

                surface.AlphaBlendTo(Hdc, destX, destY, width, height, 0, 0);
            }
            finally
            {
                _surfacePool.Return(surface);
            }

            return;
        }

        // Thin border: minimize blended area by drawing strips.
        if (topH > 0)
        {
            AlphaFillRectangleFast(destX, destY, width, topH, color);
        }

        if (bottomH > 0)
        {
            AlphaFillRectangleFast(destX, destY + bottomStart, width, bottomH, color);
        }

        if (middleH > 0 && sideW > 0 && rightX >= 0)
        {
            AlphaFillRectangleFast(destX, destY + middleY, sideW, middleH, color);

            if (rightX != 0)
            {
                AlphaFillRectangleFast(destX + rightX, destY + middleY, sideW, middleH, color);
            }
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsDc && Hdc != 0)
            {
                User32.ReleaseDC(_hwnd, Hdc);
            }

            _alphaPixelSurface.Dispose();
            _primitiveRenderer.Dispose();
            _surfacePool.Dispose();

            _disposed = true;
        }
    }

    #region State Management

    public void Save()
    {
        _stateManager.Save();
    }

    public void Restore()
    {
        _stateManager.Restore();
    }

    public void SetClip(Rect rect)
    {
        _stateManager.SetClip(rect);
    }

    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY)
    {
        // GDI only supports rectangular clip regions.
        _stateManager.SetClip(rect);
    }

    public void Translate(double dx, double dy) => _stateManager.Translate(dx, dy);

    #endregion

    #region Drawing Primitives

    public void Clear(Color color)
    {
        if (_bitmapTarget != null)
        {
            _bitmapTarget.Clear(color);
        }
        else if (_hwnd != 0)
        {
            _primitiveRenderer.Clear(_hwnd, color);
        }
        else if (_pixelWidth > 0 && _pixelHeight > 0)
        {
            _primitiveRenderer.Clear(_pixelWidth, _pixelHeight, color);
        }
    }

    public void DrawLine(Point start, Point end, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0)
        {
            return;
        }

        if (color.A < 255)
        {
            var (ax, ay) = _stateManager.ToDeviceCoords(start.X, start.Y);
            var (bx, by) = _stateManager.ToDeviceCoords(end.X, end.Y);

            float strokePx = (float)_stateManager.ToDevicePx(thickness);
            GetShapeRendererForAlpha().DrawLine(
                Hdc,
                (float)ax, (float)ay,
                (float)bx, (float)by,
                strokePx,
                color.B, color.G, color.R, color.A);
            return;
        }

        // Check if line is axis-aligned (no AA needed)
        var (ax0, ay0) = _stateManager.ToDeviceCoords(start.X, start.Y);
        var (bx0, by0) = _stateManager.ToDeviceCoords(end.X, end.Y);

        double dx = bx0 - ax0;
        double dy = by0 - ay0;

        bool isAxisAligned = Math.Abs(dx) < GdiRenderingConstants.Epsilon || Math.Abs(dy) < GdiRenderingConstants.Epsilon;

        if (isAxisAligned || _curveQuality == GdiCurveQuality.Fast || _shapeRenderer == null)
        {
            _primitiveRenderer.DrawLine(start, end, color, thickness);
            return;
        }

        // Use AA renderer for non-axis-aligned lines
        float strokePx1 = (float)_stateManager.ToDevicePx(thickness);
        _shapeRenderer.DrawLine(
            Hdc,
            (float)ax0, (float)ay0,
            (float)bx0, (float)by0,
            strokePx1,
            color.B, color.G, color.R, color.A);
    }

    public void DrawRectangle(Rect rect, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0)
        {
            return;
        }

        var dst = _stateManager.ToDeviceRect(rect);
        if (dst.Width <= 0 || dst.Height <= 0)
        {
            return;
        }

        if (color.A < 255)
        {
            int tPx = _stateManager.QuantizePenWidthPx(thickness);
            AlphaDrawRectangleFast(dst.left, dst.top, dst.Width, dst.Height, color, tPx);
            return;
        }

        _primitiveRenderer.DrawRectangle(rect, color, thickness);
    }

    public void FillRectangle(Rect rect, Color color)
    {
        if (color.A == 0)
        {
            return;
        }

        var dst = _stateManager.ToDeviceRect(rect);
        if (dst.Width <= 0 || dst.Height <= 0)
        {
            return;
        }

        if (color.A < 255)
        {
            AlphaFillRectangleFast(dst.left, dst.top, dst.Width, dst.Height, color);
            return;
        }

        _primitiveRenderer.FillRectangle(rect, color);
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0)
        {
            return;
        }

        if (color.A == 255 && (_shapeRenderer == null || _curveQuality == GdiCurveQuality.Fast))
        {
            _primitiveRenderer.DrawRoundedRectangle(rect, radiusX, radiusY, color, thickness);
            return;
        }

        var renderer = _shapeRenderer ?? GetShapeRendererForAlpha();

        var dst = _stateManager.ToDeviceRect(rect);
        if (dst.Width <= 0 || dst.Height <= 0)
        {
            return;
        }

        float rx = (float)_stateManager.ToDevicePx(radiusX);
        float ry = (float)_stateManager.ToDevicePx(radiusY);
        float strokePx = (float)_stateManager.ToDevicePx(thickness);

        renderer.DrawRoundedRectangle(
            Hdc,
            dst.left, dst.top,
            dst.Width, dst.Height,
            rx, ry, strokePx,
            color.B, color.G, color.R, color.A);
    }

    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color)
    {
        if (color.A == 0)
        {
            return;
        }

        if (color.A == 255 && (_shapeRenderer == null || _curveQuality == GdiCurveQuality.Fast))
        {
            _primitiveRenderer.FillRoundedRectangle(rect, radiusX, radiusY, color);
            return;
        }

        var dst = _stateManager.ToDeviceRect(rect);
        if (dst.Width <= 0 || dst.Height <= 0)
        {
            return;
        }

        float rx = (float)_stateManager.ToDevicePx(radiusX);
        float ry = (float)_stateManager.ToDevicePx(radiusY);

        var renderer = _shapeRenderer ?? GetShapeRendererForAlpha();

        renderer.FillRoundedRectangle(
            Hdc,
            dst.left, dst.top,
            dst.Width, dst.Height,
            rx, ry,
            color.B, color.G, color.R, color.A);
    }

    public void DrawEllipse(Rect bounds, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0)
        {
            return;
        }

        if (color.A == 255 && (_shapeRenderer == null || _curveQuality == GdiCurveQuality.Fast))
        {
            _primitiveRenderer.DrawEllipse(bounds, color, thickness);
            return;
        }

        var renderer = _shapeRenderer ?? GetShapeRendererForAlpha();

        var dst = _stateManager.ToDeviceRect(bounds);
        if (dst.Width <= 0 || dst.Height <= 0)
        {
            return;
        }

        float strokePx = (float)_stateManager.ToDevicePx(thickness);

        renderer.DrawEllipse(
            Hdc,
            dst.left, dst.top,
            dst.Width, dst.Height,
            strokePx,
            color.B, color.G, color.R, color.A);
    }

    public void FillEllipse(Rect bounds, Color color)
    {
        if (color.A == 0)
        {
            return;
        }

        if (color.A == 255 && (_shapeRenderer == null || _curveQuality == GdiCurveQuality.Fast))
        {
            _primitiveRenderer.FillEllipse(bounds, color);
            return;
        }

        var renderer = _shapeRenderer ?? GetShapeRendererForAlpha();

        var dst = _stateManager.ToDeviceRect(bounds);
        if (dst.Width <= 0 || dst.Height <= 0)
        {
            return;
        }

        renderer.FillEllipse(
            Hdc,
            dst.left, dst.top,
            dst.Width, dst.Height,
            color.B, color.G, color.R, color.A);
    }

    #endregion

    #region Text Rendering

    public unsafe void DrawText(ReadOnlySpan<char> text, Point location, IFont font, Color color)
    {
        if (font is not GdiFont gdiFont)
        {
            throw new ArgumentException("Font must be a GdiFont", nameof(font));
        }

        if (_bitmapTarget?.PresentationMode == GdiPresentationMode.PerPixelAlpha)
        {
            if (text.IsEmpty || color.A == 0)
            {
                return;
            }

            var size = MeasureText(text, gdiFont);
            int w = _stateManager.QuantizeLengthPx(size.Width);
            int h = _stateManager.QuantizeLengthPx(size.Height);
            if (w <= 0 || h <= 0)
            {
                return;
            }

            var pt = _stateManager.ToDevicePoint(location);
            var r = RECT.FromLTRB(pt.x, pt.y, pt.x + w, pt.y + h);
            uint format = GdiConstants.DT_NOPREFIX | GdiConstants.DT_SINGLELINE | GdiConstants.DT_LEFT | GdiConstants.DT_TOP;
            PerPixelAlphaTextRenderer.DrawText(Hdc, _bitmapTarget, _surfacePool, text, r, gdiFont, color, format);
            return;
        }

        var oldFont = Gdi32.SelectObject(Hdc, gdiFont.GetHandle(GdiFontRenderMode.Default));
        var oldColor = Gdi32.SetTextColor(Hdc, color.ToCOLORREF());

        try
        {
            var pt = _stateManager.ToDevicePoint(location);
            fixed (char* pText = text)
            {
                Gdi32.TextOut(Hdc, pt.x, pt.y, pText, text.Length);
            }
        }
        finally
        {
            Gdi32.SetTextColor(Hdc, oldColor);
            Gdi32.SelectObject(Hdc, oldFont);
        }
    }

    public unsafe void DrawText(
        ReadOnlySpan<char> text,
        Rect bounds,
        IFont font,
        Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap)
    {
        if (font is not GdiFont gdiFont)
        {
            throw new ArgumentException("Font must be a GdiFont", nameof(font));
        }

        if (_bitmapTarget?.PresentationMode == GdiPresentationMode.PerPixelAlpha)
        {
            if (text.IsEmpty || color.A == 0)
            {
                return;
            }

            var r = GetTextLayoutRect(bounds, wrapping);
            uint format = BuildTextFormat(horizontalAlignment, verticalAlignment, wrapping);
            int yOffsetPx = 0;
            int textHeightPx = 0;
            if (wrapping != TextWrapping.NoWrap)
            {
                ComputeWrappedTextOffsetsPx(
                    text,
                    gdiFont.GetHandle(GdiFontRenderMode.Coverage),
                    r.Width,
                    r.Height,
                    verticalAlignment,
                    out yOffsetPx,
                    out textHeightPx);
            }

            PerPixelAlphaTextRenderer.DrawText(Hdc, _bitmapTarget, _surfacePool, text, r, gdiFont, color, format, yOffsetPx, textHeightPx);
            return;
        }

        var oldFont = Gdi32.SelectObject(Hdc, gdiFont.GetHandle(GdiFontRenderMode.Default));
        var oldColor = Gdi32.SetTextColor(Hdc, color.ToCOLORREF());

        try
        {
            var r = GetTextLayoutRect(bounds, wrapping);
            uint format = BuildTextFormat(horizontalAlignment, verticalAlignment, wrapping);
            int yOffsetPx = 0;
            int textHeightPx = 0;
            if (wrapping != TextWrapping.NoWrap)
            {
                ComputeWrappedTextOffsetsPx(
                    text,
                    gdiFont.GetHandle(GdiFontRenderMode.Default),
                    r.Width,
                    r.Height,
                    verticalAlignment,
                    out yOffsetPx,
                    out textHeightPx);
            }

            int clipState = ApplyTextClip(r, wrapping);

            fixed (char* pText = text)
            {
                ApplyVerticalOffset(ref r, yOffsetPx, textHeightPx);
                Gdi32.DrawText(Hdc, pText, text.Length, ref r, format);
            }

            RestoreTextClip(clipState);
        }
        finally
        {
            Gdi32.SetTextColor(Hdc, oldColor);
            Gdi32.SelectObject(Hdc, oldFont);
        }
    }

    private int ApplyTextClip(RECT boundsPx, TextWrapping wrapping)
    {
        if (wrapping == TextWrapping.NoWrap)
        {
            return 0;
        }

        int clipState = Gdi32.SaveDC(Hdc);
        if (clipState != 0)
        {
            Gdi32.IntersectClipRect(Hdc, boundsPx.left, boundsPx.top, boundsPx.right, boundsPx.bottom);
        }

        return clipState;
    }

    private void RestoreTextClip(int clipState)
    {
        if (clipState != 0)
        {
            Gdi32.RestoreDC(Hdc, clipState);
        }
    }

    private RECT GetTextLayoutRect(Rect bounds, TextWrapping wrapping)
    {
        if (wrapping == TextWrapping.NoWrap)
        {
            return _stateManager.ToDeviceRect(bounds);
        }

        // Keep wrap width consistent with measurement.
        // Measurement uses QuantizeLengthPx(maxWidth), while ToDeviceRect rounds left/right edges
        // independently and can shrink/grow width by 1px depending on subpixel X.
        // That can change word-wrapping and produce a render height larger than DesiredSize.
        var tl = _stateManager.ToDevicePoint(bounds.TopLeft);
        int w = _stateManager.QuantizeLengthPx(bounds.Width);
        int h = _stateManager.QuantizeLengthPx(bounds.Height);
        if (w <= 0)
        {
            w = 1;
        }
        if (h <= 0)
        {
            h = 1;
        }

        return RECT.FromLTRB(tl.x, tl.y, tl.x + w, tl.y + h);
    }

    private unsafe void ComputeWrappedTextOffsetsPx(
        ReadOnlySpan<char> text,
        nint fontHandle,
        int widthPx,
        int heightPx,
        TextAlignment verticalAlignment,
        out int yOffsetPx,
        out int textHeightPx)
    {
        if (verticalAlignment == TextAlignment.Top)
        {
            yOffsetPx = 0;
            textHeightPx = 0;
            return;
        }

        if (widthPx <= 0 || heightPx <= 0 || text.IsEmpty || fontHandle == 0 || Hdc == 0)
        {
            yOffsetPx = 0;
            textHeightPx = 0;
            return;
        }

        var oldFont = Gdi32.SelectObject(Hdc, fontHandle);
        try
        {
            var rect = new RECT(0, 0, widthPx, 0);
            fixed (char* pText = text)
            {
                Gdi32.DrawText(Hdc, pText, text.Length, ref rect,
                    GdiConstants.DT_CALCRECT | GdiConstants.DT_WORDBREAK | GdiConstants.DT_NOPREFIX);
            }

            textHeightPx = rect.Height;
            int remaining = heightPx - textHeightPx;
            if (remaining <= 0)
            {
                yOffsetPx = 0;
                return;
            }

            yOffsetPx = verticalAlignment == TextAlignment.Bottom
                ? remaining
                : remaining / 2;
        }
        finally
        {
            Gdi32.SelectObject(Hdc, oldFont);
        }
    }

    private static uint BuildTextFormat(TextAlignment horizontalAlignment, TextAlignment verticalAlignment, TextWrapping wrapping)
    {
        uint format = GdiConstants.DT_NOPREFIX;
        format |= horizontalAlignment switch
        {
            TextAlignment.Left => GdiConstants.DT_LEFT,
            TextAlignment.Center => GdiConstants.DT_CENTER,
            TextAlignment.Right => GdiConstants.DT_RIGHT,
            _ => GdiConstants.DT_LEFT
        };

        if (wrapping == TextWrapping.NoWrap)
        {
            format |= GdiConstants.DT_SINGLELINE;
            format |= verticalAlignment switch
            {
                TextAlignment.Top => GdiConstants.DT_TOP,
                TextAlignment.Center => GdiConstants.DT_VCENTER,
                TextAlignment.Bottom => GdiConstants.DT_BOTTOM,
                _ => GdiConstants.DT_TOP
            };
        }
        else
        {
            format |= GdiConstants.DT_WORDBREAK;
        }

        return format;
    }

    private static void ApplyVerticalOffset(ref RECT rect, int yOffsetPx, int textHeightPx)
    {
        if (yOffsetPx != 0)
        {
            rect.top += yOffsetPx;
            rect.bottom += yOffsetPx;
        }
        if (textHeightPx > 0)
        {
            rect.bottom = rect.top + textHeightPx;
        }
    }

    public unsafe Size MeasureText(ReadOnlySpan<char> text, IFont font)
    {
        if (font is not GdiFont gdiFont)
        {
            throw new ArgumentException("Font must be a GdiFont", nameof(font));
        }

        var oldFont = Gdi32.SelectObject(Hdc, gdiFont.Handle);

        try
        {
            if (text.IsEmpty)
            {
                return Size.Empty;
            }

            var hasLineBreaks = text.IndexOfAny('\r', '\n') >= 0;
            var rect = hasLineBreaks
                ? new RECT(0, 0, _stateManager.QuantizeLengthPx(1_000_000), 0)
                : new RECT(0, 0, 0, 0);

            uint format = hasLineBreaks
                ? GdiConstants.DT_CALCRECT | GdiConstants.DT_WORDBREAK | GdiConstants.DT_NOPREFIX
                : GdiConstants.DT_CALCRECT | GdiConstants.DT_SINGLELINE | GdiConstants.DT_NOPREFIX;

            fixed (char* pText = text)
            {
                Gdi32.DrawText(Hdc, pText, text.Length, ref rect, format);
            }

            return new Size(TextMeasurePolicy.ApplyWidthPadding(rect.Width) / DpiScale, rect.Height / DpiScale);
        }
        finally
        {
            Gdi32.SelectObject(Hdc, oldFont);
        }
    }

    public unsafe Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (font is not GdiFont gdiFont)
        {
            throw new ArgumentException("Font must be a GdiFont", nameof(font));
        }

        var oldFont = Gdi32.SelectObject(Hdc, gdiFont.Handle);

        try
        {
            if (double.IsNaN(maxWidth) || maxWidth <= 0 || double.IsInfinity(maxWidth))
            {
                maxWidth = 1_000_000;
            }

            var rect = new RECT(0, 0, _stateManager.QuantizeLengthPx(maxWidth), 0);

            fixed (char* pText = text)
            {
                Gdi32.DrawText(Hdc, pText, text.Length, ref rect,
                    GdiConstants.DT_CALCRECT | GdiConstants.DT_WORDBREAK | GdiConstants.DT_NOPREFIX);
            }

            return new Size(TextMeasurePolicy.ApplyWidthPadding(rect.Width) / DpiScale, rect.Height / DpiScale);
        }
        finally
        {
            Gdi32.SelectObject(Hdc, oldFont);
        }
    }

    #endregion

    #region Image Rendering

    public void DrawImage(IImage image, Point location)
    {
        if (image is not GdiImage gdiImage)
        {
            throw new ArgumentException("Image must be a GdiImage", nameof(image));
        }

        DrawImage(gdiImage, new Rect(location.X, location.Y, image.PixelWidth, image.PixelHeight));
    }

    public void DrawImage(IImage image, Rect destRect)
    {
        if (image is not GdiImage gdiImage)
        {
            throw new ArgumentException("Image must be a GdiImage", nameof(image));
        }

        DrawImage(gdiImage, destRect, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
    }

    public void DrawImage(IImage image, Rect destRect, Rect sourceRect)
    {
        if (image is not GdiImage gdiImage)
        {
            throw new ArgumentException("Image must be a GdiImage", nameof(image));
        }

        DrawImageCore(gdiImage, destRect, sourceRect);
    }

    private void DrawImageCore(GdiImage gdiImage, Rect destRect, Rect sourceRect)
    {
        gdiImage.EnsureUpToDate();

        var destPx = _stateManager.ToDeviceRect(destRect);
        if (destPx.Width <= 0 || destPx.Height <= 0)
        {
            return;
        }

        // Resolve backend default:
        // - Default => factory default (which is Linear by default to match other backends)
        // - NearestNeighbor => GDI stretch with COLORONCOLOR (fast, pixelated)
        // - Linear => cached bilinear resample
        // - HighQuality => cached prefiltered downscale + bilinear
        var effective = ImageScaleQuality == ImageScaleQuality.Default
            ? (_imageScaleQuality == ImageScaleQuality.Default ? ImageScaleQuality.Normal : _imageScaleQuality)
            : ImageScaleQuality;

        var memDc = Gdi32.CreateCompatibleDC(Hdc);
        var oldBitmap = Gdi32.SelectObject(memDc, gdiImage.Handle);

        try
        {
            int srcX = (int)sourceRect.X;
            int srcY = (int)sourceRect.Y;
            int srcW = (int)sourceRect.Width;
            int srcH = (int)sourceRect.Height;

            if (effective == ImageScaleQuality.Fast)
            {
                // Nearest: rely on GDI stretch + alpha blend (COLORONCOLOR).
                int oldStretchMode = Gdi32.SetStretchBltMode(Hdc, GdiConstants.COLORONCOLOR);
                try
                {
                    var blend = BLENDFUNCTION.SourceOver(255);
                    Gdi32.AlphaBlend(
                        Hdc, destPx.left, destPx.top, destPx.Width, destPx.Height,
                        memDc, srcX, srcY, srcW, srcH,
                        blend);
                }
                finally
                {
                    if (oldStretchMode != 0)
                    {
                        Gdi32.SetStretchBltMode(Hdc, oldStretchMode);
                    }
                }

                return;
            }

            // Linear/HighQuality: try cached scaled bitmap for deterministic, backend-independent resampling.
            // This trades memory for speed when the same image is drawn repeatedly at the same scaled size
            // (common in UI).
            //
            // For HighQuality, allow rounding the source rect to whole pixels so ViewBox/UniformToFill
            // cases can still take the resample-cache path (otherwise we'd fall back to GDI stretch).
            bool srcAligned =
                IsNearInt(sourceRect.X) && IsNearInt(sourceRect.Y) &&
                IsNearInt(sourceRect.Width) && IsNearInt(sourceRect.Height);

            int scaledSrcX = srcX;
            int scaledSrcY = srcY;
            int scaledSrcW = srcW;
            int scaledSrcH = srcH;

            if (!srcAligned && effective == ImageScaleQuality.HighQuality)
            {
                int left = (int)Math.Round(sourceRect.X);
                int top = (int)Math.Round(sourceRect.Y);
                int right = (int)Math.Round(sourceRect.Right);
                int bottom = (int)Math.Round(sourceRect.Bottom);

                if (right > left && bottom > top)
                {
                    scaledSrcX = left;
                    scaledSrcY = top;
                    scaledSrcW = right - left;
                    scaledSrcH = bottom - top;
                    srcAligned = true;
                }
            }

            if (srcAligned &&
                gdiImage.TryGetOrCreateScaledBitmap(scaledSrcX, scaledSrcY, scaledSrcW, scaledSrcH, destPx.Width, destPx.Height, effective, out var scaledBmp))
            {
                var scaledDc = Gdi32.CreateCompatibleDC(Hdc);
                var oldScaled = Gdi32.SelectObject(scaledDc, scaledBmp);
                try
                {
                    var blendScaled = BLENDFUNCTION.SourceOver(255);
                    Gdi32.AlphaBlend(
                        Hdc, destPx.left, destPx.top, destPx.Width, destPx.Height,
                        scaledDc, 0, 0, destPx.Width, destPx.Height,
                        blendScaled);
                }
                finally
                {
                    Gdi32.SelectObject(scaledDc, oldScaled);
                    Gdi32.DeleteDC(scaledDc);
                }

                return;
            }

            // Fallback: if we can't use the cache (e.g. fractional sourceRect), use GDI stretch + alpha blend.
            // Prefer linear as the "Default" behavior to match other backends.
            // NOTE: GDI has no true "linear" filter; HALFTONE is the best available built-in option.
            int stretch = GdiConstants.HALFTONE;
            int oldMode = Gdi32.SetStretchBltMode(Hdc, stretch);
            var oldBrushOrg = default(POINT);
            bool hasBrushOrg = stretch == GdiConstants.HALFTONE;
            if (hasBrushOrg)
            {
                Gdi32.SetBrushOrgEx(Hdc, 0, 0, out oldBrushOrg);
            }

            try
            {
                var blend = BLENDFUNCTION.SourceOver(255);
                Gdi32.AlphaBlend(
                    Hdc, destPx.left, destPx.top, destPx.Width, destPx.Height,
                    memDc, srcX, srcY, srcW, srcH,
                    blend);
            }
            finally
            {
                if (oldMode != 0)
                {
                    Gdi32.SetStretchBltMode(Hdc, oldMode);
                }

                if (hasBrushOrg)
                {
                    Gdi32.SetBrushOrgEx(Hdc, oldBrushOrg.x, oldBrushOrg.y, out _);
                }
            }
        }
        finally
        {
            Gdi32.SelectObject(memDc, oldBitmap);
            Gdi32.DeleteDC(memDc);
        }
    }

    private static bool IsNearInt(double value) => Math.Abs(value - Math.Round(value)) <= 0.0001;

    #endregion
}
