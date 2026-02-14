using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Rendering.Gdi.Core;

namespace Aprillz.MewUI.Rendering.Gdi;

internal static class PerPixelAlphaTextRenderer
{
    private const byte OpaqueAlphaThreshold = 250;

    public static unsafe void DrawText(
        nint hdc,
        GdiBitmapRenderTarget bitmapTarget,
        AaSurfacePool surfacePool,
        ReadOnlySpan<char> text,
        RECT targetRect,
        GdiFont font,
        Color color,
        uint format)
    {
        int width = targetRect.Width;
        int height = targetRect.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        if (IsOpaqueUnderText(bitmapTarget, targetRect))
        {
            DrawTextDirect(hdc, text, targetRect, font.GetHandle(GdiFontUse.PerPixelAlpha), color, format);
            return;
        }

        if (width > GdiRenderingConstants.MaxAaSurfaceSize || height > GdiRenderingConstants.MaxAaSurfaceSize)
        {
            DrawTextDirect(hdc, text, targetRect, font.GetHandle(GdiFontUse.PerPixelAlpha), color, format);
            return;
        }

        var surface = surfacePool.Rent(hdc, width, height);
        if (!surface.IsValid)
        {
            surfacePool.Return(surface);
            return;
        }

        try
        {
            surface.Clear();

            var oldFont = Gdi32.SelectObject(surface.MemDc, font.GetHandle(GdiFontUse.PerPixelAlpha));
            var oldColor = Gdi32.SetTextColor(surface.MemDc, 0x00FFFFFF);
            int oldBkMode = Gdi32.SetBkMode(surface.MemDc, GdiConstants.TRANSPARENT);

            try
            {
                var localRect = RECT.FromLTRB(0, 0, width, height);
                fixed (char* pText = text)
                {
                    Gdi32.DrawText(surface.MemDc, pText, text.Length, ref localRect, format);
                }
            }
            finally
            {
                Gdi32.SetBkMode(surface.MemDc, oldBkMode);
                Gdi32.SetTextColor(surface.MemDc, oldColor);
                Gdi32.SelectObject(surface.MemDc, oldFont);
            }

            byte aColor = color.A;
            for (int y = 0; y < height; y++)
            {
                byte* row = surface.GetRowPointer(y);
                if (row == null)
                {
                    continue;
                }

                for (int x = 0; x < width; x++)
                {
                    int i = x * 4;
                    byte b = row[i + 0];
                    byte g = row[i + 1];
                    byte r = row[i + 2];
                    byte coverage = b;
                    if (g > coverage) coverage = g;
                    if (r > coverage) coverage = r;

                    if (coverage == 0 || aColor == 0)
                    {
                        row[i + 0] = 0;
                        row[i + 1] = 0;
                        row[i + 2] = 0;
                        row[i + 3] = 0;
                        continue;
                    }

                    // GDI grayscale coverage is gamma-encoded; apply a simple curve to avoid overly bold edges.
                    coverage = (byte)((coverage * coverage + 127) / 255);
                    byte a = (byte)((coverage * aColor + 127) / 255);
                    row[i + 0] = (byte)((color.B * a + 127) / 255);
                    row[i + 1] = (byte)((color.G * a + 127) / 255);
                    row[i + 2] = (byte)((color.R * a + 127) / 255);
                    row[i + 3] = a;
                }
            }

            surface.AlphaBlendTo(hdc, targetRect.left, targetRect.top, width, height, 0, 0);
        }
        finally
        {
            surfacePool.Return(surface);
        }
    }

    private static bool IsOpaqueUnderText(GdiBitmapRenderTarget bitmapTarget, RECT r)
    {
        var span = bitmapTarget.GetPixelSpan();
        int pw = bitmapTarget.PixelWidth;
        int ph = bitmapTarget.PixelHeight;
        if (pw <= 0 || ph <= 0 || span.Length < pw * ph * 4)
        {
            return false;
        }

        int left = Math.Clamp(r.left, 0, pw - 1);
        int right = Math.Clamp(r.right - 1, 0, pw - 1);
        int top = Math.Clamp(r.top, 0, ph - 1);
        int bottom = Math.Clamp(r.bottom - 1, 0, ph - 1);
        if (right < left || bottom < top)
        {
            return false;
        }

        int midX = left + (right - left) / 2;
        int midY = top + (bottom - top) / 2;
        Span<int> xs = stackalloc int[] { left, midX, right };
        Span<int> ys = stackalloc int[] { top, midY, bottom };

        foreach (int y in ys)
        {
            int row = y * pw * 4;
            foreach (int x in xs)
            {
                int idx = row + x * 4;
                byte b = span[idx + 0];
                byte g = span[idx + 1];
                byte red = span[idx + 2];
                byte a = span[idx + 3];

                bool opaque = a >= OpaqueAlphaThreshold || (a == 0 && (red | g | b) != 0);
                if (!opaque)
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static unsafe void DrawTextDirect(nint hdc, ReadOnlySpan<char> text, RECT rect, nint fontHandle, Color color, uint format)
    {
        var oldFont = Gdi32.SelectObject(hdc, fontHandle);
        var oldColor = Gdi32.SetTextColor(hdc, color.ToCOLORREF());
        try
        {
            fixed (char* pText = text)
            {
                var r = rect;
                Gdi32.DrawText(hdc, pText, text.Length, ref r, format);
            }
        }
        finally
        {
            Gdi32.SetTextColor(hdc, oldColor);
            Gdi32.SelectObject(hdc, oldFont);
        }
    }
}
