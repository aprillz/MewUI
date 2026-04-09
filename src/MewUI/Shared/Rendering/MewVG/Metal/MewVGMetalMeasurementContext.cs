using Aprillz.MewUI.Rendering.CoreText;

namespace Aprillz.MewUI.Rendering.MewVG;

internal sealed class MewVGMetalMeasurementContext : MeasureGraphicsContextBase
{
    private readonly uint _dpi;

    public MewVGMetalMeasurementContext(uint dpi)
    {
        _dpi = dpi == 0 ? 96u : dpi;
    }

    public override double DpiScale => _dpi / 96.0;

    public override TextLayout CreateTextLayout(ReadOnlySpan<char> text,
        TextFormat format, in TextLayoutConstraints constraints)
    {
        var bounds = constraints.Bounds;
        double maxWidth = double.IsPositiveInfinity(bounds.Width) ? 0 : bounds.Width;
        Size measured = format.Wrapping == TextWrapping.NoWrap
            ? MeasureText(text, format.Font)
            : MeasureText(text, format.Font, maxWidth > 0 ? maxWidth : MeasureText(text, format.Font).Width);
        double effectiveMaxWidth = maxWidth > 0 ? maxWidth : measured.Width;
        return new TextLayout
        {
            MeasuredSize = measured,
            EffectiveBounds = bounds,
            EffectiveMaxWidth = effectiveMaxWidth,
            ContentHeight = measured.Height
        };
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        if (font is not CoreTextFont ct)
        {
            return new Size(text.Length * 8, 16);
        }

        var sizePx = CoreTextText.Measure(ct, text, maxWidthPx: 0, TextWrapping.NoWrap, _dpi);
        return new Size(sizePx.Width / DpiScale, sizePx.Height / DpiScale);
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        if (font is not CoreTextFont ct)
        {
            return new Size(text.Length * 8, 16);
        }

        int maxWidthPx = maxWidth <= 0 ? 0 : Math.Max(1, LayoutRounding.CeilToPixelInt(maxWidth, DpiScale));
        var sizePx = CoreTextText.Measure(ct, text, maxWidthPx, TextWrapping.Wrap, _dpi);
        return new Size(sizePx.Width / DpiScale, sizePx.Height / DpiScale);
    }
}
