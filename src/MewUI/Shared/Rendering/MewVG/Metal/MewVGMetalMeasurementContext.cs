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
