using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed unsafe class Direct2DMeasurementContext : MeasureGraphicsContextBase
{
    private readonly nint _dwriteFactory;
    private readonly DWriteTextFormatCache? _textFormatCache;

    public override double DpiScale => 1.0;

    public Direct2DMeasurementContext(nint dwriteFactory, DWriteTextFormatCache? textFormatCache = null)
    {
        _dwriteFactory = dwriteFactory;
        _textFormatCache = textFormatCache;
    }

    public override TextLayout? CreateTextLayout(ReadOnlySpan<char> text,
        TextFormat format, in TextLayoutConstraints constraints)
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
            // Measurement: Left/Top only — alignment applied in render layout.
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
            int hr = DWriteVTable.CreateTextLayout((IDWriteFactory*)_dwriteFactory, text, textFormat, w, float.MaxValue, out textLayout);
            if (hr < 0 || textLayout == 0) return null;

            ApplyCustomFontFallback(textLayout);

            hr = DWriteVTable.GetMetrics(textLayout, out var metrics);
            if (hr < 0) return null;

            var height = metrics.height;
            if (metrics.top < 0) height += -metrics.top;

            var measured = new Size(TextMeasurePolicy.ApplyWidthPadding(metrics.widthIncludingTrailingWhitespace), height);
            double effectiveMaxWidth = bounds.Width > 0 && !double.IsPositiveInfinity(bounds.Width) ? bounds.Width : measured.Width;

            if (format.Trimming == TextTrimming.CharacterEllipsis)
            {
                DWriteVTable.CreateEllipsisTrimmingSign((IDWriteFactory*)_dwriteFactory, textFormat, out nint trimmingSign);
                var dwriteTrimming = new DWRITE_TRIMMING { granularity = DWRITE_TRIMMING_GRANULARITY.CHARACTER };
                DWriteVTable.SetTrimming(textLayout, dwriteTrimming, trimmingSign);
                ComHelpers.Release(trimmingSign);
            }

            // Measurement only — native layout released immediately. No BackendHandle.
            return new TextLayout
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
        var format = new TextFormat
        {
            Font = font,
            HorizontalAlignment = TextAlignment.Left,
            VerticalAlignment = TextAlignment.Top,
            Wrapping = TextWrapping.NoWrap,
            Trimming = TextTrimming.None
        };
        var constraints = new TextLayoutConstraints(new Rect(0, 0, double.PositiveInfinity, 0));
        var layout = CreateTextLayout(text, format, in constraints);
        return layout?.MeasuredSize ?? Size.Empty;
    }
}
