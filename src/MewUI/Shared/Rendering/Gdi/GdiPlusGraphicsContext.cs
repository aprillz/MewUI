using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi.Core;
using Aprillz.MewUI.Rendering.Gdi.Rendering;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI+ graphics context (vector/clip quality), while keeping GDI text measurement/rendering.
/// </summary>
internal sealed class GdiPlusGraphicsContext : IGraphicsContext
{
    private readonly nint _hwnd;
    private readonly bool _ownsDc;
    private readonly ImageScaleQuality _imageScaleQuality;
    private readonly GdiBitmapRenderTarget? _bitmapTarget;

    private readonly int _pixelWidth;
    private readonly int _pixelHeight;

    private readonly GdiStateManager _textStateManager;
    private readonly GdiPrimitiveRenderer _primitiveRenderer;
    private readonly AaSurfacePool _surfacePool = new();

    private nint _graphics;
    private readonly Stack<GraphicsStateSnapshot> _states = new();
    private bool _disposed;
    private readonly double _dpiScale;
    private double _translateX;
    private double _translateY;

    public double DpiScale => _dpiScale;

    public ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Default;

    internal nint Hdc { get; }

    public GdiPlusGraphicsContext(
        nint hwnd,
        nint hdc,
        double dpiScale,
        ImageScaleQuality imageScaleQuality,
        bool ownsDc = false)
        : this(hwnd, hdc, 0, 0, dpiScale, imageScaleQuality, ownsDc)
    {
    }

    internal GdiPlusGraphicsContext(
        nint hwnd,
        nint hdc,
        int pixelWidth,
        int pixelHeight,
        double dpiScale,
        ImageScaleQuality imageScaleQuality,
        bool ownsDc = false,
        GdiBitmapRenderTarget? bitmapTarget = null)
    {
        _hwnd = hwnd;
        Hdc = hdc;
        _pixelWidth = pixelWidth;
        _pixelHeight = pixelHeight;
        _ownsDc = ownsDc;
        _imageScaleQuality = imageScaleQuality;
        _bitmapTarget = bitmapTarget;

        _dpiScale = dpiScale;
        _textStateManager = new GdiStateManager(hdc, dpiScale);
        _primitiveRenderer = new GdiPrimitiveRenderer(hdc, _textStateManager);

        Gdi32.SetBkMode(Hdc, GdiConstants.TRANSPARENT);
        GdiPlusInterop.EnsureInitialized();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        _surfacePool.Dispose();

        if (_graphics != 0)
        {
            GdiPlusInterop.GdipDeleteGraphics(_graphics);
            _graphics = 0;
        }

        if (_ownsDc && Hdc != 0)
        {
            User32.ReleaseDC(_hwnd, Hdc);
        }
    }

    #region State Management

    public void Save()
    {
        _textStateManager.Save();

        uint state = 0;
        if (EnsureGraphics())
        {
            GdiPlusInterop.GdipSaveGraphics(_graphics, out state);
        }

        _states.Push(new GraphicsStateSnapshot
        {
            GdiPlusState = state,
            TranslateX = _translateX,
            TranslateY = _translateY,
        });
    }

    public void Restore()
    {
        _textStateManager.Restore();

        if (_states.Count == 0)
        {
            return;
        }

        var state = _states.Pop();
        _translateX = state.TranslateX;
        _translateY = state.TranslateY;

        if (_graphics != 0 && state.GdiPlusState != 0)
        {
            GdiPlusInterop.GdipRestoreGraphics(_graphics, state.GdiPlusState);
        }
    }

    public void SetClip(Rect rect)
    {
        // Keep HDC clip in sync for GDI text rendering.
        _textStateManager.SetClip(rect);

        if (!EnsureGraphics())
        {
            return;
        }

        var clip = LayoutRounding.SnapViewportRectToPixels(rect, _dpiScale);
        var r = ToDeviceRect(clip);
        GdiPlusInterop.GdipSetClipRectI(_graphics, r.left, r.top, r.Width, r.Height, GdiPlusInterop.CombineMode.Intersect);
    }

    public void SetClipRoundedRect(Rect rect, double radiusX, double radiusY)
    {
        // GDI text can only do rectangle clip; keep it close to bounds.
        _textStateManager.SetClip(rect);

        if (!EnsureGraphics())
        {
            return;
        }

        if (radiusX <= 0 && radiusY <= 0)
        {
            SetClip(rect);
            return;
        }

        var clip = LayoutRounding.SnapViewportRectToPixels(rect, _dpiScale);
        var r = ToDeviceRect(clip);
        int ellipseW = Math.Max(1, QuantizeLengthPx(radiusX * 2));
        int ellipseH = Math.Max(1, QuantizeLengthPx(radiusY * 2));

        if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0)
        {
            return;
        }

        try
        {
            AddRoundedRectPathI(path, r.left, r.top, r.Width, r.Height, ellipseW, ellipseH);
            GdiPlusInterop.GdipClosePathFigure(path);
            GdiPlusInterop.GdipSetClipPath(_graphics, path, GdiPlusInterop.CombineMode.Intersect);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(path);
        }
    }

    public void Translate(double dx, double dy)
    {
        _translateX += dx;
        _translateY += dy;
        _textStateManager.Translate(dx, dy);
    }

    #endregion

    #region Drawing Primitives (GDI+)

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
        if (color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        float widthPx = (float)ToDevicePx(thickness);
        if (widthPx <= 0)
        {
            return;
        }

        var (ax, ay) = ToDeviceCoords(start.X, start.Y);
        var (bx, by) = ToDeviceCoords(end.X, end.Y);

        if (GdiPlusInterop.GdipCreatePen1(ToArgb(color), widthPx, GdiPlusInterop.Unit.Pixel, out var pen) != 0 || pen == 0)
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipDrawLineI(_graphics, pen, (int)Math.Round(ax), (int)Math.Round(ay), (int)Math.Round(bx), (int)Math.Round(by));
        }
        finally
        {
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    public void DrawRectangle(Rect rect, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        float widthPx = (float)ToDevicePx(thickness);
        if (widthPx <= 0)
        {
            return;
        }

        if (GdiPlusInterop.GdipCreatePen1(ToArgb(color), widthPx, GdiPlusInterop.Unit.Pixel, out var pen) != 0 || pen == 0)
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipDrawRectangleI(_graphics, pen, r.left, r.top, r.Width, r.Height);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    public void FillRectangle(Rect rect, Color color)
    {
        if (color.A == 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        if (GdiPlusInterop.GdipCreateSolidFill(ToArgb(color), out var brush) != 0 || brush == 0)
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipFillRectangleI(_graphics, brush, r.left, r.top, r.Width, r.Height);
        }
        finally
        {
            GdiPlusInterop.GdipDeleteBrush(brush);
        }
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        float widthPx = (float)ToDevicePx(thickness);
        int ew = Math.Max(1, QuantizeLengthPx(radiusX * 2));
        int eh = Math.Max(1, QuantizeLengthPx(radiusY * 2));

        if (GdiPlusInterop.GdipCreatePen1(ToArgb(color), widthPx, GdiPlusInterop.Unit.Pixel, out var pen) != 0 || pen == 0)
        {
            return;
        }

        if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0)
        {
            GdiPlusInterop.GdipDeletePen(pen);
            return;
        }

        try
        {
            AddRoundedRectPathI(path, r.left, r.top, r.Width, r.Height, ew, eh);
            GdiPlusInterop.GdipClosePathFigure(path);
            GdiPlusInterop.GdipDrawPath(_graphics, pen, path);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(path);
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    public void FillRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color)
    {
        if (color.A == 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(rect);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        int ew = Math.Max(1, QuantizeLengthPx(radiusX * 2));
        int eh = Math.Max(1, QuantizeLengthPx(radiusY * 2));

        if (GdiPlusInterop.GdipCreateSolidFill(ToArgb(color), out var brush) != 0 || brush == 0)
        {
            return;
        }

        if (GdiPlusInterop.GdipCreatePath(GdiPlusInterop.FillMode.Winding, out var path) != 0 || path == 0)
        {
            GdiPlusInterop.GdipDeleteBrush(brush);
            return;
        }

        try
        {
            AddRoundedRectPathI(path, r.left, r.top, r.Width, r.Height, ew, eh);
            GdiPlusInterop.GdipClosePathFigure(path);
            GdiPlusInterop.GdipFillPath(_graphics, brush, path);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePath(path);
            GdiPlusInterop.GdipDeleteBrush(brush);
        }
    }

    public void DrawEllipse(Rect bounds, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(bounds);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        float widthPx = (float)ToDevicePx(thickness);
        if (GdiPlusInterop.GdipCreatePen1(ToArgb(color), widthPx, GdiPlusInterop.Unit.Pixel, out var pen) != 0 || pen == 0)
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipDrawEllipseI(_graphics, pen, r.left, r.top, r.Width, r.Height);
        }
        finally
        {
            GdiPlusInterop.GdipDeletePen(pen);
        }
    }

    public void FillEllipse(Rect bounds, Color color)
    {
        if (color.A == 0 || !EnsureGraphics())
        {
            return;
        }

        var r = ToDeviceRect(bounds);
        if (r.Width <= 0 || r.Height <= 0)
        {
            return;
        }

        if (GdiPlusInterop.GdipCreateSolidFill(ToArgb(color), out var brush) != 0 || brush == 0)
        {
            return;
        }

        try
        {
            GdiPlusInterop.GdipFillEllipseI(_graphics, brush, r.left, r.top, r.Width, r.Height);
        }
        finally
        {
            GdiPlusInterop.GdipDeleteBrush(brush);
        }
    }

    #endregion

    #region Text Rendering (GDI)

    public unsafe void DrawText(ReadOnlySpan<char> text, Point location, IFont font, Color color)
    {
        if (font is not GdiFont gdiFont)
        {
            throw new ArgumentException("Font must be a GdiFont", nameof(font));
        }

        if (text.IsEmpty || color.A == 0)
        {
            return;
        }

        // For layered/bitmap targets, legacy GDI text APIs do not produce usable per-pixel alpha.
        // Use coverage-based rendering and synthesize premultiplied alpha for smooth edges.
        if (_bitmapTarget != null)
        {
            var sizeDip = MeasureText(text, gdiFont);
            int w = Math.Max(1, QuantizeLengthPx(sizeDip.Width));
            int h = Math.Max(1, QuantizeLengthPx(sizeDip.Height));
            var pt = ToDevicePoint(location);
            var r = RECT.FromLTRB(pt.x, pt.y, pt.x + w, pt.y + h);
            uint format = GdiConstants.DT_NOPREFIX | GdiConstants.DT_LEFT | GdiConstants.DT_TOP | GdiConstants.DT_SINGLELINE;
            PerPixelAlphaTextRenderer.DrawText(Hdc, _bitmapTarget, _surfacePool, text, r, gdiFont, color, format);
            return;
        }

        var oldFont = Gdi32.SelectObject(Hdc, gdiFont.GetHandle(GdiFontRenderMode.Default));
        var oldColor = Gdi32.SetTextColor(Hdc, color.ToCOLORREF());

        try
        {
            var pt = ToDevicePoint(location);
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

        if (text.IsEmpty || color.A == 0)
        {
            return;
        }

        if (_bitmapTarget != null)
        {
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

            PerPixelAlphaTextRenderer.DrawText(
                Hdc,
                _bitmapTarget,
                _surfacePool,
                text,
                r,
                gdiFont,
                color,
                format,
                yOffsetPx,
                textHeightPx);
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
        return ToDeviceRect(bounds);
        }

        var tl = ToDevicePoint(bounds.TopLeft);
        int w = QuantizeLengthPx(bounds.Width);
        int h = QuantizeLengthPx(bounds.Height);
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
                ? new RECT(0, 0, QuantizeLengthPx(1_000_000), 0)
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

            var rect = new RECT(0, 0, QuantizeLengthPx(maxWidth), 0);

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

    #region Image Rendering (GDI)

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

        var destPx = ToDeviceRect(destRect);
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

    private bool EnsureGraphics()
    {
        if (_graphics != 0)
        {
            return true;
        }

        if (GdiPlusInterop.GdipCreateFromHDC(Hdc, out _graphics) != 0 || _graphics == 0)
        {
            return false;
        }

        GdiPlusInterop.GdipSetSmoothingMode(_graphics, GdiPlusInterop.SmoothingMode.AntiAlias);
        GdiPlusInterop.GdipSetPixelOffsetMode(_graphics, GdiPlusInterop.PixelOffsetMode.Half);
        GdiPlusInterop.GdipSetCompositingMode(_graphics, GdiPlusInterop.CompositingMode.SourceOver);
        GdiPlusInterop.GdipSetCompositingQuality(_graphics, GdiPlusInterop.CompositingQuality.HighQuality);

        return true;
    }

    private static uint ToArgb(Color color)
        => (uint)(color.A << 24 | color.R << 16 | color.G << 8 | color.B);

    private static void AddRoundedRectPathI(nint path, int x, int y, int width, int height, int ellipseW, int ellipseH)
    {
        int w = Math.Max(0, width);
        int h = Math.Max(0, height);
        if (w == 0 || h == 0)
        {
            return;
        }

        int ew = Math.Min(ellipseW, w);
        int eh = Math.Min(ellipseH, h);

        int right = x + w;
        int bottom = y + h;

        GdiPlusInterop.GdipAddPathArcI(path, x, y, ew, eh, 180, 90);
        GdiPlusInterop.GdipAddPathArcI(path, right - ew, y, ew, eh, 270, 90);
        GdiPlusInterop.GdipAddPathArcI(path, right - ew, bottom - eh, ew, eh, 0, 90);
        GdiPlusInterop.GdipAddPathArcI(path, x, bottom - eh, ew, eh, 90, 90);
    }

    private POINT ToDevicePoint(Point pt)
    {
        var (x, y) = RenderingUtil.ToDevicePoint(pt, _translateX, _translateY, _dpiScale);
        return new POINT(x, y);
    }

    private RECT ToDeviceRect(Rect rect)
    {
        var (left, top, right, bottom) = RenderingUtil.ToDeviceRect(rect, _translateX, _translateY, _dpiScale);
        return new RECT(left, top, right, bottom);
    }

    private int QuantizeLengthPx(double lengthDip)
    {
        if (lengthDip <= 0 || double.IsNaN(lengthDip) || double.IsInfinity(lengthDip))
        {
            return 0;
        }

        return LayoutRounding.RoundToPixelInt(lengthDip, _dpiScale);
    }

    private (double x, double y) ToDeviceCoords(double x, double y)
        => ((x + _translateX) * _dpiScale, (y + _translateY) * _dpiScale);

    private double ToDevicePx(double logicalValue) => logicalValue * _dpiScale;

    private readonly struct GraphicsStateSnapshot
    {
        public required uint GdiPlusState { get; init; }
        public required double TranslateX { get; init; }
        public required double TranslateY { get; init; }
    }
}
