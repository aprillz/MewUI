using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed partial class OpenGLGraphicsContext
{
    static partial void TryGetInitialViewportSizePx(nint hwnd, nint hdc, double dpiScale, ref bool handled, ref int widthPx, ref int heightPx)
    {
        if (handled)
        {
            return;
        }

        // macOS: hwnd is NSView*, size in points -> pixels via dpiScale.
        var size = NsOpenGLWindowResources.GetViewSizePx(hwnd, dpiScale);
        widthPx = size.WidthPx;
        heightPx = size.HeightPx;
        handled = true;
    }

    static partial void TryMeasureTextBitmapSizeForPointDraw(ReadOnlySpan<char> text, IFont font, double dpiScale, ref bool handled, ref int widthPx, ref int heightPx)
    {
        // No special-case sizing beyond MeasureText() on macOS.
    }

    partial void TryDrawTextNative(
        ReadOnlySpan<char> text,
        RECT boundsPx,
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

        if (font is not CoreText.CoreTextFont ctFont)
        {
            return;
        }

        uint dpi = (uint)Math.Round(DpiScale * 96.0);
        var fontRef = ctFont.GetFontRef(dpi);
        var key = new OpenGLTextCacheKey(new TextCacheKey(
            string.GetHashCode(text),
            fontRef,
            ctFont.Family,
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
            int wrapWidthPx = wrapping == TextWrapping.Wrap ? boundsPx.Width : 0;
            var bmp = CoreText.CoreTextText.Rasterize(ctFont, text, widthPx, heightPx, dpi, color, horizontalAlignment, verticalAlignment, wrapping, wrapWidthPx, trimming);
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

        using var fallback = new OpenGLMeasurementContext((uint)Math.Round(DpiScale * 96.0));
        result = wrapping == TextWrapping.Wrap && maxWidthDip > 0
            ? fallback.MeasureText(text, font, maxWidthDip)
            : fallback.MeasureText(text, font);
        handled = true;
    }
}
