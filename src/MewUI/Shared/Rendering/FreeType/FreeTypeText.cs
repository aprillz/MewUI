using System.Runtime.InteropServices;

using Aprillz.MewUI.Native.FreeType;

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

        var face = FreeTypeFaceCache.Instance.Get(font.FontPath, font.PixelHeight, font.Weight, font.IsItalic);
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
        TextWrapping wrapping,
        TextTrimming trimming = TextTrimming.None)
    {
        if (text.IsEmpty)
        {
            return new TextBitmap(1, 1, EmptyPixel);
        }

        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        // Use cached face for performance.
        var face = FreeTypeFaceCache.Instance.Get(font.FontPath, font.PixelHeight, font.Weight, font.IsItalic);

        var buffer = new byte[widthPx * heightPx * 4];

        int lineHeightPx = Math.Max(1, (int)Math.Round(font.PixelHeight * 1.25));

        var lines = new List<TextLayout.LineSegment>();
        int maxLineWidth = 0;
        int effectiveWrapWidth = wrapping == TextWrapping.Wrap ? widthPx : 0;
        TextLayout.EnumerateLines(text, effectiveWrapWidth, wrapping, span => MeasureRunWidthPx(span, face), line =>
        {
            int width = (int)Math.Ceiling(line.Width);
            if (width > maxLineWidth)
            {
                maxLineWidth = width;
            }
            lines.Add(new TextLayout.LineSegment(line.Start, line.Length, width));
        });

        // Post-pass: apply character-ellipsis trimming.
        var trimmedFlags = new List<bool>(lines.Count);
        if (trimming == TextTrimming.CharacterEllipsis)
        {
            if (wrapping == TextWrapping.NoWrap)
            {
                for (int i = 0; i < lines.Count; i++)
                {
                    var line = lines[i];
                    if (line.Width > widthPx && line.Length > 0)
                    {
                        var lineText = text.Slice(line.Start, line.Length);
                        var trimmed = TextLayout.TrimLineWithEllipsis(lineText, line.Start, widthPx, span => MeasureRunWidthPx(span, face));
                        lines[i] = new TextLayout.LineSegment(trimmed.Start, trimmed.Length, trimmed.Width);
                        trimmedFlags.Add(true);
                    }
                    else
                    {
                        trimmedFlags.Add(false);
                    }
                }
            }
            else
            {
                // Wrap + Ellipsis: if lines exceed available height, trim last visible line.
                int maxVisibleLines = Math.Max(1, heightPx / lineHeightPx);
                if (lines.Count > maxVisibleLines)
                {
                    lines.RemoveRange(maxVisibleLines, lines.Count - maxVisibleLines);

                    int lastIdx = lines.Count - 1;
                    var lastLine = lines[lastIdx];
                    if (lastLine.Length > 0)
                    {
                        var lineText = text.Slice(lastLine.Start, lastLine.Length);
                        int ellipsisW = MeasureRunWidthPx("...", face);
                        int maxTextW = Math.Max(0, widthPx - ellipsisW);

                        // Don't use TrimLineWithEllipsis here — its fast path skips the
                        // ellipsis when text fits, but wrap overflow always needs "...".
                        int trimLen = lastLine.Length;
                        int textW = MeasureRunWidthPx(lineText, face);
                        if (textW > maxTextW)
                        {
                            double avgChar = (double)textW / trimLen;
                            trimLen = Math.Clamp((int)(maxTextW / avgChar), 0, lastLine.Length);
                            textW = trimLen > 0 ? MeasureRunWidthPx(lineText.Slice(0, trimLen), face) : 0;

                            if (textW > maxTextW)
                            {
                                while (trimLen > 0 && textW > maxTextW)
                                {
                                    trimLen--;
                                    textW = trimLen > 0 ? MeasureRunWidthPx(lineText.Slice(0, trimLen), face) : 0;
                                }
                            }
                            else
                            {
                                while (trimLen < lastLine.Length)
                                {
                                    int next = MeasureRunWidthPx(lineText.Slice(0, trimLen + 1), face);
                                    if (next > maxTextW) break;
                                    trimLen++;
                                    textW = next;
                                }
                            }
                        }

                        lines[lastIdx] = new TextLayout.LineSegment(lastLine.Start, trimLen, textW + ellipsisW);
                    }

                    for (int i = 0; i < lines.Count - 1; i++)
                        trimmedFlags.Add(false);
                    trimmedFlags.Add(true);
                }
                else
                {
                    for (int i = 0; i < lines.Count; i++)
                        trimmedFlags.Add(false);
                }
            }
        }
        else
        {
            for (int i = 0; i < lines.Count; i++)
                trimmedFlags.Add(false);
        }

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

        bool useShaping = HarfBuzzAvailability.IsAvailable;

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

            var lineText = text.Slice(line.Start, line.Length);
            bool trimmed = trimmedFlags[lineIndex];

            if (useShaping)
            {
                // Append ellipsis to shaped text when trimmed.
                ReadOnlySpan<char> shapeInput;
                string? withEllipsis = null;
                if (trimmed)
                {
                    withEllipsis = string.Concat(lineText, "...");
                    shapeInput = withEllipsis.AsSpan();
                }
                else
                {
                    shapeInput = lineText;
                }

                var shaped = HarfBuzzShaper.Shape(shapeInput, face);
                if (shaped != null)
                {
                    RenderShapedGlyphs(shaped, face, font, penX, penY, buffer, widthPx, heightPx, color);
                    continue;
                }
            }

            // Fallback: character-by-character rendering.
            RenderLineFallback(text, line, face, font, penX, penY, buffer, widthPx, heightPx, color);

            if (trimmed)
            {
                // Advance penX to end of line for ellipsis placement.
                int lineEndX = penX + MeasureRunWidthPxFallback(lineText, face);
                RenderEllipsisFallback(face, font, lineEndX, penY, buffer, widthPx, heightPx, color);
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

        if (HarfBuzzAvailability.IsAvailable)
        {
            var shaped = HarfBuzzShaper.Shape(text, face);
            if (shaped != null)
            {
                return MeasureShapedWidth(shaped);
            }
        }

        return MeasureRunWidthPxFallback(text, face);
    }

    private static int MeasureShapedWidth(ShapedGlyph[] glyphs)
    {
        long total = 0;
        for (int i = 0; i < glyphs.Length; i++)
        {
            total += glyphs[i].XAdvance26_6;
        }

        return (int)Math.Round(total / 64.0, MidpointRounding.AwayFromZero);
    }

    private static int MeasureRunWidthPxFallback(ReadOnlySpan<char> text, FreeTypeFaceCache.FaceEntry face)
    {
        int width = 0;
        uint prevGlyph = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\r' || ch == '\n')
            {
                continue;
            }

            uint code;
            if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
            {
                code = (uint)char.ConvertToUtf32(ch, text[i + 1]);
                i++;
            }
            else
            {
                code = ch;
            }

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

    private static void RenderShapedGlyphs(
        ShapedGlyph[] glyphs,
        FreeTypeFaceCache.FaceEntry face,
        FreeTypeFont font,
        int startPenX,
        int penY,
        byte[] buffer,
        int widthPx,
        int heightPx,
        Color color)
    {
        long penX26_6 = (long)startPenX << 6;
        int baseY = penY + font.PixelHeight;
        int flags = FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_TARGET_LIGHT;

        for (int i = 0; i < glyphs.Length; i++)
        {
            ref readonly var g = ref glyphs[i];

            int drawX = (int)((penX26_6 + g.XOffset26_6) >> 6);
            int drawYOffset = (int)(g.YOffset26_6 >> 6);

            lock (face.SyncRoot)
            {
                if (FT.FT_Load_Glyph(face.Face, g.GlyphId, flags) != 0)
                {
                    goto Advance;
                }

                var slotPtr = face.GetGlyphSlotPointer();
                if (slotPtr != 0 && FT.FT_Get_Glyph(slotPtr, out var glyphPtr) == 0 && glyphPtr != 0)
                {
                    nint bmpGlyphPtr = glyphPtr;
                    try
                    {
                        if (FT.FT_Glyph_To_Bitmap(ref bmpGlyphPtr, FreeTypeRenderMode.FT_RENDER_MODE_NORMAL, origin: 0, destroy: false) == 0 && bmpGlyphPtr != 0)
                        {
                            var bmpGlyph = Marshal.PtrToStructure<FT_BitmapGlyphRec>(bmpGlyphPtr);
                            int dstX0 = drawX + bmpGlyph.left;
                            int dstY0 = baseY - bmpGlyph.top - drawYOffset;
                            BlitGlyph(bmpGlyph.bitmap, (int)bmpGlyph.bitmap.width, (int)bmpGlyph.bitmap.rows,
                                dstX0, dstY0, buffer, widthPx, heightPx, color);
                        }
                    }
                    finally
                    {
                        if (bmpGlyphPtr != 0 && bmpGlyphPtr != glyphPtr)
                        {
                            FT.FT_Done_Glyph(bmpGlyphPtr);
                        }

                        FT.FT_Done_Glyph(glyphPtr);
                    }
                }
            }

            Advance:
            penX26_6 += g.XAdvance26_6;
        }
    }

    private static void RenderLineFallback(
        ReadOnlySpan<char> text,
        TextLayout.LineSegment line,
        FreeTypeFaceCache.FaceEntry face,
        FreeTypeFont font,
        int penX,
        int penY,
        byte[] buffer,
        int widthPx,
        int heightPx,
        Color color)
    {
        uint prevGlyph = 0;
        int flags = FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_TARGET_LIGHT;
        int baseY = penY + font.PixelHeight;

        for (int i = 0; i < line.Length; i++)
        {
            char ch = text[line.Start + i];
            if (ch == '\r' || ch == '\n')
            {
                continue;
            }

            uint code;
            if (char.IsHighSurrogate(ch) && i + 1 < line.Length && char.IsLowSurrogate(text[line.Start + i + 1]))
            {
                code = (uint)char.ConvertToUtf32(ch, text[line.Start + i + 1]);
                i++;
            }
            else
            {
                code = ch;
            }

            uint glyph = face.GetGlyphIndex(code);
            penX += GetKerningPx(face, prevGlyph, glyph);
            prevGlyph = glyph;

            lock (face.SyncRoot)
            {
                if (FT.FT_Load_Char(face.Face, code, flags) != 0)
                {
                    goto Advance;
                }

                var slotPtr = face.GetGlyphSlotPointer();
                if (slotPtr != 0 && FT.FT_Get_Glyph(slotPtr, out var glyphPtr) == 0 && glyphPtr != 0)
                {
                    nint bmpGlyphPtr = glyphPtr;
                    try
                    {
                        int err = FT.FT_Glyph_To_Bitmap(ref bmpGlyphPtr, FreeTypeRenderMode.FT_RENDER_MODE_NORMAL, origin: 0, destroy: false);
                        if (err == 0 && bmpGlyphPtr != 0)
                        {
                            var bmpGlyph = Marshal.PtrToStructure<FT_BitmapGlyphRec>(bmpGlyphPtr);
                            BlitGlyph(bmpGlyph.bitmap, (int)bmpGlyph.bitmap.width, (int)bmpGlyph.bitmap.rows,
                                penX + bmpGlyph.left, baseY - bmpGlyph.top, buffer, widthPx, heightPx, color);
                        }
                    }
                    finally
                    {
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

    private static void RenderEllipsisFallback(
        FreeTypeFaceCache.FaceEntry face,
        FreeTypeFont font,
        int penX,
        int penY,
        byte[] buffer,
        int widthPx,
        int heightPx,
        Color color)
    {
        // Render "..." as three period glyphs.
        const string dots = "...";
        var seg = new TextLayout.LineSegment(0, dots.Length, 0);
        RenderLineFallback(dots, seg, face, font, penX, penY, buffer, widthPx, heightPx, color);
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
