using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.DirectWrite;

namespace Aprillz.MewUI.Rendering.Direct2D;

internal sealed unsafe class Direct2DMeasurementContext : MeasureGraphicsContextBase
{
    private readonly nint _dwriteFactory;

    public override double DpiScale => 1.0;

    public Direct2DMeasurementContext(nint dwriteFactory) => _dwriteFactory = dwriteFactory;

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font) => MeasureText(text, font, float.MaxValue);

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        if (font is not DirectWriteFont dwFont)
        {
            throw new ArgumentException("Font must be a DirectWriteFont", nameof(font));
        }

        nint textFormat = 0;
        nint textLayout = 0;
        try
        {
            var weight = (DWRITE_FONT_WEIGHT)(int)dwFont.Weight;
            var style = dwFont.IsItalic ? DWRITE_FONT_STYLE.ITALIC : DWRITE_FONT_STYLE.NORMAL;
            int hr = DWriteVTable.CreateTextFormat((IDWriteFactory*)_dwriteFactory, dwFont.Family, weight, style, (float)dwFont.Size, out textFormat);
            if (hr < 0 || textFormat == 0)
            {
                return Size.Empty;
            }

            DWriteVTable.SetWordWrapping(textFormat, DWRITE_WORD_WRAPPING.WRAP);

            float w = maxWidth >= float.MaxValue ? float.MaxValue : (float)Math.Max(0, maxWidth);
            hr = DWriteVTable.CreateTextLayout((IDWriteFactory*)_dwriteFactory, text, textFormat, w, float.MaxValue, out textLayout);
            if (hr < 0 || textLayout == 0)
            {
                return Size.Empty;
            }

            hr = DWriteVTable.GetMetrics(textLayout, out var metrics);
            if (hr < 0)
            {
                return Size.Empty;
            }

            var height = metrics.height;
            if (metrics.top < 0)
            {
                height += -metrics.top;
            }

            return new Size(metrics.widthIncludingTrailingWhitespace, height);
        }
        finally
        {
            ComHelpers.Release(textLayout);
            ComHelpers.Release(textFormat);
        }
    }
}
