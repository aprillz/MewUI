using System.Runtime.InteropServices;

using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.DirectWrite;

/// <summary>
/// DirectWrite text measurement shared by every backend. Needs only a factory handle and a font,
/// so it runs without a device context, a render target or a window.
/// </summary>
internal static unsafe class DirectWriteTextMeasure
{
    /// <summary>
    /// Measured size in device-independent units. <paramref name="maxWidthDip"/> may be infinite.
    /// </summary>
    internal static Size Measure(nint factory, DirectWriteFont font, DWriteTextFormatCache? formatCache,
        ReadOnlySpan<char> text, double maxWidthDip, TextWrapping wrapping, TextTrimming trimming,
        float pixelsPerDip, out double contentHeight)
    {
        contentHeight = 0;
        if (text.IsEmpty || factory == 0)
        {
            return Size.Empty;
        }

        nint textFormat = AcquireTextFormat(factory, font, formatCache, wrapping, out bool ownFormat);
        if (textFormat == 0)
        {
            return Size.Empty;
        }

        nint textLayout = 0;
        try
        {
            double clamped = double.IsPositiveInfinity(maxWidthDip) ? float.MaxValue : Math.Max(0, maxWidthDip);
            float width = clamped >= float.MaxValue ? float.MaxValue : (float)clamped;

            int hr = DWriteVTable.CreateGdiCompatibleTextLayout(
                (IDWriteFactory*)factory, text, textFormat, width, float.MaxValue, pixelsPerDip,
                useGdiNatural: false, out textLayout);
            if (hr < 0 || textLayout == 0)
            {
                return Size.Empty;
            }

            ApplyCustomFontFallback(factory, textLayout);

            if (trimming == TextTrimming.CharacterEllipsis)
            {
                DWriteVTable.CreateEllipsisTrimmingSign((IDWriteFactory*)factory, textFormat, out nint trimmingSign);
                var dwriteTrimming = new DWRITE_TRIMMING { granularity = DWRITE_TRIMMING_GRANULARITY.CHARACTER };
                DWriteVTable.SetTrimming(textLayout, dwriteTrimming, trimmingSign);
                ComHelpers.Release(trimmingSign);
            }

            if (DWriteVTable.GetMetrics(textLayout, out var metrics) < 0)
            {
                return Size.Empty;
            }

            double height = metrics.height;
            if (metrics.top < 0)
            {
                height += -metrics.top;
            }

            contentHeight = height;
            return new Size(metrics.widthIncludingTrailingWhitespace, height);
        }
        finally
        {
            ComHelpers.Release(textLayout);
            if (ownFormat)
            {
                ComHelpers.Release(textFormat);
            }
        }
    }

    /// <summary>Ink of a single-line run that falls outside its layout box, in device-independent units.</summary>
    internal static TextInkOverhang GetRunInkOverhang(nint factory, DirectWriteFont font,
        DWriteTextFormatCache? formatCache, ReadOnlySpan<char> text, float pixelsPerDip)
    {
        if (text.IsEmpty || factory == 0)
        {
            return TextInkOverhang.None;
        }

        nint textFormat = AcquireTextFormat(factory, font, formatCache, TextWrapping.NoWrap, out bool ownFormat);
        if (textFormat == 0)
        {
            return TextInkOverhang.None;
        }

        nint textLayout = 0;
        try
        {
            int hr = DWriteVTable.CreateGdiCompatibleTextLayout(
                (IDWriteFactory*)factory, text, textFormat, float.MaxValue, float.MaxValue, pixelsPerDip,
                useGdiNatural: false, out textLayout);
            if (hr < 0 || textLayout == 0)
            {
                return TextInkOverhang.None;
            }

            ApplyCustomFontFallback(factory, textLayout);

            // An unconstrained layout's box is the text box, so DirectWrite reports the overhangs
            // against exactly the rectangle the caller will reserve.
            if (DWriteVTable.GetOverhangMetrics(textLayout, out var overhangs) < 0)
            {
                return TextInkOverhang.None;
            }

            return TextInkOverhang.FromEdges(overhangs.left, overhangs.top, overhangs.right, overhangs.bottom);
        }
        finally
        {
            ComHelpers.Release(textLayout);
            if (ownFormat)
            {
                ComHelpers.Release(textFormat);
            }
        }
    }

    /// <summary>
    /// Writes the cumulative advance after each UTF-16 code unit, in device-independent units.
    /// </summary>
    internal static void FillPrefixAdvances(nint factory, DirectWriteFont font,
        DWriteTextFormatCache? formatCache, ReadOnlySpan<char> text, float pixelsPerDip, Span<double> result)
    {
        nint textFormat = AcquireTextFormat(factory, font, formatCache, TextWrapping.NoWrap, out bool ownFormat);
        if (textFormat == 0)
        {
            throw new InvalidOperationException("DirectWrite could not create a text format for this font.");
        }

        nint textLayout = 0;
        try
        {
            int hr = DWriteVTable.CreateGdiCompatibleTextLayout(
                (IDWriteFactory*)factory, text, textFormat, float.MaxValue, float.MaxValue, pixelsPerDip,
                useGdiNatural: false, out textLayout);
            if (hr < 0 || textLayout == 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            ApplyCustomFontFallback(factory, textLayout);

            foreach (var run in DWriteGlyphRunExtractor.Capture(textLayout))
            {
                var glyphPrefix = new double[run.Advances.Length + 1];
                for (int i = 0; i < run.Advances.Length; i++)
                {
                    glyphPrefix[i + 1] = glyphPrefix[i] + run.Advances[i];
                }

                int local = 0;
                while (local < run.ClusterMap.Length)
                {
                    ushort glyphStart = run.ClusterMap[local];
                    int nextLocal = local + 1;
                    while (nextLocal < run.ClusterMap.Length && run.ClusterMap[nextLocal] == glyphStart)
                    {
                        nextLocal++;
                    }

                    // A run whose glyphs were all deleted still maps every character to a glyph
                    // slot, so the map runs past the glyph array. Those slots carry no width.
                    int clusterStart = Math.Min(glyphStart, run.GlyphIndices.Length);
                    int nextGlyph = nextLocal < run.ClusterMap.Length
                        ? run.ClusterMap[nextLocal]
                        : run.GlyphIndices.Length;
                    nextGlyph = Math.Clamp(nextGlyph, clusterStart, run.GlyphIndices.Length);
                    double clusterEnd = run.BaselineOriginX + glyphPrefix[nextGlyph];
                    for (int textIndex = local; textIndex < nextLocal; textIndex++)
                    {
                        int destination = checked((int)run.TextPosition + textIndex);
                        if ((uint)destination < (uint)result.Length)
                        {
                            result[destination] = clusterEnd;
                        }
                    }
                    local = nextLocal;
                }
            }

            double previous = 0;
            for (int i = 0; i < result.Length; i++)
            {
                if (result[i] <= 0)
                {
                    result[i] = previous;
                }
                previous = Math.Max(previous, result[i]);
                result[i] = previous;
            }
        }
        finally
        {
            ComHelpers.Release(textLayout);
            if (ownFormat)
            {
                ComHelpers.Release(textFormat);
            }
        }
    }

    internal static void ApplyCustomFontFallback(nint factory, nint textLayout)
    {
        if (textLayout == 0)
        {
            return;
        }

        var fallback = DWriteFontFallbackHelper.GetOrCreate((IDWriteFactory*)factory);
        if (fallback == 0)
        {
            return;
        }

        _ = DWriteTextLayout2VTable.SetFontFallback(textLayout, fallback);
    }

    /// <summary>
    /// A cached format is owned by the cache; one created here is owned by the caller, which
    /// <paramref name="ownFormat"/> reports.
    /// </summary>
    private static nint AcquireTextFormat(nint factory, DirectWriteFont font,
        DWriteTextFormatCache? formatCache, TextWrapping wrapping, out bool ownFormat)
    {
        ownFormat = false;
        if (formatCache is not null)
        {
            return formatCache.GetOrCreate(factory, font, TextAlignment.Left, TextAlignment.Top, wrapping);
        }

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
        ownFormat = true;
        return textFormat;
    }
}
