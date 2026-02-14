using System.Runtime.InteropServices;

using Aprillz.MewUI.Native.FreeType;
using Aprillz.MewUI.Rendering;

using FT = Aprillz.MewUI.Native.FreeType.FreeType;

namespace Aprillz.MewUI.Rendering.FreeType;

internal static unsafe class FreeTypeText
{
    private static readonly byte[] EmptyPixel = new byte[4];

    public static Size Measure(ReadOnlySpan<char> text, FreeTypeFont font)
    {
        return Measure(text, font, maxWidthPx: 0, TextWrapping.NoWrap);
    }

    public static Size Measure(ReadOnlySpan<char> text, FreeTypeFont font, int maxWidthPx, TextWrapping wrapping)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        var face = FreeTypeFaceCache.Instance.Get(font.FontPath, font.PixelHeight);
        int lineHeightPx = Math.Max(1, (int)Math.Round(font.PixelHeight * 1.25));

        int maxLineWidth = 0;
        int lines = 0;

        TextLayout.EnumerateLines(text, maxWidthPx, wrapping, span => MeasureRunWidthPx(span, face), line =>
        {
            int width = (int)Math.Ceiling(line.Width);
            if (width > maxLineWidth)
            {
                maxLineWidth = width;
            }
            lines++;
        });

        if (lines <= 0)
        {
            lines = 1;
        }

        maxLineWidth = (int)Math.Round(TextMeasurePolicy.ApplyWidthPadding(maxLineWidth));

        int heightPx = lines * lineHeightPx;
        return new Size(maxLineWidth, heightPx);
    }

    public static TextBitmap Rasterize(
        ReadOnlySpan<char> text,
        FreeTypeFont font,
        int widthPx,
        int heightPx,
        Color color,
        TextAlignment hAlign,
        TextAlignment vAlign,
        TextWrapping wrapping)
    {
        if (text.IsEmpty)
        {
            return new TextBitmap(1, 1, EmptyPixel);
        }

        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        // Use cached face for performance.
        var face = FreeTypeFaceCache.Instance.Get(font.FontPath, font.PixelHeight);

        var buffer = new byte[widthPx * heightPx * 4];

        int lineHeightPx = Math.Max(1, (int)Math.Round(font.PixelHeight * 1.25));

        var lines = new List<TextLayout.LineSegment>();
        int maxLineWidth = 0;
        TextLayout.EnumerateLines(text, wrapping == TextWrapping.Wrap ? widthPx : 0, wrapping, span => MeasureRunWidthPx(span, face), line =>
        {
            int width = (int)Math.Ceiling(line.Width);
            if (width > maxLineWidth)
            {
                maxLineWidth = width;
            }
            lines.Add(new TextLayout.LineSegment(line.Start, line.Length, width));
        });

        int contentW = Math.Min(widthPx, (int)Math.Round(TextMeasurePolicy.ApplyWidthPadding(maxLineWidth)));
        int contentH = Math.Min(heightPx, lines.Count * lineHeightPx);

        int startX = hAlign switch
        {
            TextAlignment.Center => (widthPx - contentW) / 2,
            TextAlignment.Right => widthPx - contentW,
            _ => 0
        };

        int startY = vAlign switch
        {
            TextAlignment.Center => (heightPx - contentH) / 2,
            TextAlignment.Bottom => heightPx - contentH,
            _ => 0
        };

        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            if (line.Length <= 0)
            {
                continue;
            }

            int lineWidthPx = line.Width > 0 ? (int)Math.Ceiling(TextMeasurePolicy.ApplyWidthPadding(line.Width)) : 0;
            int lineX = hAlign switch
            {
                TextAlignment.Center => startX + (contentW - lineWidthPx) / 2,
                TextAlignment.Right => startX + (contentW - lineWidthPx),
                _ => startX
            };

            int penX = lineX;
            int penY = startY + (lineIndex * lineHeightPx);
            uint prevGlyph = 0;

            for (int i = 0; i < line.Length; i++)
            {
                char ch = text[line.Start + i];
                if (ch == '\r' || ch == '\n')
                {
                    continue;
                }

                uint code = ch;
                uint glyph = face.GetGlyphIndex(code);
                penX += GetKerningPx(face, prevGlyph, glyph);
                prevGlyph = glyph;

                lock (face.SyncRoot)
                {
                    // Prefer LIGHT target for crisper UI text on typical Linux setups.
                    // Avoid FORCE_AUTOHINT by default; it can make some fonts look blurrier/thicker than expected.
                    int flags = FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_TARGET_LIGHT;
                    if (FT.FT_Load_Char(face.Face, code, flags) != 0)
                    {
                        goto Advance;
                    }

                    var slotPtr = face.GetGlyphSlotPointer();
                    if (slotPtr != 0 && FT.FT_Get_Glyph(slotPtr, out var glyphPtr) == 0 && glyphPtr != 0)
                    {
                        nint bmpGlyphPtr = 0;
                        try
                        {
                            bmpGlyphPtr = glyphPtr;
                            int err = FT.FT_Glyph_To_Bitmap(ref bmpGlyphPtr, FreeTypeRenderMode.FT_RENDER_MODE_NORMAL, origin: 0, destroy: false);
                            if (err == 0 && bmpGlyphPtr != 0)
                            {
                                var bmpGlyph = Marshal.PtrToStructure<FT_BitmapGlyphRec>(bmpGlyphPtr);

                                int glyphW = (int)bmpGlyph.bitmap.width;
                                int glyphH = (int)bmpGlyph.bitmap.rows;
                                int left = bmpGlyph.left;
                                int top = bmpGlyph.top;

                                // baseline at (penX, penY + ascent). Approx using pixel height.
                                int baseY = penY + font.PixelHeight;

                                int dstX0 = penX + left;
                                int dstY0 = baseY - top;

                                BlitGlyph(bmpGlyph.bitmap, glyphW, glyphH, dstX0, dstY0, buffer, widthPx, heightPx, color);
                            }
                        }
                        finally
                        {
                            // When destroy=false, both the original and the (optional) bitmap glyph may need cleanup.
                            // Some FreeType builds may convert in-place, so guard against double-free.
                            if (bmpGlyphPtr != 0 && bmpGlyphPtr != glyphPtr)
                            {
                                FT.FT_Done_Glyph(bmpGlyphPtr);
                            }

                            FT.FT_Done_Glyph(glyphPtr);
                        }
                    }
                }

                Advance:
                penX += face.GetAdvancePx(code);
            }
        }

        return new TextBitmap(widthPx, heightPx, buffer);
    }

    private static int MeasureRunWidthPx(ReadOnlySpan<char> text, FreeTypeFaceCache.FaceEntry face)
    {
        if (text.IsEmpty)
        {
            return 0;
        }

        int width = 0;
        uint prevGlyph = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\r' || ch == '\n')
            {
                continue;
            }

            uint code = ch;
            uint glyph = face.GetGlyphIndex(code);
            width += GetKerningPx(face, prevGlyph, glyph);
            width += face.GetAdvancePx(code);
            prevGlyph = glyph;
        }

        return width;
    }

    private static int GetKerningPx(FreeTypeFaceCache.FaceEntry face, uint leftGlyph, uint rightGlyph)
    {
        if (leftGlyph == 0 || rightGlyph == 0)
        {
            return 0;
        }

        lock (face.SyncRoot)
        {
            int err = FT.FT_Get_Kerning(face.Face, leftGlyph, rightGlyph, FreeTypeKerning.FT_KERNING_DEFAULT, out var v);
            if (err != 0)
            {
                return 0;
            }

            return (int)((long)v.x >> 6);
        }
    }

    private static void BlitGlyph(
        FT_Bitmap bitmap,
        int glyphW,
        int glyphH,
        int dstX0,
        int dstY0,
        byte[] dstBgra,
        int dstW,
        int dstH,
        Color color)
    {
        if (bitmap.buffer == null || glyphW <= 0 || glyphH <= 0)
        {
            return;
        }

        // We only handle grayscale bitmaps for now.
        // pixel_mode == 2 : FT_PIXEL_MODE_GRAY
        if (bitmap.pixel_mode != 2)
        {
            return;
        }

        int pitch = bitmap.pitch;
        if (pitch == 0)
        {
            return;
        }

        byte r = color.R;
        byte g = color.G;
        byte b = color.B;
        byte a0 = color.A;

        for (int y = 0; y < glyphH; y++)
        {
            int dy = dstY0 + y;
            if ((uint)dy >= (uint)dstH)
            {
                continue;
            }

            byte* srcRow = bitmap.buffer + y * pitch;

            for (int x = 0; x < glyphW; x++)
            {
                int dx = dstX0 + x;
                if ((uint)dx >= (uint)dstW)
                {
                    continue;
                }

                byte cov = srcRow[x];
                if (cov == 0)
                {
                    continue;
                }

                int a = cov * a0 / 255;
                int di = (dy * dstW + dx) * 4;

                // Alpha blend over existing.
                byte dstA = dstBgra[di + 3];
                byte dstB = dstBgra[di + 0];
                byte dstG = dstBgra[di + 1];
                byte dstR = dstBgra[di + 2];

                int outA = a + dstA * (255 - a) / 255;
                if (outA == 0)
                {
                    continue;
                }

                int outB = (b * a + dstB * dstA * (255 - a) / 255) / outA;
                int outG = (g * a + dstG * dstA * (255 - a) / 255) / outA;
                int outR = (r * a + dstR * dstA * (255 - a) / 255) / outA;

                dstBgra[di + 0] = (byte)outB;
                dstBgra[di + 1] = (byte)outG;
                dstBgra[di + 2] = (byte)outR;
                dstBgra[di + 3] = (byte)outA;
            }
        }
    }

}
