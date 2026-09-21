using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Text;
using Aprillz.MewUI.Rendering.Win32;

namespace Aprillz.MewUI.Rendering.DirectWrite;

/// <summary>
/// Rasterizes a text box into a straight-alpha BGRA bitmap through DirectWrite's software glyph
/// rasterizer. Needs no device context, render target or window.
/// </summary>
internal static unsafe class DirectWriteTextRasterizer
{
    private static readonly byte[] _emptyPixel = new byte[4];

    // The layout is built with CreateGdiCompatibleTextLayout, which places glyphs on GDI's metrics.
    // Rasterizing in a natural mode instead would render shapes the layout never measured, so both
    // the rendering and the measuring mode follow the layout.
    private const DWRITE_RENDERING_MODE RENDERING_MODE = DWRITE_RENDERING_MODE.GDI_CLASSIC;
    private const DWRITE_MEASURING_MODE MEASURING_MODE = DWRITE_MEASURING_MODE.GDI_CLASSIC;

    // Colour glyphs carry no stems to fit to the pixel grid, and grid fitting costs them their
    // gradation: the same layer yields 7 distinct coverage values grid fitted against 60 here.
    // Positions still come from the GDI-compatible layout; only the outline rasterization differs.
    private const DWRITE_RENDERING_MODE COLOR_RENDERING_MODE = DWRITE_RENDERING_MODE.NATURAL_SYMMETRIC;

    /// <summary>
    /// Lays <paramref name="text"/> out in a box of <paramref name="widthPx"/> by
    /// <paramref name="heightPx"/> device pixels and returns its coverage tinted with
    /// <paramref name="color"/>. A <paramref name="buffer"/> of at least
    /// <c>widthPx * heightPx * 4</c> bytes is filled in place and aliased by the result.
    /// </summary>
    internal static TextBitmap Rasterize(
        nint factory,
        DirectWriteFont font,
        ReadOnlySpan<char> text,
        int widthPx,
        int heightPx,
        Color color,
        TextAlignment horizontalAlignment,
        TextAlignment verticalAlignment,
        TextWrapping wrapping,
        TextTrimming trimming,
        float pixelsPerDip,
        byte[]? buffer = null,
        TextInkInsetPx inkInset = default)
    {
        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);

        if (factory == 0 || text.IsEmpty)
        {
            return new TextBitmap(1, 1, _emptyPixel);
        }

        float scale = pixelsPerDip > 0 ? pixelsPerDip : 1f;
        nint textFormat = CreateTextFormat(factory, font, horizontalAlignment, verticalAlignment, wrapping);
        if (textFormat == 0)
        {
            return new TextBitmap(1, 1, _emptyPixel);
        }

        nint textLayout = 0;
        try
        {
            int hr = DWriteVTable.CreateGdiCompatibleTextLayout(
                (IDWriteFactory*)factory, text, textFormat,
                Math.Max(1, widthPx - inkInset.Left - inkInset.Right) / scale,
                Math.Max(1, heightPx - inkInset.Top - inkInset.Bottom) / scale,
                scale, useGdiNatural: false, out textLayout);
            if (hr < 0 || textLayout == 0)
            {
                return new TextBitmap(1, 1, _emptyPixel);
            }

            DirectWriteTextMeasure.ApplyCustomFontFallback(factory, textLayout);

            if (trimming == TextTrimming.CharacterEllipsis)
            {
                DWriteVTable.CreateEllipsisTrimmingSign((IDWriteFactory*)factory, textFormat, out nint sign);
                var dwriteTrimming = new DWRITE_TRIMMING { granularity = DWRITE_TRIMMING_GRANULARITY.CHARACTER };
                DWriteVTable.SetTrimming(textLayout, dwriteTrimming, sign);
                ComHelpers.Release(sign);
            }

            // Coverage is accumulated for the whole box before it is tinted, so glyphs that touch the
            // same pixel do not blend with each other the way successive alpha writes would. Colour
            // glyphs cannot share that plane because each layer carries its own colour.
            var coverage = new byte[widthPx * heightPx];
            float[]? colorPlane = null;
            foreach (var run in DWriteGlyphRunExtractor.Capture(textLayout, retainFontFaces: true))
            {
                using (run)
                {
                    AccumulateRun(factory, run, scale, widthPx, heightPx, inkInset.Left, inkInset.Top, color, coverage, ref colorPlane);
                }
            }

            int bytes = widthPx * heightPx * 4;
            var bgra = buffer is not null && buffer.Length >= bytes ? buffer : new byte[bytes];
            TintCoverage(coverage, bgra.AsSpan(0, bytes), color);
            if (colorPlane is not null)
            {
                MergeColorPlane(colorPlane, bgra.AsSpan(0, bytes));
            }

            return new TextBitmap(widthPx, heightPx, bgra);
        }
        finally
        {
            ComHelpers.Release(textLayout);
            ComHelpers.Release(textFormat);
        }
    }

    /// <summary>
    /// Lays the text out the same way <see cref="Rasterize"/> does and returns the raw per-channel
    /// coverage instead of a tinted bitmap, so a caller drawing over known pixels can blend each
    /// channel itself. Colour glyphs have no per-channel form, so a run that carries them is left
    /// to the tinted path by returning false.
    /// </summary>
    internal static bool TryRasterizeSubpixel(
        nint factory, DirectWriteFont font, ReadOnlySpan<char> text,
        int widthPx, int heightPx,
        TextAlignment horizontalAlignment, TextAlignment verticalAlignment,
        TextWrapping wrapping, TextTrimming trimming, float pixelsPerDip,
        TextInkInsetPx inkInset, out Win32TextCoverage coverage)
    {
        coverage = default;
        widthPx = Math.Max(1, widthPx);
        heightPx = Math.Max(1, heightPx);
        if (factory == 0 || text.IsEmpty)
        {
            return false;
        }

        float scale = pixelsPerDip > 0 ? pixelsPerDip : 1f;
        nint textFormat = CreateTextFormat(factory, font, horizontalAlignment, verticalAlignment, wrapping);
        if (textFormat == 0)
        {
            return false;
        }

        nint textLayout = 0;
        try
        {
            int hr = DWriteVTable.CreateGdiCompatibleTextLayout(
                (IDWriteFactory*)factory, text, textFormat,
                Math.Max(1, widthPx - inkInset.Left - inkInset.Right) / scale,
                Math.Max(1, heightPx - inkInset.Top - inkInset.Bottom) / scale,
                scale, useGdiNatural: false, out textLayout);
            if (hr < 0 || textLayout == 0)
            {
                return false;
            }

            ApplyTrimming(factory, textFormat, textLayout, trimming);
            DirectWriteTextMeasure.ApplyCustomFontFallback(factory, textLayout);

            var channels = new byte[widthPx * heightPx * 3];
            foreach (var run in DWriteGlyphRunExtractor.Capture(textLayout, retainFontFaces: true))
            {
                using (run)
                {
                    if (!AccumulateRunChannels(factory, run, scale, widthPx, heightPx,
                            inkInset.Left, inkInset.Top, channels))
                    {
                        return false;
                    }
                }
            }

            coverage = new Win32TextCoverage(widthPx, heightPx, channels);
            return true;
        }
        finally
        {
            ComHelpers.Release(textLayout);
            ComHelpers.Release(textFormat);
        }
    }

    /// <summary>Returns false for a run that carries colour layers, which have no per-channel form.</summary>
    private static bool AccumulateRunChannels(nint factory, DWriteGlyphRunExtractor.GlyphRun run,
        float pixelsPerDip, int widthPx, int heightPx, int offsetX, int offsetY, byte[] channels)
    {
        if (run.FontFace == 0 || run.GlyphIndices.Length == 0)
        {
            return true;
        }

        fixed (ushort* glyphIndices = run.GlyphIndices)
        fixed (float* advances = run.Advances)
        fixed (DWRITE_GLYPH_OFFSET* offsets = run.Offsets)
        {
            var glyphRun = new DWRITE_GLYPH_RUN
            {
                fontFace = run.FontFace,
                fontEmSize = run.FontEmSize,
                glyphCount = (uint)run.GlyphIndices.Length,
                glyphIndices = glyphIndices,
                glyphAdvances = advances,
                glyphOffsets = run.Offsets.Length == run.GlyphIndices.Length ? offsets : null,
                isSideways = run.IsSideways ? 1 : 0,
                bidiLevel = run.BidiLevel,
            };

            int colorHr = DWriteColorGlyphRun.Translate((IDWriteFactory*)factory,
                run.BaselineOriginX, run.BaselineOriginY, in glyphRun,
                MEASURING_MODE, colorPaletteIndex: 0, out nint layers);
            if (colorHr >= 0 && layers != 0)
            {
                ComHelpers.Release(layers);
                return false;
            }

            int hr = DWriteGlyphRunAnalysis.Create(
                (IDWriteFactory*)factory, in glyphRun, pixelsPerDip,
                RENDERING_MODE, MEASURING_MODE,
                run.BaselineOriginX, run.BaselineOriginY, out nint analysis);
            if (hr < 0 || analysis == 0)
            {
                return true;
            }

            try
            {
                if (DWriteGlyphRunAnalysis.GetAlphaTextureBounds(analysis, DWRITE_TEXTURE_TYPE.CLEARTYPE_3x1,
                        out var bounds) < 0)
                {
                    return true;
                }

                int runWidth = bounds.right - bounds.left;
                int runHeight = bounds.bottom - bounds.top;
                if (runWidth <= 0 || runHeight <= 0)
                {
                    return true;
                }

                var texture = new byte[runWidth * runHeight * 3];
                if (DWriteGlyphRunAnalysis.CreateAlphaTexture(analysis, DWRITE_TEXTURE_TYPE.CLEARTYPE_3x1,
                        in bounds, texture) < 0)
                {
                    return true;
                }

                for (int y = 0; y < runHeight; y++)
                {
                    int targetY = bounds.top + offsetY + y;
                    if ((uint)targetY >= (uint)heightPx)
                    {
                        continue;
                    }

                    int sourceRow = y * runWidth * 3;
                    int targetRow = targetY * widthPx * 3;
                    for (int x = 0; x < runWidth; x++)
                    {
                        int targetX = bounds.left + offsetX + x;
                        if ((uint)targetX >= (uint)widthPx)
                        {
                            continue;
                        }

                        int source = sourceRow + (x * 3);
                        int target = targetRow + (targetX * 3);
                        for (int channel = 0; channel < 3; channel++)
                        {
                            if (texture[source + channel] > channels[target + channel])
                            {
                                channels[target + channel] = texture[source + channel];
                            }
                        }
                    }
                }

                return true;
            }
            finally
            {
                DWriteGlyphRunAnalysis.Release(analysis);
            }
        }
    }

    private static void ApplyTrimming(nint factory, nint textFormat, nint textLayout, TextTrimming trimming)
    {
        if (trimming != TextTrimming.CharacterEllipsis)
        {
            return;
        }

        DWriteVTable.CreateEllipsisTrimmingSign((IDWriteFactory*)factory, textFormat, out nint sign);
        var dwriteTrimming = new DWRITE_TRIMMING { granularity = DWRITE_TRIMMING_GRANULARITY.CHARACTER };
        DWriteVTable.SetTrimming(textLayout, dwriteTrimming, sign);
        ComHelpers.Release(sign);
    }

    private static void AccumulateRun(nint factory, DWriteGlyphRunExtractor.GlyphRun run,
        float pixelsPerDip, int widthPx, int heightPx, int offsetX, int offsetY, Color textColor,
        Span<byte> coverage, ref float[]? colorPlane)
    {
        if (run.FontFace == 0 || run.GlyphIndices.Length == 0)
        {
            return;
        }

        fixed (ushort* glyphIndices = run.GlyphIndices)
        fixed (float* advances = run.Advances)
        fixed (DWRITE_GLYPH_OFFSET* offsets = run.Offsets)
        {
            var glyphRun = new DWRITE_GLYPH_RUN
            {
                fontFace = run.FontFace,
                fontEmSize = run.FontEmSize,
                glyphCount = (uint)run.GlyphIndices.Length,
                glyphIndices = glyphIndices,
                glyphAdvances = advances,
                glyphOffsets = run.Offsets.Length == run.GlyphIndices.Length ? offsets : null,
                isSideways = run.IsSideways ? 1 : 0,
                bidiLevel = run.BidiLevel,
            };

            if (TryRasterizeColorLayers(factory, in glyphRun, run.BaselineOriginX, run.BaselineOriginY,
                    pixelsPerDip, widthPx, heightPx, offsetX, offsetY, textColor, ref colorPlane))
            {
                return;
            }

            int hr = DWriteGlyphRunAnalysis.Create(
                (IDWriteFactory*)factory, in glyphRun, pixelsPerDip,
                RENDERING_MODE,
                MEASURING_MODE,
                run.BaselineOriginX, run.BaselineOriginY, out nint analysis);
            if (hr < 0 || analysis == 0)
            {
                return;
            }

            try
            {
                // The 3x1 texture is the only one a subpixel rendering mode produces; asking for the
                // 1x1 one returns an empty rectangle and no pixels.
                if (DWriteGlyphRunAnalysis.GetAlphaTextureBounds(analysis, DWRITE_TEXTURE_TYPE.CLEARTYPE_3x1,
                        out var bounds) < 0)
                {
                    return;
                }

                int runWidth = bounds.right - bounds.left;
                int runHeight = bounds.bottom - bounds.top;
                if (runWidth <= 0 || runHeight <= 0)
                {
                    return;
                }

                var texture = new byte[runWidth * runHeight * 3];
                if (DWriteGlyphRunAnalysis.CreateAlphaTexture(analysis, DWRITE_TEXTURE_TYPE.CLEARTYPE_3x1,
                        in bounds, texture) < 0)
                {
                    return;
                }

                Blit(texture, runWidth, runHeight, bounds.left + offsetX, bounds.top + offsetY, widthPx, heightPx, coverage);
            }
            finally
            {
                DWriteGlyphRunAnalysis.Release(analysis);
            }
        }
    }

    /// <summary>
    /// Splits a run into its colour layers and paints each one. Returns false when the run carries no
    /// colour glyph data, or when the system predates <c>IDWriteFactory2</c>, so the caller falls
    /// back to the monochrome path.
    /// </summary>
    private static bool TryRasterizeColorLayers(nint factory, in DWRITE_GLYPH_RUN glyphRun,
        float baselineOriginX, float baselineOriginY, float pixelsPerDip,
        int widthPx, int heightPx, int offsetX, int offsetY, Color textColor, ref float[]? colorPlane)
    {
        int hr = DWriteColorGlyphRun.Translate((IDWriteFactory*)factory, baselineOriginX, baselineOriginY,
            in glyphRun, MEASURING_MODE, colorPaletteIndex: 0, out nint layers);
        if (hr < 0 || layers == 0)
        {
            return false;
        }

        try
        {
            var plane = colorPlane ??= new float[widthPx * heightPx * 4];
            bool painted = false;
            while (DWriteColorGlyphRun.MoveNext(layers))
            {
                var layer = DWriteColorGlyphRun.GetCurrentRun(layers);
                if (layer is null)
                {
                    break;
                }

                // A layer with no palette entry of its own is drawn in the text's colour, which is how
                // a colour font mixes glyph outlines with the surrounding run.
                var (red, green, blue, alpha) = layer->paletteIndex == DWriteColorGlyphRun.USE_TEXT_COLOR
                    ? (textColor.R / 255f, textColor.G / 255f, textColor.B / 255f, textColor.A / 255f)
                    : (layer->runColor.r, layer->runColor.g, layer->runColor.b, layer->runColor.a);

                painted |= PaintLayer(factory, in layer->glyphRun, layer->baselineOriginX,
                    layer->baselineOriginY, pixelsPerDip, widthPx, heightPx, offsetX, offsetY, red, green, blue, alpha, plane);
            }

            return painted;
        }
        finally
        {
            ComHelpers.Release(layers);
        }
    }

    private static bool PaintLayer(nint factory, in DWRITE_GLYPH_RUN glyphRun,
        float baselineOriginX, float baselineOriginY, float pixelsPerDip,
        int widthPx, int heightPx, int offsetX, int offsetY, float red, float green, float blue, float alpha, float[] plane)
    {
        int hr = DWriteGlyphRunAnalysis.Create(
            (IDWriteFactory*)factory, in glyphRun, pixelsPerDip,
            COLOR_RENDERING_MODE, MEASURING_MODE,
            baselineOriginX, baselineOriginY, out nint analysis);
        if (hr < 0 || analysis == 0)
        {
            return false;
        }

        try
        {
            if (DWriteGlyphRunAnalysis.GetAlphaTextureBounds(analysis, DWRITE_TEXTURE_TYPE.CLEARTYPE_3x1,
                    out var bounds) < 0)
            {
                return false;
            }

            int runWidth = bounds.right - bounds.left;
            int runHeight = bounds.bottom - bounds.top;
            if (runWidth <= 0 || runHeight <= 0)
            {
                return false;
            }

            var texture = new byte[runWidth * runHeight * 3];
            if (DWriteGlyphRunAnalysis.CreateAlphaTexture(analysis, DWRITE_TEXTURE_TYPE.CLEARTYPE_3x1,
                    in bounds, texture) < 0)
            {
                return false;
            }

            for (int y = 0; y < runHeight; y++)
            {
                int targetY = bounds.top + offsetY + y;
                if ((uint)targetY >= (uint)heightPx)
                {
                    continue;
                }

                int sourceRow = y * runWidth * 3;
                for (int x = 0; x < runWidth; x++)
                {
                    int targetX = bounds.left + offsetX + x;
                    if ((uint)targetX >= (uint)widthPx)
                    {
                        continue;
                    }

                    int source = sourceRow + (x * 3);
                    float sourceAlpha = (texture[source] + texture[source + 1] + texture[source + 2])
                        / (3f * 255f) * alpha;
                    if (sourceAlpha <= 0)
                    {
                        continue;
                    }

                    CompositeOver(plane, ((targetY * widthPx) + targetX) * 4, red, green, blue, sourceAlpha);
                }
            }

            return true;
        }
        finally
        {
            DWriteGlyphRunAnalysis.Release(analysis);
        }
    }

    /// <summary>Straight-alpha source-over into the colour plane, which keeps layer order.</summary>
    private static void CompositeOver(float[] plane, int index, float red, float green, float blue, float alpha)
    {
        float destinationAlpha = plane[index + 3];
        float outAlpha = alpha + (destinationAlpha * (1 - alpha));
        if (outAlpha <= 0)
        {
            return;
        }

        float keep = destinationAlpha * (1 - alpha);
        plane[index] = ((red * alpha) + (plane[index] * keep)) / outAlpha;
        plane[index + 1] = ((green * alpha) + (plane[index + 1] * keep)) / outAlpha;
        plane[index + 2] = ((blue * alpha) + (plane[index + 2] * keep)) / outAlpha;
        plane[index + 3] = outAlpha;
    }

    /// <summary>Lays the colour plane over the already tinted monochrome coverage.</summary>
    private static void MergeColorPlane(float[] plane, Span<byte> bgra)
    {
        for (int i = 0, p = 0; p < bgra.Length; i += 4, p += 4)
        {
            float sourceAlpha = plane[i + 3];
            if (sourceAlpha <= 0)
            {
                continue;
            }

            float destinationAlpha = bgra[p + 3] / 255f;
            float outAlpha = sourceAlpha + (destinationAlpha * (1 - sourceAlpha));
            if (outAlpha <= 0)
            {
                continue;
            }

            float keep = destinationAlpha * (1 - sourceAlpha);
            bgra[p] = ToByte(((plane[i + 2] * sourceAlpha) + (bgra[p] / 255f * keep)) / outAlpha);
            bgra[p + 1] = ToByte(((plane[i + 1] * sourceAlpha) + (bgra[p + 1] / 255f * keep)) / outAlpha);
            bgra[p + 2] = ToByte(((plane[i] * sourceAlpha) + (bgra[p + 2] / 255f * keep)) / outAlpha);
            bgra[p + 3] = ToByte(outAlpha);
        }
    }

    private static byte ToByte(float value)
        => (byte)Math.Clamp((int)MathF.Round(value * 255f), 0, 255);

    /// <summary>Averages the three subpixels into one coverage value and keeps the strongest per pixel.</summary>
    private static void Blit(ReadOnlySpan<byte> texture, int runWidth, int runHeight,
        int originX, int originY, int widthPx, int heightPx, Span<byte> coverage)
    {
        for (int y = 0; y < runHeight; y++)
        {
            int targetY = originY + y;
            if ((uint)targetY >= (uint)heightPx)
            {
                continue;
            }

            int sourceRow = y * runWidth * 3;
            int targetRow = targetY * widthPx;
            for (int x = 0; x < runWidth; x++)
            {
                int targetX = originX + x;
                if ((uint)targetX >= (uint)widthPx)
                {
                    continue;
                }

                int source = sourceRow + (x * 3);
                int value = (texture[source] + texture[source + 1] + texture[source + 2]) / 3;
                int target = targetRow + targetX;
                if (value > coverage[target])
                {
                    coverage[target] = (byte)value;
                }
            }
        }
    }

    private static void TintCoverage(ReadOnlySpan<byte> coverage, Span<byte> bgra, Color color)
    {
        byte alpha = color.A;
        for (int i = 0, p = 0; i < coverage.Length; i++, p += 4)
        {
            byte value = coverage[i];
            if (value == 0 || alpha == 0)
            {
                bgra[p] = 0;
                bgra[p + 1] = 0;
                bgra[p + 2] = 0;
                bgra[p + 3] = 0;
                continue;
            }

            bgra[p] = color.B;
            bgra[p + 1] = color.G;
            bgra[p + 2] = color.R;
            bgra[p + 3] = (byte)(value * alpha / 255);
        }
    }

    private static nint CreateTextFormat(nint factory, DirectWriteFont font,
        TextAlignment horizontalAlignment, TextAlignment verticalAlignment, TextWrapping wrapping)
    {
        int hr = DWriteVTable.CreateTextFormat(
            (IDWriteFactory*)factory,
            font.Family,
            font.PrivateFontCollection,
            (DWRITE_FONT_WEIGHT)(int)font.Weight,
            font.IsItalic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL,
            (float)font.Size,
            out nint textFormat);
        if (hr < 0 || textFormat == 0)
        {
            return 0;
        }

        DWriteVTable.SetWordWrapping(textFormat,
            wrapping == TextWrapping.NoWrap ? DWRITE_WORD_WRAPPING.NO_WRAP : DWRITE_WORD_WRAPPING.WRAP);
        DWriteVTable.SetTextAlignment(textFormat, horizontalAlignment switch
        {
            TextAlignment.Center => DWRITE_TEXT_ALIGNMENT.CENTER,
            TextAlignment.Right => DWRITE_TEXT_ALIGNMENT.TRAILING,
            _ => DWRITE_TEXT_ALIGNMENT.LEADING,
        });
        DWriteVTable.SetParagraphAlignment(textFormat, verticalAlignment switch
        {
            TextAlignment.Center => DWRITE_PARAGRAPH_ALIGNMENT.CENTER,
            TextAlignment.Bottom => DWRITE_PARAGRAPH_ALIGNMENT.FAR,
            _ => DWRITE_PARAGRAPH_ALIGNMENT.NEAR,
        });
        return textFormat;
    }
}
