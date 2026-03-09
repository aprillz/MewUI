using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Rendering.FreeType;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed partial class OpenGLGraphicsContext
{
    static partial void TryGetInitialViewportSizePx(nint hwnd, nint hdc, double dpiScale, ref bool handled, ref int widthPx, ref int heightPx)
    {
        if (handled)
        {
            return;
        }

        // Linux/X11: hdc is Display*, hwnd is Window.
        if (X11.XGetWindowAttributes(hdc, hwnd, out var attrs) != 0)
        {
            widthPx = Math.Max(1, attrs.width);
            heightPx = Math.Max(1, attrs.height);
        }
        else
        {
            widthPx = 1;
            heightPx = 1;
        }

        handled = true;
    }

    static partial void TryMeasureTextBitmapSizeForPointDraw(ReadOnlySpan<char> text, IFont font, double dpiScale, ref bool handled, ref int widthPx, ref int heightPx)
    {
        if (handled)
        {
            return;
        }

        if (font is FreeTypeFont ftFont)
        {
            var px = FreeTypeText.Measure(text, ftFont);
            widthPx = Math.Max(1, (int)Math.Ceiling(px.Width));
            heightPx = Math.Max(1, (int)Math.Ceiling(px.Height));
            handled = true;
        }
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

        if (font is not FreeTypeFont ftFont)
        {
            return;
        }

        var key = new OpenGLTextCacheKey(new TextCacheKey(
            string.GetHashCode(text),
            0,
            ftFont.FontPath,
            ftFont.PixelHeight,
            color.ToArgb(),
            widthPx,
            heightPx,
            (int)horizontalAlignment,
            (int)verticalAlignment,
            (int)wrapping,
            (int)trimming));

        if (!_resources.TextCache.TryGet(_resources.SupportsBgra, _hdc, key, out var texture))
        {
            var bmp = FreeTypeText.Rasterize(text, ftFont, widthPx, heightPx, color, horizontalAlignment, verticalAlignment, wrapping, trimming);
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

        if (font is FreeTypeFont ftFont)
        {
            int maxWidthPx = maxWidthDip <= 0
                ? 0
                : Math.Max(1, LayoutRounding.CeilToPixelInt(maxWidthDip, DpiScale));

            var px = FreeTypeText.Measure(text, ftFont, maxWidthPx, wrapping);
            result = new Size(px.Width / DpiScale, px.Height / DpiScale);
            handled = true;
            return;
        }

        using var fallback = new OpenGLMeasurementContext((uint)Math.Round(DpiScale * 96.0));
        result = wrapping == TextWrapping.Wrap && maxWidthDip > 0
            ? fallback.MeasureText(text, font, maxWidthDip)
            : fallback.MeasureText(text, font);
        handled = true;
    }
}
