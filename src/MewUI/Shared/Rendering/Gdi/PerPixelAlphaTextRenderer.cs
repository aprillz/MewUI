using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Constants;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Rendering.Gdi.Core;

namespace Aprillz.MewUI.Rendering.Gdi;

internal static class PerPixelAlphaTextRenderer
{
    public static unsafe void DrawText(
        nint hdc,
        GdiPixelRenderSurface? pixelSurface,
        AaSurfacePool surfacePool,
        ReadOnlySpan<char> text,
        RECT targetRect,
        GdiFont font,
        Color color,
        uint format,
        int yOffsetPx = 0,
        int textHeightPx = 0,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None,
        TextAlignment hAlign = TextAlignment.Left,
        TextAlignment vAlign = TextAlignment.Top)
    {
        var surfaceRect = targetRect;
        if (pixelSurface != null)
        {
            surfaceRect.left = Math.Clamp(surfaceRect.left, 0, pixelSurface.PixelWidth);
            surfaceRect.top = Math.Clamp(surfaceRect.top, 0, pixelSurface.PixelHeight);
            surfaceRect.right = Math.Clamp(surfaceRect.right, 0, pixelSurface.PixelWidth);
            surfaceRect.bottom = Math.Clamp(surfaceRect.bottom, 0, pixelSurface.PixelHeight);
        }

        int width = surfaceRect.Width;
        int height = surfaceRect.Height;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var drawRect = pixelSurface != null
            ? RECT.FromLTRB(
                targetRect.left - surfaceRect.left,
                targetRect.top - surfaceRect.top,
                targetRect.right - surfaceRect.left,
                targetRect.bottom - surfaceRect.top)
            : targetRect;
        if (yOffsetPx != 0)
        {
            drawRect.top += yOffsetPx;
            drawRect.bottom += yOffsetPx;
        }
        if (textHeightPx > 0)
        {
            drawRect.bottom = drawRect.top + textHeightPx;
        }

        if (width > GdiRenderingConstants.MaxAaSurfaceSize || height > GdiRenderingConstants.MaxAaSurfaceSize)
        {
            DrawTextDirect(hdc, text, drawRect, font.GetHandle(GdiFontRenderMode.Coverage), color, format, wrapping, trimming, hAlign, vAlign);
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

            var oldFont = Gdi32.SelectObject(surface.MemDc, font.GetHandle(GdiFontRenderMode.Coverage));
            var oldColor = Gdi32.SetTextColor(surface.MemDc, 0x00FFFFFF);
            int oldBkMode = Gdi32.SetBkMode(surface.MemDc, GdiConstants.TRANSPARENT);

            try
            {
                var localRect = pixelSurface != null
                    ? RECT.FromLTRB(
                        targetRect.left - surfaceRect.left,
                        targetRect.top - surfaceRect.top,
                        targetRect.right - surfaceRect.left,
                        targetRect.bottom - surfaceRect.top)
                    : RECT.FromLTRB(0, 0, width, height);
                if (yOffsetPx != 0)
                {
                    localRect.top += yOffsetPx;
                    localRect.bottom += yOffsetPx;
                }
                if (textHeightPx > 0)
                {
                    localRect.bottom = localRect.top + textHeightPx;
                }

                bool drawn = false;
                if (trimming == TextTrimming.CharacterEllipsis && wrapping != TextWrapping.NoWrap)
                {
                    drawn = GdiWrappedEllipsisHelper.TryDrawWrappedWithEllipsis(
                        surface.MemDc, text, localRect, width, height, hAlign, vAlign);
                }

                if (!drawn)
                {
                    fixed (char* pText = text)
                    {
                        Gdi32.DrawText(surface.MemDc, pText, text.Length, ref localRect, format);
                    }
                }
            }
            finally
            {
                Gdi32.SetBkMode(surface.MemDc, oldBkMode);
                Gdi32.SetTextColor(surface.MemDc, oldColor);
                Gdi32.SelectObject(surface.MemDc, oldFont);
            }

            CoverageToPremultiplied(surface, width, height, color);

            if (pixelSurface != null)
            {
                CompositeToPixelSurface(surface, pixelSurface, hdc, surfaceRect.left, surfaceRect.top, width, height);
            }
            else
            {
                surface.AlphaBlendTo(hdc, surfaceRect.left, surfaceRect.top, width, height, 0, 0);
            }
        }
        finally
        {
            surfacePool.Return(surface);
        }
    }

    private static unsafe void CoverageToPremultiplied(AaSurface surface, int width, int height, Color color)
    {
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
    }

    /// <summary>
    /// Per-pixel-alpha text under a world transform (e.g. rotation) drawn into a cache surface.
    /// The direct GDI <c>DrawText</c> path used for transformed text writes no alpha, so on a
    /// transparent cache surface the glyphs end up fully transparent (the background shows through
    /// as if the text were the background colour). This renders white coverage through the same
    /// transform into a bounding-box-sized temp surface, converts it to premultiplied colour, then
    /// alpha-composites into the pixel surface. Returns false when the bounding box is too large to
    /// rent a temp surface, letting the caller fall back to its direct GDI path.
    /// </summary>
    public static unsafe bool DrawTextTransformed(
        nint hdc,
        GdiPixelRenderSurface pixelSurface,
        AaSurfacePool surfacePool,
        ReadOnlySpan<char> text,
        RECT localTextRect,
        RECT bboxDeviceRect,
        XFORM transform,
        GdiFont font,
        Color color,
        uint format,
        int yOffsetPx,
        int textHeightPx,
        TextWrapping wrapping,
        TextTrimming trimming,
        TextAlignment hAlign,
        TextAlignment vAlign)
    {
        int width = bboxDeviceRect.Width;
        int height = bboxDeviceRect.Height;
        if (width <= 0 || height <= 0)
        {
            return true;
        }

        if (width > GdiRenderingConstants.MaxAaSurfaceSize || height > GdiRenderingConstants.MaxAaSurfaceSize)
        {
            return false;
        }

        var surface = surfacePool.Rent(hdc, width, height);
        if (!surface.IsValid)
        {
            surfacePool.Return(surface);
            return false;
        }

        try
        {
            surface.Clear();

            // The temp surface's local origin is the bounding box's device-space top-left, so the
            // world transform's translation is shifted by that origin to keep the rotated glyphs
            // inside the temp surface.
            var localTransform = transform;
            localTransform.eDx -= bboxDeviceRect.left;
            localTransform.eDy -= bboxDeviceRect.top;

            int savedDc = Gdi32.SaveDC(surface.MemDc);
            Gdi32.SetGraphicsMode(surface.MemDc, GdiConstants.GM_ADVANCED);
            Gdi32.SetWorldTransform(surface.MemDc, ref localTransform);

            var oldFont = Gdi32.SelectObject(surface.MemDc, font.GetHandle(GdiFontRenderMode.Coverage));
            _ = Gdi32.SetTextColor(surface.MemDc, 0x00FFFFFF);
            _ = Gdi32.SetBkMode(surface.MemDc, GdiConstants.TRANSPARENT);

            var r = localTextRect;
            if (yOffsetPx != 0)
            {
                r.top += yOffsetPx;
                r.bottom += yOffsetPx;
            }
            if (textHeightPx > 0)
            {
                r.bottom = r.top + textHeightPx;
            }

            bool drawn = false;
            if (trimming == TextTrimming.CharacterEllipsis && wrapping != TextWrapping.NoWrap)
            {
                drawn = GdiWrappedEllipsisHelper.TryDrawWrappedWithEllipsis(
                    surface.MemDc, text, r, r.Width, r.Height, hAlign, vAlign);
            }

            if (!drawn)
            {
                fixed (char* pText = text)
                {
                    Gdi32.DrawText(surface.MemDc, pText, text.Length, ref r, format);
                }
            }

            Gdi32.SelectObject(surface.MemDc, oldFont);
            Gdi32.RestoreDC(surface.MemDc, savedDc);

            CoverageToPremultiplied(surface, width, height, color);
            CompositeToPixelSurface(surface, pixelSurface, hdc, bboxDeviceRect.left, bboxDeviceRect.top, width, height);
            return true;
        }
        finally
        {
            surfacePool.Return(surface);
        }
    }

    private static unsafe void CompositeToPixelSurface(
        AaSurface source,
        GdiPixelRenderSurface target,
        nint targetDc,
        int destX,
        int destY,
        int width,
        int height)
    {
        var targetSpan = target.GetPixelSpan();
        if (targetSpan.IsEmpty)
        {
            return;
        }

        int srcX = 0;
        int srcY = 0;
        if (destX < 0)
        {
            srcX = -destX;
            width += destX;
            destX = 0;
        }
        if (destY < 0)
        {
            srcY = -destY;
            height += destY;
            destY = 0;
        }

        width = Math.Min(width, Math.Min(source.Width - srcX, target.PixelWidth - destX));
        height = Math.Min(height, Math.Min(source.Height - srcY, target.PixelHeight - destY));
        if (width <= 0 || height <= 0)
        {
            return;
        }

        // This path writes the DIB pixels directly, bypassing GDI's clipping. Restrict the
        // destination to the active HDC clip, and retain per-pixel checks for rounded regions.
        int clipComplexity = Gdi32.GetClipBox(targetDc, out var clip);
        if (clipComplexity == 0 || clipComplexity == 1)
        {
            return;
        }

        int clippedLeft = Math.Max(destX, clip.left);
        int clippedTop = Math.Max(destY, clip.top);
        int clippedRight = Math.Min(destX + width, clip.right);
        int clippedBottom = Math.Min(destY + height, clip.bottom);
        if (clippedRight <= clippedLeft || clippedBottom <= clippedTop)
        {
            return;
        }

        srcX += clippedLeft - destX;
        srcY += clippedTop - destY;
        destX = clippedLeft;
        destY = clippedTop;
        width = clippedRight - clippedLeft;
        height = clippedBottom - clippedTop;
        bool hasComplexClip = clipComplexity == 3;

        fixed (byte* targetBase = targetSpan)
        {
            int targetStride = target.PixelWidth * 4;
            for (int y = 0; y < height; y++)
            {
                byte* src = source.GetRowPointer(srcY + y) + srcX * 4;
                byte* dst = targetBase + (destY + y) * targetStride + destX * 4;

                for (int x = 0; x < width; x++, src += 4, dst += 4)
                {
                    if (hasComplexClip && Gdi32.PtVisible(targetDc, destX + x, destY + y) == 0)
                    {
                        continue;
                    }

                    int sa = src[3];
                    if (sa == 0)
                    {
                        continue;
                    }

                    int invA = 255 - sa;
                    dst[0] = (byte)Math.Min(255, src[0] + (dst[0] * invA + 127) / 255);
                    dst[1] = (byte)Math.Min(255, src[1] + (dst[1] * invA + 127) / 255);
                    dst[2] = (byte)Math.Min(255, src[2] + (dst[2] * invA + 127) / 255);
                    dst[3] = (byte)Math.Min(255, sa + (dst[3] * invA + 127) / 255);
                }
            }
        }
    }

    private static unsafe void DrawTextDirect(
        nint hdc, ReadOnlySpan<char> text, RECT rect, nint fontHandle, Color color, uint format,
        TextWrapping wrapping = TextWrapping.NoWrap, TextTrimming trimming = TextTrimming.None,
        TextAlignment hAlign = TextAlignment.Left, TextAlignment vAlign = TextAlignment.Top)
    {
        var oldFont = Gdi32.SelectObject(hdc, fontHandle);
        var oldColor = Gdi32.SetTextColor(hdc, color.ToCOLORREF());
        try
        {
            bool drawn = false;
            if (trimming == TextTrimming.CharacterEllipsis && wrapping != TextWrapping.NoWrap)
            {
                drawn = GdiWrappedEllipsisHelper.TryDrawWrappedWithEllipsis(
                    hdc, text, rect, rect.Width, rect.Height, hAlign, vAlign);
            }

            if (!drawn)
            {
                fixed (char* pText = text)
                {
                    var r = rect;
                    Gdi32.DrawText(hdc, pText, text.Length, ref r, format);
                }
            }
        }
        finally
        {
            Gdi32.SetTextColor(hdc, oldColor);
            Gdi32.SelectObject(hdc, oldFont);
        }
    }
}
