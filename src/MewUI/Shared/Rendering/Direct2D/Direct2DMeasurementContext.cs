using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed unsafe class Direct2DMeasurementContext : MeasureGraphicsContextBase
{
    private readonly nint _dwriteFactory;

    public override double DpiScale => 1.0;

    public Direct2DMeasurementContext(nint dwriteFactory) => _dwriteFactory = dwriteFactory;

    public override TextLayout CreateTextLayout(ReadOnlySpan<char> text,
        TextFormat format, in TextLayoutConstraints constraints)
    {
        if (text.IsEmpty) return default;

        if (format.Font is not DirectWriteFont dwFont)
            throw new ArgumentException("Font must be a DirectWriteFont", nameof(format));

        var bounds = constraints.Bounds;
        double maxWidth = double.IsPositiveInfinity(bounds.Width) ? float.MaxValue : Math.Max(0, bounds.Width);

        nint textFormat = 0;
        nint textLayout = 0;
        try
        {
            var weight = (DWRITE_FONT_WEIGHT)(int)dwFont.Weight;
            var style = dwFont.IsItalic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL;
            int hr = DWriteVTable.CreateTextFormat((IDWriteFactory*)_dwriteFactory, dwFont.Family, dwFont.PrivateFontCollection, weight, style, (float)dwFont.Size, out textFormat);
            if (hr < 0 || textFormat == 0) return default;

            DWriteVTable.SetWordWrapping(textFormat,
                format.Wrapping == TextWrapping.NoWrap ? DWRITE_WORD_WRAPPING.NO_WRAP : DWRITE_WORD_WRAPPING.WRAP);

            float w = maxWidth >= float.MaxValue ? float.MaxValue : (float)maxWidth;
            hr = DWriteVTable.CreateTextLayout((IDWriteFactory*)_dwriteFactory, text, textFormat, w, float.MaxValue, out textLayout);
            if (hr < 0 || textLayout == 0) return default;

            hr = DWriteVTable.GetMetrics(textLayout, out var metrics);
            if (hr < 0) return default;

            var height = metrics.height;
            if (metrics.top < 0) height += -metrics.top;

            var measured = new Size(TextMeasurePolicy.ApplyWidthPadding(metrics.widthIncludingTrailingWhitespace), height);
            double effectiveMaxWidth = bounds.Width > 0 && !double.IsPositiveInfinity(bounds.Width) ? bounds.Width : measured.Width;

            // Apply trimming if requested.
            if (format.Trimming == TextTrimming.CharacterEllipsis)
            {
                DWriteVTable.CreateEllipsisTrimmingSign((IDWriteFactory*)_dwriteFactory, textFormat, out nint trimmingSign);
                var dwriteTrimming = new DWRITE_TRIMMING { granularity = DWRITE_TRIMMING_GRANULARITY.CHARACTER };
                DWriteVTable.SetTrimming(textLayout, dwriteTrimming, trimmingSign);
                ComHelpers.Release(trimmingSign);
            }

            // Keep native layout alive — caller owns it via TextLayout.NativeHandle.
            var result = new TextLayout
            {
                MeasuredSize = measured,
                EffectiveBounds = bounds,
                EffectiveMaxWidth = effectiveMaxWidth,
                ContentHeight = measured.Height,
                NativeHandle = textLayout
            };
            textLayout = 0; // prevent release in finally
            TextTracker?.TrackLayout(result);
            return result;
        }
        finally
        {
            ComHelpers.Release(textLayout);
            ComHelpers.Release(textFormat);
        }
    }

    public void ReleaseTextLayout(TextLayout layout)
    {
        if (layout == null) return;
        if (layout.NativeHandle != 0)
        {
            ComHelpers.Release(layout.NativeHandle);
            layout.NativeHandle = 0;
        }
    }

    public void ReleaseTextFormat(TextFormat format)
    {
        if (format == null) return;
        if (format.NativeHandle != 0)
        {
            ComHelpers.Release(format.NativeHandle);
            format.NativeHandle = 0;
        }
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
    {
        return MeasureText(text, font, double.PositiveInfinity);
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        var format = CreateTextFormat(font, TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap, TextTrimming.None);
        try
        {
            var constraints = new TextLayoutConstraints(new Rect(0, 0, double.PositiveInfinity, 0));
            var layout = CreateTextLayout(text, format, in constraints);
            try
            {
                var size = layout?.MeasuredSize ?? Size.Empty;
                return size;
            }
            finally
            {

                ReleaseTextLayout(layout);
                TextLayout.Deatch(ref layout);
            }
        }
        finally
        {
            ReleaseTextFormat(format);
            TextFormat.Deatch(ref format);
        }
    }
}
