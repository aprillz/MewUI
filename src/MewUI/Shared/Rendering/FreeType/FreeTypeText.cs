using System.Globalization;
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

        TextLayoutUtils.EnumerateLines(text, maxWidthPx, wrapping, span => MeasureRunWidthPx(span, face, font.PixelHeight, font.Weight, font.IsItalic), line =>
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

        var lines = new List<LineSegment>();
        int maxLineWidth = 0;
        int effectiveWrapWidth = wrapping == TextWrapping.Wrap ? widthPx : 0;
        TextLayoutUtils.EnumerateLines(text, effectiveWrapWidth, wrapping, span => MeasureRunWidthPx(span, face, font.PixelHeight, font.Weight, font.IsItalic), line =>
        {
            int width = (int)Math.Ceiling(line.Width);
            if (width > maxLineWidth)
            {
                maxLineWidth = width;
            }
            lines.Add(new LineSegment(line.Start, line.Length, width));
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
                        var trimmed = TextLayoutUtils.TrimLineWithEllipsis(lineText, line.Start, widthPx, span => MeasureRunWidthPx(span, face, font.PixelHeight, font.Weight, font.IsItalic));
                        lines[i] = new LineSegment(trimmed.Start, trimmed.Length, trimmed.Width);
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
                        int ellipsisW = MeasureRunWidthPx("...", face, font.PixelHeight, font.Weight, font.IsItalic);
                        int maxTextW = Math.Max(0, widthPx - ellipsisW);

                        // Don't use TrimLineWithEllipsis here - its fast path skips the
                        // ellipsis when text fits, but wrap overflow always needs "...".
                        int trimLen = lastLine.Length;
                        int textW = MeasureRunWidthPx(lineText, face, font.PixelHeight, font.Weight, font.IsItalic);
                        if (textW > maxTextW)
                        {
                            double avgChar = (double)textW / trimLen;
                            trimLen = Math.Clamp((int)(maxTextW / avgChar), 0, lastLine.Length);
                            textW = trimLen > 0 ? MeasureRunWidthPx(lineText.Slice(0, trimLen), face, font.PixelHeight, font.Weight, font.IsItalic) : 0;

                            if (textW > maxTextW)
                            {
                                while (trimLen > 0 && textW > maxTextW)
                                {
                                    trimLen--;
                                    textW = trimLen > 0 ? MeasureRunWidthPx(lineText.Slice(0, trimLen), face, font.PixelHeight, font.Weight, font.IsItalic) : 0;
                                }
                            }
                            else
                            {
                                while (trimLen < lastLine.Length)
                                {
                                    int next = MeasureRunWidthPx(lineText.Slice(0, trimLen + 1), face, font.PixelHeight, font.Weight, font.IsItalic);
                                    if (next > maxTextW) break;
                                    trimLen++;
                                    textW = next;
                                }
                            }
                        }

                        lines[lastIdx] = new LineSegment(lastLine.Start, trimLen, textW + ellipsisW);
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

        // Use actual text width for centering - padding is for external Measure() only.
        int contentW = Math.Min(widthPx, maxLineWidth);
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

            int lineWidthPx = line.Width > 0 ? (int)Math.Ceiling(line.Width) : 0;
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
                    RenderShapedGlyphs(shaped, face, font, penX, penY, buffer, widthPx, heightPx, color, shapeInput);
                    continue;
                }
            }

            // Fallback: character-by-character rendering.
            RenderLineFallback(text, line, face, font, penX, penY, buffer, widthPx, heightPx, color);

            if (trimmed)
            {
                // Advance penX to end of line for ellipsis placement.
                int lineEndX = penX + MeasureRunWidthPxFallback(lineText, face, font.PixelHeight, font.Weight, font.IsItalic);
                RenderEllipsisFallback(face, font, lineEndX, penY, buffer, widthPx, heightPx, color);
            }
        }

        return new TextBitmap(widthPx, heightPx, buffer);
    }

    private static int MeasureRunWidthPx(ReadOnlySpan<char> text, FreeTypeFaceCache.FaceEntry face,
        int pixelHeight, FontWeight weight, bool italic)
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
                return MeasureShapedWidth(shaped, text, pixelHeight, weight, italic);
            }
        }

        return MeasureRunWidthPxFallback(text, face, pixelHeight, weight, italic);
    }

    /// <summary>
    /// Measures the total width of shaped glyphs, substituting fallback face advances
    /// for .notdef glyphs (glyph ID 0) so that measurement matches rendering.
    /// Consecutive .notdef glyphs are re-shaped with the fallback font to handle ZWJ sequences.
    /// </summary>
    private static int MeasureShapedWidth(ShapedGlyph[] glyphs,
        ReadOnlySpan<char> sourceText, int pixelHeight, FontWeight weight, bool italic)
    {
        long total = 0;

        for (int i = 0; i < glyphs.Length; i++)
        {
            ref readonly var g = ref glyphs[i];

            if (g.GlyphId == 0 && !sourceText.IsEmpty)
            {
                // Use grapheme cluster boundary to capture full ZWJ sequences
                int textStart = (int)g.Cluster;
                int clusterLen = StringInfo.GetNextTextElementLength(sourceText.Slice(textStart));
                int textEnd = textStart + clusterLen;

                // Collect ALL glyphs within this grapheme cluster range
                int runStart = i;
                while (i + 1 < glyphs.Length && (int)glyphs[i + 1].Cluster < textEnd)
                    i++;

                if (textEnd > textStart)
                {
                    uint firstCp = GetCodepointFromCluster(sourceText, (uint)textStart);
                    if (firstCp != 0)
                    {
                        var fallbackFace = LinuxFontFallbackResolver.Resolve(
                            firstCp, pixelHeight, weight, italic);
                        if (fallbackFace != null)
                        {
                            // Re-shape with fallback font (ZWJ sequences combine here)
                            var clusterText = sourceText.Slice(textStart, textEnd - textStart);
                            var reshaped = HarfBuzzShaper.Shape(clusterText, fallbackFace);
                            if (reshaped != null && reshaped.Length > 0)
                            {
                                double bitmapScale = fallbackFace.BitmapScale;
                                for (int j = 0; j < reshaped.Length; j++)
                                    total += (long)Math.Round(reshaped[j].XAdvance26_6 * bitmapScale);
                                continue;
                            }
                        }
                    }
                }

                // Fallback: individual codepoint advances
                for (int j = runStart; j <= i; j++)
                {
                    ref readonly var gg = ref glyphs[j];
                    uint codepoint = GetCodepointFromCluster(sourceText, gg.Cluster);
                    if (codepoint != 0)
                    {
                        var fb = LinuxFontFallbackResolver.Resolve(
                            codepoint, pixelHeight, weight, italic);
                        if (fb != null && fb.GetGlyphIndex(codepoint) != 0)
                        {
                            total += (long)fb.GetAdvancePx(codepoint) << 6;
                            continue;
                        }
                    }
                    total += gg.XAdvance26_6;
                }
                continue;
            }

            total += g.XAdvance26_6;
        }

        return (int)Math.Round(total / 64.0, MidpointRounding.AwayFromZero);
    }

    private static int MeasureRunWidthPxFallback(ReadOnlySpan<char> text, FreeTypeFaceCache.FaceEntry face,
        int pixelHeight, FontWeight weight, bool italic)
    {
        int width = 0;
        uint prevGlyph = 0;
        FreeTypeFaceCache.FaceEntry prevFace = face;
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
            var activeFace = face;

            if (glyph == 0)
            {
                var fallbackFace = LinuxFontFallbackResolver.Resolve(code, pixelHeight, weight, italic);
                if (fallbackFace != null)
                {
                    uint fbGlyph = fallbackFace.GetGlyphIndex(code);
                    if (fbGlyph != 0)
                    {
                        activeFace = fallbackFace;
                        glyph = fbGlyph;
                    }
                }
            }

            // Only apply kerning within the same face
            if (activeFace == prevFace)
            {
                width += GetKerningPx(activeFace, prevGlyph, glyph);
            }
            width += activeFace.GetAdvancePx(code);
            prevGlyph = glyph;
            prevFace = activeFace;
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
        Color color,
        ReadOnlySpan<char> sourceText = default)
    {
        long penX26_6 = (long)startPenX << 6;
        int baseY = penY + font.PixelHeight;
        int flags = FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_TARGET_LIGHT;

        for (int i = 0; i < glyphs.Length; i++)
        {
            ref readonly var g = ref glyphs[i];

            // Detect .notdef glyph (glyph ID 0) - try fallback with ZWJ-aware reshaping
            if (g.GlyphId == 0 && !sourceText.IsEmpty)
            {
                // Use grapheme cluster boundary (not consecutive .notdef) to capture
                // the full ZWJ sequence - intermediate chars like ZWJ/VS16 may have
                // valid glyphs in the primary font but must be re-shaped together.
                int textStart = (int)g.Cluster;
                int clusterLen = StringInfo.GetNextTextElementLength(sourceText.Slice(textStart));
                int textEnd = textStart + clusterLen;

                // Collect ALL glyphs within this grapheme cluster range
                int runStart = i;
                while (i + 1 < glyphs.Length && (int)glyphs[i + 1].Cluster < textEnd)
                    i++;

                if (textEnd > textStart)
                {
                    var fallbackText = sourceText.Slice(textStart, textEnd - textStart);
                    uint firstCp = GetCodepointFromCluster(sourceText, (uint)textStart);

                    if (firstCp != 0)
                    {
                        var fallbackFace = LinuxFontFallbackResolver.Resolve(
                            firstCp, font.PixelHeight, font.Weight, font.IsItalic);
                        if (fallbackFace != null)
                        {
                            // Re-shape the entire grapheme cluster with fallback font
                            var reshaped = HarfBuzzShaper.Shape(fallbackText, fallbackFace);
                            if (reshaped != null && reshaped.Length > 0)
                            {
                                penX26_6 += RenderShapedGlyphsDirect(
                                    reshaped, fallbackFace, font, penX26_6, baseY,
                                    buffer, widthPx, heightPx, color);
                                continue;
                            }
                        }
                    }
                }

                // Fallback: render individual codepoints
                for (int j = runStart; j <= i; j++)
                {
                    ref readonly var gg = ref glyphs[j];
                    uint codepoint = GetCodepointFromCluster(sourceText, gg.Cluster);
                    if (codepoint != 0)
                    {
                        var fb = LinuxFontFallbackResolver.Resolve(
                            codepoint, font.PixelHeight, font.Weight, font.IsItalic);
                        if (fb != null)
                        {
                            uint fbGlyph = fb.GetGlyphIndex(codepoint);
                            if (fbGlyph != 0)
                            {
                                int drawX = (int)(penX26_6 >> 6);
                                RenderFallbackCodepoint(fb, codepoint, fbGlyph, drawX, baseY,
                                    buffer, widthPx, heightPx, color, out int advancePx);
                                penX26_6 += (long)advancePx << 6;
                                continue;
                            }
                        }
                    }
                    penX26_6 += gg.XAdvance26_6;
                }
                continue;
            }

            int glyphDrawX = (int)((penX26_6 + g.XOffset26_6) >> 6);
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
                            int dstX0 = glyphDrawX + bmpGlyph.left;
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

    /// <summary>
    /// Renders shaped glyphs from a specific face (typically a fallback emoji font).
    /// Handles color emoji (BGRA) and BitmapScale. Does NOT attempt further fallback for .notdef.
    /// Returns total advance in 26.6 fixed-point.
    /// </summary>
    private static long RenderShapedGlyphsDirect(
        ShapedGlyph[] glyphs,
        FreeTypeFaceCache.FaceEntry face,
        FreeTypeFont font,
        long startPenX26_6,
        int baseY,
        byte[] buffer,
        int widthPx,
        int heightPx,
        Color color)
    {
        long penX26_6 = startPenX26_6;
        bool isColorFont = FT.FT_HAS_COLOR(face.Face);
        double scale = face.BitmapScale;

        for (int i = 0; i < glyphs.Length; i++)
        {
            ref readonly var g = ref glyphs[i];
            long scaledAdvance = (long)Math.Round(g.XAdvance26_6 * scale);
            if (g.GlyphId == 0) { penX26_6 += scaledAdvance; continue; }

            int glyphDrawX = (int)((penX26_6 + (long)Math.Round(g.XOffset26_6 * scale)) >> 6);
            int drawYOffset = (int)((long)Math.Round(g.YOffset26_6 * scale) >> 6);

            if (isColorFont)
            {
                // Cached path: area-average downscale once, blit from cache thereafter.
                BlitCachedColorGlyph(face, g.GlyphId,
                    glyphDrawX, baseY - drawYOffset, buffer, widthPx, heightPx);
            }
            else
            {
                int flags = FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_TARGET_LIGHT;
                lock (face.SyncRoot)
                {
                    if (FT.FT_Load_Glyph(face.Face, g.GlyphId, flags) != 0)
                        goto Advance;

                    var slotPtr = face.GetGlyphSlotPointer();
                    if (slotPtr != 0 && FT.FT_Get_Glyph(slotPtr, out var glyphPtr) == 0 && glyphPtr != 0)
                    {
                        nint bmpGlyphPtr = glyphPtr;
                        try
                        {
                            if (FT.FT_Glyph_To_Bitmap(ref bmpGlyphPtr, FreeTypeRenderMode.FT_RENDER_MODE_NORMAL, origin: 0, destroy: false) == 0 && bmpGlyphPtr != 0)
                            {
                                var bmpGlyph = Marshal.PtrToStructure<FT_BitmapGlyphRec>(bmpGlyphPtr);
                                int left = (int)(bmpGlyph.left * scale);
                                int top = (int)(bmpGlyph.top * scale);
                                BlitGlyph(bmpGlyph.bitmap, (int)bmpGlyph.bitmap.width, (int)bmpGlyph.bitmap.rows,
                                    glyphDrawX + left, baseY - top - drawYOffset, buffer, widthPx, heightPx, color, scale);
                            }
                        }
                        finally
                        {
                            if (bmpGlyphPtr != 0 && bmpGlyphPtr != glyphPtr)
                                FT.FT_Done_Glyph(bmpGlyphPtr);
                            FT.FT_Done_Glyph(glyphPtr);
                        }
                    }
                }
            }

            Advance:
            penX26_6 += scaledAdvance;
        }

        return penX26_6 - startPenX26_6;
    }

    private static uint GetCodepointFromCluster(ReadOnlySpan<char> text, uint cluster)
    {
        int offset = (int)cluster;
        if ((uint)offset >= (uint)text.Length) return 0;

        char ch = text[offset];
        if (char.IsHighSurrogate(ch) && offset + 1 < text.Length && char.IsLowSurrogate(text[offset + 1]))
        {
            return (uint)char.ConvertToUtf32(ch, text[offset + 1]);
        }

        return ch;
    }

    private static void RenderFallbackCodepoint(
        FreeTypeFaceCache.FaceEntry fallbackFace,
        uint codepoint,
        uint glyphId,
        int penX,
        int baseY,
        byte[] buffer,
        int widthPx,
        int heightPx,
        Color color,
        out int advancePx)
    {
        advancePx = fallbackFace.GetAdvancePx(codepoint);

        bool isColorFont = FT.FT_HAS_COLOR(fallbackFace.Face);

        if (isColorFont)
        {
            // Cached path: area-average downscale once, blit from cache thereafter.
            BlitCachedColorGlyph(fallbackFace, glyphId,
                penX, baseY, buffer, widthPx, heightPx);
            return;
        }

        int flags = FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_TARGET_LIGHT;

        lock (fallbackFace.SyncRoot)
        {
            if (FT.FT_Load_Glyph(fallbackFace.Face, glyphId, flags) != 0) return;

            var slotPtr = fallbackFace.GetGlyphSlotPointer();
            if (slotPtr == 0) return;

            if (FT.FT_Get_Glyph(slotPtr, out var glyphPtr) != 0 || glyphPtr == 0) return;

            nint bmpGlyphPtr = glyphPtr;
            try
            {
                if (FT.FT_Glyph_To_Bitmap(ref bmpGlyphPtr, FreeTypeRenderMode.FT_RENDER_MODE_NORMAL, origin: 0, destroy: false) == 0 && bmpGlyphPtr != 0)
                {
                    var bmpGlyph = Marshal.PtrToStructure<FT_BitmapGlyphRec>(bmpGlyphPtr);
                    double scale = fallbackFace.BitmapScale;
                    int left = (int)(bmpGlyph.left * scale);
                    int top = (int)(bmpGlyph.top * scale);
                    BlitGlyph(bmpGlyph.bitmap, (int)bmpGlyph.bitmap.width, (int)bmpGlyph.bitmap.rows,
                        penX + left, baseY - top, buffer, widthPx, heightPx, color, scale);
                }
            }
            finally
            {
                if (bmpGlyphPtr != 0 && bmpGlyphPtr != glyphPtr)
                    FT.FT_Done_Glyph(bmpGlyphPtr);
                FT.FT_Done_Glyph(glyphPtr);
            }
        }
    }

    private static void RenderLineFallback(
        ReadOnlySpan<char> text,
        LineSegment line,
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
        FreeTypeFaceCache.FaceEntry prevFace = face;
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
            var activeFace = face;

            if (glyph == 0)
            {
                var fallbackFace = LinuxFontFallbackResolver.Resolve(code, font.PixelHeight, font.Weight, font.IsItalic);
                if (fallbackFace != null && fallbackFace.GetGlyphIndex(code) != 0)
                {
                    activeFace = fallbackFace;
                    glyph = fallbackFace.GetGlyphIndex(code);
                }
            }

            // Only apply kerning within the same face
            if (activeFace == prevFace)
            {
                penX += GetKerningPx(activeFace, prevGlyph, glyph);
            }
            prevGlyph = glyph;
            prevFace = activeFace;

            // Check for color emoji (CBDT/CBLC bitmap fonts)
            bool isColorFont = FT.FT_HAS_COLOR(activeFace.Face);

            if (isColorFont && glyph != 0)
            {
                // Cached path: area-average downscale once, blit from cache thereafter.
                BlitCachedColorGlyph(activeFace, glyph,
                    penX, baseY, buffer, widthPx, heightPx);
            }
            else
            {
                lock (activeFace.SyncRoot)
                {
                    if (FT.FT_Load_Char(activeFace.Face, code, flags) != 0)
                    {
                        goto Advance;
                    }

                    var slotPtr = activeFace.GetGlyphSlotPointer();
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
            }

            Advance:
            penX += activeFace.GetAdvancePx(code);
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
        var seg = new LineSegment(0, dots.Length, 0);
        RenderLineFallback(dots, seg, face, font, penX, penY, buffer, widthPx, heightPx, color);
    }

    /// <summary>
    /// Loads, scales (with area averaging), caches, and blits a color emoji glyph.
    /// The scaled bitmap is cached per (face, glyphId) so subsequent renders are a plain blit.
    /// </summary>
    private static void BlitCachedColorGlyph(
        FreeTypeFaceCache.FaceEntry face,
        uint glyphId,
        int dstX0,
        int dstY0,
        byte[] dstBgra,
        int dstW,
        int dstH)
    {
        var cached = face.GetScaledBitmap(glyphId);
        if (cached != null)
        {
            var c = cached.Value;
            BlitPreScaledBgra(c.Bgra, c.Width, c.Height,
                dstX0 + c.Left, dstY0 - c.Top, dstBgra, dstW, dstH);
            return;
        }

        // Cache miss - load, scale, cache.
        int flags = FreeTypeLoad.FT_LOAD_DEFAULT | FreeTypeLoad.FT_LOAD_TARGET_LIGHT | FreeTypeLoad.FT_LOAD_COLOR;
        lock (face.SyncRoot)
        {
            // Re-check under lock (another thread may have populated the cache).
            cached = face.GetScaledBitmap(glyphId);
            if (cached != null)
            {
                var c2 = cached.Value;
                BlitPreScaledBgra(c2.Bgra, c2.Width, c2.Height,
                    dstX0 + c2.Left, dstY0 - c2.Top, dstBgra, dstW, dstH);
                return;
            }

            if (FT.FT_Load_Glyph(face.Face, glyphId, flags) != 0) return;

            var slotPtr = face.GetGlyphSlotPointer();
            if (slotPtr == 0) return;
            if (FT.FT_Get_Glyph(slotPtr, out var glyphPtr) != 0 || glyphPtr == 0) return;

            nint bmpGlyphPtr = glyphPtr;
            try
            {
                if (FT.FT_Glyph_To_Bitmap(ref bmpGlyphPtr, FreeTypeRenderMode.FT_RENDER_MODE_NORMAL, origin: 0, destroy: false) != 0 || bmpGlyphPtr == 0)
                    return;

                var bmpGlyph = Marshal.PtrToStructure<FT_BitmapGlyphRec>(bmpGlyphPtr);
                double scale = face.BitmapScale;
                int srcW = (int)bmpGlyph.bitmap.width;
                int srcH = (int)bmpGlyph.bitmap.rows;
                int left = (int)(bmpGlyph.left * scale);
                int top = (int)(bmpGlyph.top * scale);

                if (srcW <= 0 || srcH <= 0 || bmpGlyph.bitmap.buffer == null)
                    return;

                int outW = Math.Max(1, (int)(srcW * scale));
                int outH = Math.Max(1, (int)(srcH * scale));
                byte[] scaled = ScaleColorBitmap(bmpGlyph.bitmap, srcW, srcH, outW, outH);

                var entry = new FreeTypeFaceCache.FaceEntry.ScaledBitmap(scaled, outW, outH, left, top);
                face.CacheScaledBitmap(glyphId, entry);

                BlitPreScaledBgra(scaled, outW, outH,
                    dstX0 + left, dstY0 - top, dstBgra, dstW, dstH);
            }
            finally
            {
                if (bmpGlyphPtr != 0 && bmpGlyphPtr != glyphPtr)
                    FT.FT_Done_Glyph(bmpGlyphPtr);
                FT.FT_Done_Glyph(glyphPtr);
            }
        }
    }

    /// <summary>
    /// Pre-scales a BGRA bitmap using area averaging (box filter).
    /// </summary>
    private static unsafe byte[] ScaleColorBitmap(FT_Bitmap bitmap, int srcW, int srcH, int outW, int outH)
    {
        var result = new byte[outW * outH * 4];
        int pitch = bitmap.pitch;
        double invScaleX = (double)srcW / outW;
        double invScaleY = (double)srcH / outH;

        for (int y = 0; y < outH; y++)
        {
            double areaY0 = y * invScaleY;
            double areaY1 = (y + 1) * invScaleY;
            int isy0 = (int)areaY0;
            int isy1 = Math.Min((int)Math.Ceiling(areaY1), srcH);

            for (int x = 0; x < outW; x++)
            {
                double areaX0 = x * invScaleX;
                double areaX1 = (x + 1) * invScaleX;
                int isx0 = (int)areaX0;
                int isx1 = Math.Min((int)Math.Ceiling(areaX1), srcW);

                double totalB = 0, totalG = 0, totalR = 0, totalA = 0;
                double totalWeight = 0;

                for (int sy = isy0; sy < isy1; sy++)
                {
                    double wy = Math.Min(sy + 1, areaY1) - Math.Max(sy, areaY0);
                    byte* srcRow = bitmap.buffer + sy * pitch;

                    for (int sx = isx0; sx < isx1; sx++)
                    {
                        double wx = Math.Min(sx + 1, areaX1) - Math.Max(sx, areaX0);
                        double w = wx * wy;

                        byte* p = srcRow + sx * 4;
                        totalB += p[0] * w;
                        totalG += p[1] * w;
                        totalR += p[2] * w;
                        totalA += p[3] * w;
                        totalWeight += w;
                    }
                }

                int di = (y * outW + x) * 4;
                if (totalWeight > 0)
                {
                    double inv = 1.0 / totalWeight;
                    result[di + 0] = (byte)Math.Min(255, (int)(totalB * inv + 0.5));
                    result[di + 1] = (byte)Math.Min(255, (int)(totalG * inv + 0.5));
                    result[di + 2] = (byte)Math.Min(255, (int)(totalR * inv + 0.5));
                    result[di + 3] = (byte)Math.Min(255, (int)(totalA * inv + 0.5));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Blits a pre-scaled BGRA buffer (no further scaling needed).
    /// </summary>
    private static void BlitPreScaledBgra(
        byte[] src, int srcW, int srcH,
        int dstX0, int dstY0,
        byte[] dstBgra, int dstW, int dstH)
    {
        for (int y = 0; y < srcH; y++)
        {
            int dy = dstY0 + y;
            if ((uint)dy >= (uint)dstH) continue;

            for (int x = 0; x < srcW; x++)
            {
                int dx = dstX0 + x;
                if ((uint)dx >= (uint)dstW) continue;

                int si = (y * srcW + x) * 4;
                byte srcA = src[si + 3];
                if (srcA == 0) continue;

                int di = (dy * dstW + dx) * 4;

                if (srcA == 255)
                {
                    dstBgra[di + 0] = src[si + 0];
                    dstBgra[di + 1] = src[si + 1];
                    dstBgra[di + 2] = src[si + 2];
                    dstBgra[di + 3] = 255;
                    continue;
                }

                byte dstA0 = dstBgra[di + 3];
                int invAlpha = 255 - srcA;

                dstBgra[di + 0] = (byte)(src[si + 0] + dstBgra[di + 0] * invAlpha / 255);
                dstBgra[di + 1] = (byte)(src[si + 1] + dstBgra[di + 1] * invAlpha / 255);
                dstBgra[di + 2] = (byte)(src[si + 2] + dstBgra[di + 2] * invAlpha / 255);
                dstBgra[di + 3] = (byte)(srcA + dstA0 * invAlpha / 255);
            }
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
        Color color,
        double scale = 1.0)
    {
        if (bitmap.buffer == null || glyphW <= 0 || glyphH <= 0)
        {
            return;
        }

        if (bitmap.pixel_mode == 7) return;
        if (bitmap.pixel_mode != 2) return;

        int pitch = bitmap.pitch;
        if (pitch == 0) return;

        byte r = color.R;
        byte g = color.G;
        byte b = color.B;
        byte a0 = color.A;

        int outW = (int)(glyphW * scale);
        int outH = (int)(glyphH * scale);
        double invScale = 1.0 / scale;

        for (int y = 0; y < outH; y++)
        {
            int dy = dstY0 + y;
            if ((uint)dy >= (uint)dstH) continue;

            int srcY = Math.Min((int)(y * invScale), glyphH - 1);
            byte* srcRow = bitmap.buffer + srcY * pitch;

            for (int x = 0; x < outW; x++)
            {
                int dx = dstX0 + x;
                if ((uint)dx >= (uint)dstW) continue;

                int srcX = Math.Min((int)(x * invScale), glyphW - 1);
                byte cov = srcRow[srcX];
                if (cov == 0) continue;

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

    /// <summary>
    /// Blits a BGRA color bitmap glyph (e.g., color emoji from CBDT/CBLC tables).
    /// pixel_mode == 7 (FT_PIXEL_MODE_BGRA): each pixel is 4 bytes (B, G, R, A premultiplied).
    /// Uses area averaging (box filter) for downscaling, bilinear for upscaling.
    /// </summary>
    private static unsafe void BlitColorGlyph(
        FT_Bitmap bitmap,
        int glyphW,
        int glyphH,
        int dstX0,
        int dstY0,
        byte[] dstBgra,
        int dstW,
        int dstH,
        double scale = 1.0)
    {
        if (bitmap.buffer == null || glyphW <= 0 || glyphH <= 0)
        {
            return;
        }

        int pitch = bitmap.pitch;
        if (pitch == 0)
        {
            return;
        }

        int outW = (int)(glyphW * scale);
        int outH = (int)(glyphH * scale);
        if (outW <= 0 || outH <= 0) return;
        double invScale = 1.0 / scale;
        bool downscale = scale < 1.0;

        for (int y = 0; y < outH; y++)
        {
            int dy = dstY0 + y;
            if ((uint)dy >= (uint)dstH) continue;

            for (int x = 0; x < outW; x++)
            {
                int dx = dstX0 + x;
                if ((uint)dx >= (uint)dstW) continue;

                byte srcB, srcG, srcR, srcA;

                if (downscale)
                {
                    // Area averaging: each output pixel covers a rectangle in source space.
                    // Average all source pixels weighted by their overlap area.
                    double areaX0 = x * invScale;
                    double areaX1 = (x + 1) * invScale;
                    double areaY0 = y * invScale;
                    double areaY1 = (y + 1) * invScale;

                    int isx0 = (int)areaX0;
                    int isx1 = Math.Min((int)Math.Ceiling(areaX1), glyphW);
                    int isy0 = (int)areaY0;
                    int isy1 = Math.Min((int)Math.Ceiling(areaY1), glyphH);

                    double totalB = 0, totalG = 0, totalR = 0, totalA = 0;
                    double totalWeight = 0;

                    for (int sy = isy0; sy < isy1; sy++)
                    {
                        double wy = Math.Min(sy + 1, areaY1) - Math.Max(sy, areaY0);
                        byte* srcRow = bitmap.buffer + sy * pitch;

                        for (int sx = isx0; sx < isx1; sx++)
                        {
                            double wx = Math.Min(sx + 1, areaX1) - Math.Max(sx, areaX0);
                            double w = wx * wy;

                            byte* p = srcRow + sx * 4;
                            totalB += p[0] * w;
                            totalG += p[1] * w;
                            totalR += p[2] * w;
                            totalA += p[3] * w;
                            totalWeight += w;
                        }
                    }

                    if (totalWeight <= 0) continue;
                    double inv = 1.0 / totalWeight;
                    srcB = (byte)Math.Min(255, (int)(totalB * inv + 0.5));
                    srcG = (byte)Math.Min(255, (int)(totalG * inv + 0.5));
                    srcR = (byte)Math.Min(255, (int)(totalR * inv + 0.5));
                    srcA = (byte)Math.Min(255, (int)(totalA * inv + 0.5));
                }
                else
                {
                    // Bilinear interpolation for upscaling or 1:1
                    double srcXf = x * invScale;
                    double srcYf = y * invScale;
                    int sx0 = Math.Min((int)srcXf, glyphW - 1);
                    int sx1 = Math.Min(sx0 + 1, glyphW - 1);
                    int sy0 = Math.Min((int)srcYf, glyphH - 1);
                    int sy1 = Math.Min(sy0 + 1, glyphH - 1);
                    double fx = srcXf - sx0;
                    double fy = srcYf - sy0;

                    byte* r00 = bitmap.buffer + sy0 * pitch + sx0 * 4;
                    byte* r01 = bitmap.buffer + sy0 * pitch + sx1 * 4;
                    byte* r10 = bitmap.buffer + sy1 * pitch + sx0 * 4;
                    byte* r11 = bitmap.buffer + sy1 * pitch + sx1 * 4;

                    double w00 = (1 - fx) * (1 - fy);
                    double w01 = fx * (1 - fy);
                    double w10 = (1 - fx) * fy;
                    double w11 = fx * fy;

                    srcB = (byte)(r00[0] * w00 + r01[0] * w01 + r10[0] * w10 + r11[0] * w11);
                    srcG = (byte)(r00[1] * w00 + r01[1] * w01 + r10[1] * w10 + r11[1] * w11);
                    srcR = (byte)(r00[2] * w00 + r01[2] * w01 + r10[2] * w10 + r11[2] * w11);
                    srcA = (byte)(r00[3] * w00 + r01[3] * w01 + r10[3] * w10 + r11[3] * w11);
                }

                if (srcA == 0) continue;

                int di = (dy * dstW + dx) * 4;

                if (srcA == 255)
                {
                    dstBgra[di + 0] = srcB;
                    dstBgra[di + 1] = srcG;
                    dstBgra[di + 2] = srcR;
                    dstBgra[di + 3] = 255;
                    continue;
                }

                byte dstA0 = dstBgra[di + 3];
                int invAlpha = 255 - srcA;

                dstBgra[di + 0] = (byte)(srcB + dstBgra[di + 0] * invAlpha / 255);
                dstBgra[di + 1] = (byte)(srcG + dstBgra[di + 1] * invAlpha / 255);
                dstBgra[di + 2] = (byte)(srcR + dstBgra[di + 2] * invAlpha / 255);
                dstBgra[di + 3] = (byte)(srcA + dstA0 * invAlpha / 255);
            }
        }
    }

    /// <summary>
    /// Extracts the pixel height from a FaceEntry by reading the FT_Size metrics.
    /// Used when we only have a FaceEntry and need the pixel height for fallback resolution.
    /// </summary>
    private static int GetPixelHeight(FreeTypeFaceCache.FaceEntry face)
    {
        var metrics = FreeTypeFaceCache.GetSizeMetrics(face.Face);
        return metrics.y_ppem;
    }

}
