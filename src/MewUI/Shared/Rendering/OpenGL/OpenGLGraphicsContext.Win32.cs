using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.Gdi;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed partial class OpenGLGraphicsContext
{
    static partial void TryGetInitialViewportSizePx(nint hwnd, nint hdc, double dpiScale, ref bool handled, ref int widthPx, ref int heightPx)
    {
        if (handled)
        {
            return;
        }

        User32.GetClientRect(hwnd, out var client);
        widthPx = Math.Max(1, client.Width);
        heightPx = Math.Max(1, client.Height);
        handled = true;
    }

    static partial void TryMeasureTextBitmapSizeForPointDraw(ReadOnlySpan<char> text, IFont font, double dpiScale, ref bool handled, ref int widthPx, ref int heightPx)
    {
        // No special-case sizing beyond MeasureText() on Win32.
    }

    partial void TryDrawTextNative(
        ReadOnlySpan<char> text,
        Native.Structs.RECT boundsPx,
        IFont font,
        Color color,
        int widthPx,
        int heightPx,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping,
        TextTrimming trimming,
        ref bool handled)
    {
        if (handled)
        {
            return;
        }

        if (font is not GdiFont gdiFont)
        {
            return;
        }

        var key = new OpenGLTextCacheKey(new TextCacheKey(
            string.GetHashCode(text),
            gdiFont.Handle,
            string.Empty,
            0,
            color.ToArgb(),
            widthPx,
            heightPx,
            (int)horizontalAlignment,
            (int)verticalAlignment,
            (int)wrapping,
            (int)trimming));

        if (!_resources.TextCache.TryGet(_resources.SupportsBgra, _hdc, key, out var texture))
        {
            var bmp = OpenGLTextRasterizer.Rasterize(_hdc, gdiFont, text, widthPx, heightPx, color, horizontalAlignment, verticalAlignment, wrapping, trimming);
            texture = _resources.TextCache.CreateTexture(_resources.SupportsBgra, _hdc, key, ref bmp);
        }

        DrawTexturedQuad(boundsPx, ref texture);
        handled = true;
    }

    partial void TryMeasureTextNative(
        ReadOnlySpan<char> text,
        IFont font,
        double maxWidthDip,
        TextWrapping wrapping,
        ref bool handled,
        ref Size result)
    {
        if (handled)
        {
            return;
        }

        uint dpi = (uint)Math.Round(DpiScale * 96.0);
        using var measure = new GdiMeasurementContext(User32.GetDC(0), dpi);
        result = wrapping == TextWrapping.Wrap && maxWidthDip > 0
            ? measure.MeasureText(text, font, maxWidthDip)
            : measure.MeasureText(text, font);
        handled = true;
    }
}
