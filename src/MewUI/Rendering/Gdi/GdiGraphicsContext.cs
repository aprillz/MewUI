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
    private readonly nint _hwnd;
    private readonly bool _ownsDc;
    private readonly GdiCurveQuality _curveQuality;
    private readonly ImageScaleQuality _imageScaleQuality;
    private readonly int _supersampleFactor;

    private readonly GdiStateManager _stateManager;
    private readonly GdiPrimitiveRenderer _primitiveRenderer;
    private readonly HybridShapeRenderer? _shapeRenderer;
    private readonly AaSurfacePool _surfacePool;

    private bool _disposed;

    public double DpiScale => _stateManager.DpiScale;

    internal nint Hdc { get; }

    public GdiGraphicsContext(
        nint hwnd,
        nint hdc,
        double dpiScale,
        GdiCurveQuality curveQuality,
        ImageScaleQuality imageScaleQuality,
        bool ownsDc = false)
    {
        _hwnd = hwnd;
        Hdc = hdc;
        _ownsDc = ownsDc;
        _curveQuality = curveQuality;
        _imageScaleQuality = imageScaleQuality;
        _supersampleFactor = (int)curveQuality switch { 2 => 2, 3 => 3, _ => 1 };

        _stateManager = new GdiStateManager(hdc, dpiScale);
        _primitiveRenderer = new GdiPrimitiveRenderer(hdc, _stateManager);
        _surfacePool = new AaSurfacePool();

        if (curveQuality != GdiCurveQuality.Fast)
        {
            _shapeRenderer = new HybridShapeRenderer(_surfacePool, _supersampleFactor);
        }

        // Set default modes
        Gdi32.SetBkMode(Hdc, GdiConstants.TRANSPARENT);
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_ownsDc && Hdc != 0)
            {
                User32.ReleaseDC(_hwnd, Hdc);
            }

            _primitiveRenderer.Dispose();
            _surfacePool.Dispose();

            _disposed = true;
        }
    }

    #region State Management

    public void Save() => _stateManager.Save();

    public void Restore() => _stateManager.Restore();

    public void SetClip(Rect rect) => _stateManager.SetClip(rect);

    public void Translate(double dx, double dy) => _stateManager.Translate(dx, dy);

    #endregion

    #region Drawing Primitives

    public void Clear(Color color) => _primitiveRenderer.Clear(_hwnd, color);

    public void DrawLine(Point start, Point end, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0)
        {
            return;
        }

        // Check if line is axis-aligned (no AA needed)
        var (ax, ay) = _stateManager.ToDeviceCoords(start.X, start.Y);
        var (bx, by) = _stateManager.ToDeviceCoords(end.X, end.Y);

        double dx = bx - ax;
        double dy = by - ay;

        bool isAxisAligned = Math.Abs(dx) < GdiRenderingConstants.Epsilon || Math.Abs(dy) < GdiRenderingConstants.Epsilon;

        if (isAxisAligned || _shapeRenderer == null)
        {
            _primitiveRenderer.DrawLine(start, end, color, thickness);
            return;
        }

        // Use AA renderer for non-axis-aligned lines
        float strokePx = (float)_stateManager.ToDevicePx(thickness);
        _shapeRenderer.DrawLine(
            Hdc,
            (float)ax, (float)ay,
            (float)bx, (float)by,
            strokePx,
            color.B, color.G, color.R, color.A);
    }

    public void DrawRectangle(Rect rect, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0)
        {
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

        _primitiveRenderer.FillRectangle(rect, color);
    }

    public void DrawRoundedRectangle(Rect rect, double radiusX, double radiusY, Color color, double thickness = 1)
    {
        if (color.A == 0 || thickness <= 0)
        {
            return;
        }

        if (_shapeRenderer == null || _curveQuality == GdiCurveQuality.Fast)
        {
            _primitiveRenderer.DrawRoundedRectangle(rect, radiusX, radiusY, color, thickness);
            return;
        }

        var dst = _stateManager.ToDeviceRect(rect);
        if (dst.Width <= 0 || dst.Height <= 0)
        {
            return;
        }

        float rx = (float)_stateManager.ToDevicePx(radiusX);
        float ry = (float)_stateManager.ToDevicePx(radiusY);
        float strokePx = (float)_stateManager.ToDevicePx(thickness);

        _shapeRenderer.DrawRoundedRectangle(
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

        if (_shapeRenderer == null || _curveQuality == GdiCurveQuality.Fast)
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

        _shapeRenderer.FillRoundedRectangle(
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

        if (_shapeRenderer == null || _curveQuality == GdiCurveQuality.Fast)
        {
            _primitiveRenderer.DrawEllipse(bounds, color, thickness);
            return;
        }

        var dst = _stateManager.ToDeviceRect(bounds);
        if (dst.Width <= 0 || dst.Height <= 0)
        {
            return;
        }

        float strokePx = (float)_stateManager.ToDevicePx(thickness);

        _shapeRenderer.DrawEllipse(
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

        if (_shapeRenderer == null || _curveQuality == GdiCurveQuality.Fast)
        {
            _primitiveRenderer.FillEllipse(bounds, color);
            return;
        }

        var dst = _stateManager.ToDeviceRect(bounds);
        if (dst.Width <= 0 || dst.Height <= 0)
        {
            return;
        }

        _shapeRenderer.FillEllipse(
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

        var oldFont = Gdi32.SelectObject(Hdc, gdiFont.Handle);
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

        var oldFont = Gdi32.SelectObject(Hdc, gdiFont.Handle);
        var oldColor = Gdi32.SetTextColor(Hdc, color.ToCOLORREF());

        try
        {
            var r = _stateManager.ToDeviceRect(bounds);
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

            fixed (char* pText = text)
            {
                Gdi32.DrawText(Hdc, pText, text.Length, ref r, format);
            }
        }
        finally
        {
            Gdi32.SetTextColor(Hdc, oldColor);
            Gdi32.SelectObject(Hdc, oldFont);
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

            return new Size(rect.Width / DpiScale, rect.Height / DpiScale);
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

            return new Size(rect.Width / DpiScale, rect.Height / DpiScale);
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
        var destPx = _stateManager.ToDeviceRect(destRect);
        if (destPx.Width <= 0 || destPx.Height <= 0)
        {
            return;
        }

        var memDc = Gdi32.CreateCompatibleDC(Hdc);
        var oldBitmap = Gdi32.SelectObject(memDc, gdiImage.Handle);

        int mode = _imageScaleQuality == ImageScaleQuality.HighQuality
            ? GdiConstants.HALFTONE
            : GdiConstants.COLORONCOLOR;

        int oldStretchMode = Gdi32.SetStretchBltMode(Hdc, mode);
        bool hasBrushOrg = mode == GdiConstants.HALFTONE;
        var oldBrushOrg = default(POINT);

        if (hasBrushOrg)
        {
            Gdi32.SetBrushOrgEx(Hdc, 0, 0, out oldBrushOrg);
        }

        try
        {
            int srcX = (int)sourceRect.X;
            int srcY = (int)sourceRect.Y;
            int srcW = (int)sourceRect.Width;
            int srcH = (int)sourceRect.Height;

            // Try to use cached scaled bitmap for high-quality downscaling
            if (_imageScaleQuality == ImageScaleQuality.HighQuality &&
                IsNearInt(sourceRect.X) && IsNearInt(sourceRect.Y) &&
                IsNearInt(sourceRect.Width) && IsNearInt(sourceRect.Height) &&
                gdiImage.TryGetOrCreateScaledBitmap(srcX, srcY, srcW, srcH, destPx.Width, destPx.Height, out var scaledBmp))
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

            // Standard alpha blend
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

            if (hasBrushOrg)
            {
                Gdi32.SetBrushOrgEx(Hdc, oldBrushOrg.x, oldBrushOrg.y, out _);
            }

            Gdi32.SelectObject(memDc, oldBitmap);
            Gdi32.DeleteDC(memDc);
        }
    }

    private static bool IsNearInt(double value) => Math.Abs(value - Math.Round(value)) <= 0.0001;

    #endregion
}
