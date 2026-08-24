using System.Runtime.InteropServices;
using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed unsafe class Direct2DMeasurementContext : MeasureGraphicsContextBase, ITextAdvanceSource
{
    private readonly nint _dwriteFactory;
    private readonly DWriteTextFormatCache? _textFormatCache;

    // Layout grid for the GDI-compatible metrics; sizes still come back in DIPs.
    private readonly float _pixelsPerDip;

    public override double DpiScale => 1.0;

    public Direct2DMeasurementContext(nint dwriteFactory, uint dpi = 96, DWriteTextFormatCache? textFormatCache = null)
    {
        _dwriteFactory = dwriteFactory;
        _pixelsPerDip = dpi > 0 ? dpi / 96f : 1f;
        _textFormatCache = textFormatCache;
    }

    private BackendTextLayout? CreateMeasurementLayout(ReadOnlySpan<char> text,
        BackendTextFormat format, in BackendTextLayoutConstraints constraints)
    {
        if (text.IsEmpty) return null;

        if (format.Font is not DirectWriteFont dwFont)
            throw new ArgumentException("Font must be a DirectWriteFont", nameof(format));

        var bounds = constraints.Bounds;
        double maxWidth = double.IsPositiveInfinity(bounds.Width) ? float.MaxValue : Math.Max(0, bounds.Width);

        nint textFormat = 0;
        bool ownFormat = false;
        nint textLayout = 0;
        try
        {
            // Measurement: Left/Top only - alignment applied in render layout.
            if (_textFormatCache != null)
            {
                textFormat = _textFormatCache.GetOrCreate(_dwriteFactory, dwFont,
                    TextAlignment.Left, TextAlignment.Top, format.Wrapping);
            }
            else
            {
                var weight = (DWRITE_FONT_WEIGHT)(int)dwFont.Weight;
                var style = dwFont.IsItalic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL;
                int hr2 = DWriteVTable.CreateTextFormat((IDWriteFactory*)_dwriteFactory, dwFont.Family, dwFont.PrivateFontCollection, weight, style, (float)dwFont.Size, out textFormat);
                if (hr2 < 0 || textFormat == 0) return null;
                DWriteVTable.SetWordWrapping(textFormat,
                    format.Wrapping == TextWrapping.NoWrap ? DWRITE_WORD_WRAPPING.NO_WRAP : DWRITE_WORD_WRAPPING.WRAP);
                ownFormat = true;
            }
            if (textFormat == 0) return null;

            float w = maxWidth >= float.MaxValue ? float.MaxValue : (float)maxWidth;
            int hr = DWriteVTable.CreateGdiCompatibleTextLayout(
                (IDWriteFactory*)_dwriteFactory, text, textFormat, w, float.MaxValue, _pixelsPerDip, useGdiNatural: false, out textLayout);
            if (hr < 0 || textLayout == 0) return null;

            ApplyCustomFontFallback(textLayout);

            hr = DWriteVTable.GetMetrics(textLayout, out var metrics);
            if (hr < 0) return null;

            var height = metrics.height;
            if (metrics.top < 0) height += -metrics.top;

            var measured = new Size(metrics.widthIncludingTrailingWhitespace, height);
            double effectiveMaxWidth = bounds.Width > 0 && !double.IsPositiveInfinity(bounds.Width) ? bounds.Width : measured.Width;

            if (format.Trimming == TextTrimming.CharacterEllipsis)
            {
                DWriteVTable.CreateEllipsisTrimmingSign((IDWriteFactory*)_dwriteFactory, textFormat, out nint trimmingSign);
                var dwriteTrimming = new DWRITE_TRIMMING { granularity = DWRITE_TRIMMING_GRANULARITY.CHARACTER };
                DWriteVTable.SetTrimming(textLayout, dwriteTrimming, trimmingSign);
                ComHelpers.Release(trimmingSign);
            }

            // Measurement only - native layout released immediately. No BackendHandle.
            return new BackendTextLayout
            {
                MeasuredSize = measured,
                EffectiveBounds = bounds,
                EffectiveMaxWidth = effectiveMaxWidth,
                ContentHeight = measured.Height,
            };
        }
        finally
        {
            ComHelpers.Release(textLayout);
            if (ownFormat) ComHelpers.Release(textFormat);
        }
    }

    private void ApplyCustomFontFallback(nint textLayout)
    {
        if (textLayout == 0) return;
        var fallback = DWriteFontFallbackHelper.GetOrCreate((IDWriteFactory*)_dwriteFactory);
        if (fallback == 0) return;
        _ = DWriteTextLayout2VTable.SetFontFallback(textLayout, fallback);
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
        => MeasureText(text, font, double.PositiveInfinity);

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        var format = new BackendTextFormat
        {
            Font = font,
            HorizontalAlignment = TextAlignment.Left,
            VerticalAlignment = TextAlignment.Top,
            Wrapping = TextWrapping.NoWrap,
            Trimming = TextTrimming.None
        };
        var constraints = new BackendTextLayoutConstraints(new Rect(0, 0, double.PositiveInfinity, 0));
        var layout = CreateMeasurementLayout(text, format, in constraints);
        return layout?.MeasuredSize ?? Size.Empty;
    }

    double[] ITextAdvanceSource.GetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font)
    {
        if (text.IsEmpty)
        {
            return [];
        }

        var advances = new double[text.Length];
        FillUtf16PrefixAdvances(text, font, advances);
        return advances;
    }

    bool ITextAdvanceSource.TryGetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font, Span<double> destination)
    {
        if (text.IsEmpty || destination.Length < text.Length)
        {
            return text.IsEmpty;
        }

        FillUtf16PrefixAdvances(text, font, destination);
        return true;
    }

    private void FillUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font, Span<double> result)
    {
        if (font is not DirectWriteFont dwFont)
        {
            throw new ArgumentException("Font must be a DirectWriteFont.", nameof(font));
        }

        nint textFormat = 0;
        nint textLayout = 0;
        bool ownFormat = false;
        try
        {
            if (_textFormatCache is not null)
            {
                textFormat = _textFormatCache.GetOrCreate(
                    _dwriteFactory,
                    dwFont,
                    TextAlignment.Left,
                    TextAlignment.Top,
                    TextWrapping.NoWrap);
            }
            else
            {
                int formatHr = DWriteVTable.CreateTextFormat(
                    (IDWriteFactory*)_dwriteFactory,
                    dwFont.Family,
                    dwFont.PrivateFontCollection,
                    (DWRITE_FONT_WEIGHT)(int)dwFont.Weight,
                    dwFont.IsItalic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL,
                    (float)dwFont.Size,
                    out textFormat);
                if (formatHr < 0 || textFormat == 0)
                {
                    Marshal.ThrowExceptionForHR(formatHr);
                }
                DWriteVTable.SetWordWrapping(textFormat, DWRITE_WORD_WRAPPING.NO_WRAP);
                ownFormat = true;
            }

            int hr = DWriteVTable.CreateGdiCompatibleTextLayout(
                (IDWriteFactory*)_dwriteFactory,
                text,
                textFormat,
                float.MaxValue,
                float.MaxValue,
                _pixelsPerDip,
                useGdiNatural: false,
                out textLayout);
            if (hr < 0 || textLayout == 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            ApplyCustomFontFallback(textLayout);
            var runs = DWriteGlyphRunExtractor.Capture(textLayout);
            foreach (var run in runs)
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
}
