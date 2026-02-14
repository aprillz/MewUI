using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.FreeType;
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

        if (OperatingSystem.IsWindows())
        {
            // On Windows, we can get the actual client size immediately. On other platforms, we may not be able to until the first resize event.
            User32.GetClientRect(hwnd, out var client);
            widthPx = Math.Max(1, client.Width);
            heightPx = Math.Max(1, client.Height);
            handled = true;
        }
        else if (OperatingSystem.IsLinux())
        {
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
        else
        {
            throw new PlatformNotSupportedException();
        }
    }

    static partial void TryMeasureTextBitmapSizeForPointDraw(ReadOnlySpan<char> text, IFont font, double dpiScale, ref bool handled, ref int widthPx, ref int heightPx)
    {
        if (OperatingSystem.IsLinux())
        {
            if (font is FreeTypeFont ftFont)
            {
                var px = FreeTypeText.Measure(text, ftFont);
                widthPx = Math.Max(1, (int)Math.Ceiling(px.Width));
                heightPx = Math.Max(1, (int)Math.Ceiling(px.Height));
                handled = true;
            }
        }
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
        ref bool handled)
    {
        if (handled)
        {
            return;
        }

        if (OperatingSystem.IsWindows())
        {
            if (font is not GdiFont gdiFont)
            {
                return;
            }

            var key = new OpenGLTextCacheKey(string.GetHashCode(text), gdiFont.Handle, FontId: string.Empty, FontSizePx: 0, color.ToArgb(), widthPx, heightPx,
                (int)horizontalAlignment, (int)verticalAlignment, (int)wrapping);

            if (!_resources.TextCache.TryGet(_resources.SupportsBgra, _hdc, key, out var texture))
            {
                var bmp = OpenGLTextRasterizer.Rasterize(_hdc, gdiFont, text, widthPx, heightPx, color, horizontalAlignment, verticalAlignment, wrapping);
                texture = _resources.TextCache.CreateTexture(_resources.SupportsBgra, _hdc, key, ref bmp);
            }

            DrawTexturedQuad(boundsPx, ref texture);
            handled = true;
        }
        else if (OperatingSystem.IsLinux())
        {
            if (font is not FreeTypeFont ftFont)
            {
                return;
            }

            var key = new OpenGLTextCacheKey(string.GetHashCode(text), 0, ftFont.FontPath, ftFont.PixelHeight, color.ToArgb(), widthPx, heightPx,
                (int)horizontalAlignment, (int)verticalAlignment, (int)wrapping);

            if (!_resources.TextCache.TryGet(_resources.SupportsBgra, _hdc, key, out var texture))
            {
                var bmp = FreeTypeText.Rasterize(text, ftFont, widthPx, heightPx, color, horizontalAlignment, verticalAlignment, wrapping);
                texture = _resources.TextCache.CreateTexture(_resources.SupportsBgra, _hdc, key, ref bmp);
            }

            DrawTexturedQuad(boundsPx, ref texture);
            handled = true;
        }
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

        if (OperatingSystem.IsWindows())
        {
            uint dpi = (uint)Math.Round(DpiScale * 96.0);
            using var measure = new GdiMeasurementContext(User32.GetDC(0), dpi);
            result = wrapping == TextWrapping.Wrap && maxWidthDip > 0
                ? measure.MeasureText(text, font, maxWidthDip)
                : measure.MeasureText(text, font);
            handled = true;
        }
        else if (OperatingSystem.IsLinux())
        {
            if (font is FreeTypeFont ftFont)
            {
                // TODO: wrapping-aware measurement; for now ignore maxWidthDip.
                var px = FreeTypeText.Measure(text, ftFont);
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
}
