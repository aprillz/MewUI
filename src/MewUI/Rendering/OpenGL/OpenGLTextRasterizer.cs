using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Rendering.Gdi;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal static class OpenGLTextRasterizer
{
    private static readonly byte[] EmptyPixel = new byte[4];

    public unsafe static OpenGLTextBitmap Rasterize(
        nint hdcWindow,
        GdiFont font,
        ReadOnlySpan<char> text,
        int widthPx,
        int heightPx,
        Color color,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping)
    {
        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        nint memDc = Gdi32.CreateCompatibleDC(hdcWindow);
        if (memDc == 0)
        {
            return new OpenGLTextBitmap(1, 1, EmptyPixel);
        }

        try
        {
            var bmi = BITMAPINFO.Create32bpp(widthPx, heightPx);
            nint bits;
            nint dib = Gdi32.CreateDIBSection(memDc, ref bmi, GdiConstants.DIB_RGB_COLORS, out bits, 0, 0);
            if (dib == 0 || bits == 0)
            {
                return new OpenGLTextBitmap(1, 1, EmptyPixel);
            }

            nint oldBmp = Gdi32.SelectObject(memDc, dib);
            nint oldFont = Gdi32.SelectObject(memDc, font.Handle);
            try
            {
                // Opaque black background for coverage extraction.
                var rect = new RECT(0, 0, widthPx, heightPx);
                var brush = Gdi32.CreateSolidBrush(0x000000);
                try
                {
                    Gdi32.FillRect(memDc, ref rect, brush);
                }
                finally
                {
                    Gdi32.DeleteObject(brush);
                }

                Gdi32.SetBkMode(memDc, GdiConstants.OPAQUE);
                Gdi32.SetBkColor(memDc, 0x000000);
                Gdi32.SetTextColor(memDc, 0xFFFFFF);

                uint format = GdiConstants.DT_NOPREFIX;
                format |= wrapping == TextWrapping.NoWrap ? GdiConstants.DT_SINGLELINE : GdiConstants.DT_WORDBREAK;

                format |= horizontalAlignment switch
                {
                    TextAlignment.Center => GdiConstants.DT_CENTER,
                    TextAlignment.Right => GdiConstants.DT_RIGHT,
                    _ => GdiConstants.DT_LEFT
                };

                format |= verticalAlignment switch
                {
                    TextAlignment.Center => GdiConstants.DT_VCENTER,
                    TextAlignment.Bottom => GdiConstants.DT_BOTTOM,
                    _ => GdiConstants.DT_TOP
                };

                fixed (char* pText = text)
                {
                    Gdi32.DrawText(memDc, pText, text.Length, ref rect, format);
                }

                int bytes = widthPx * heightPx * 4;
                var bgra = new byte[bytes];
                Marshal.Copy(bits, bgra, 0, bytes);

                // Convert black background + white text into alpha, and apply requested color.
                ApplyCoverageToColor(bgra, color);
                return new OpenGLTextBitmap(widthPx, heightPx, bgra);
            }
            finally
            {
                if (oldFont != 0)
                {
                    Gdi32.SelectObject(memDc, oldFont);
                }

                if (oldBmp != 0)
                {
                    Gdi32.SelectObject(memDc, oldBmp);
                }

                Gdi32.DeleteObject(dib);
            }
        }
        finally
        {
            Gdi32.DeleteDC(memDc);
        }
    }

    private static void ApplyCoverageToColor(byte[] bgra, Color color)
    {
        byte r = color.R;
        byte g = color.G;
        byte b = color.B;
        byte a0 = color.A;

        for (int i = 0; i < bgra.Length; i += 4)
        {
            byte bb = bgra[i];
            byte gg = bgra[i + 1];
            byte rr = bgra[i + 2];

            // ClearType can output colored subpixels; approximate coverage using luminance to avoid "bold" look.
            byte coverage = (byte)((rr * 30 + gg * 59 + bb * 11) / 100);

            if (coverage == 0 || a0 == 0)
            {
                bgra[i] = 0;
                bgra[i + 1] = 0;
                bgra[i + 2] = 0;
                bgra[i + 3] = 0;
                continue;
            }

            int a = coverage * a0 / 255;
            bgra[i] = b;
            bgra[i + 1] = g;
            bgra[i + 2] = r;
            bgra[i + 3] = (byte)a;
        }
    }
}
