namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// Measurement-only graphics context used by OpenGL-based backends when a real rendering context is not available.
/// </summary>
internal sealed partial class OpenGLMeasurementContext : MeasureGraphicsContextBase
{
    private readonly uint _dpi;
    public override double DpiScale { get; }

    public OpenGLMeasurementContext(uint dpi)
    {
        _dpi = dpi == 0 ? 96u : dpi;
        DpiScale = _dpi / 96.0;
    }

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

        bool handled = false;
        var result = Size.Empty;
        TryMeasureTextNative(text, font, _dpi, DpiScale, maxWidthDip: 0, wrapping: TextWrapping.NoWrap, ref handled, ref result);
        if (handled)
        {
            return result;
        }

        double size = font.Size <= 0 ? 12 : font.Size;
        int lines = 1;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines++;
            }
        }

        double lineHeight = size * 1.25;
        double maxLineChars = 0;
        int current = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\r')
            {
                continue;
            }

            if (c == '\n')
            {
                if (current > maxLineChars)
                {
                    maxLineChars = current;
                }

                current = 0;
                continue;
            }
            current++;
        }
        if (current > maxLineChars)
        {
            maxLineChars = current;
        }

        double width = maxLineChars * size * 0.6;
        double height = lines * lineHeight;
        return new Size(width, height);
    }

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
    {
        if (double.IsNaN(maxWidth) || maxWidth <= 0 || double.IsInfinity(maxWidth))
        {
            return MeasureText(text, font);
        }

        bool handled = false;
        var result = Size.Empty;
        TryMeasureTextNative(text, font, _dpi, DpiScale, maxWidth, wrapping: TextWrapping.Wrap, ref handled, ref result);
        if (handled)
        {
            return result;
        }

        var raw = MeasureText(text, font);
        if (raw.Width <= maxWidth)
        {
            return raw;
        }

        double size = font.Size <= 0 ? 12 : font.Size;
        double charsPerLine = Math.Max(1, maxWidth / (size * 0.6));
        int totalChars = 0;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c != '\r' && c != '\n')
            {
                totalChars++;
            }
        }
        double lineCount = Math.Ceiling(totalChars / charsPerLine);
        double height = lineCount * size * 1.25;
        return new Size(maxWidth, height);
    }

    static partial void TryMeasureTextNative(
        ReadOnlySpan<char> text,
        IFont font,
        uint dpi,
        double dpiScale,
        double maxWidthDip,
        TextWrapping wrapping,
        ref bool handled,
        ref Size result);
}
