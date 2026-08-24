using Aprillz.MewUI.Rendering.FreeType;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed partial class OpenGLMeasurementContext : ITextAdvanceSource
{
    bool ITextAdvanceSource.TryGetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font, Span<double> destination)
    {
        if (text.IsEmpty || font is not FreeTypeFont ftFont || destination.Length < text.Length)
        {
            return false;
        }

        FreeTypeText.FillUtf16PrefixAdvancesPx(text, ftFont, destination);
        double scale = DpiScale;
        for (int index = 0; index < text.Length; index++)
        {
            destination[index] /= scale;
        }
        return true;
    }

    double[] ITextAdvanceSource.GetUtf16PrefixAdvances(ReadOnlySpan<char> text, IFont font)
    {
        if (!text.IsEmpty && font is FreeTypeFont ftFont)
        {
            var advances = FreeTypeText.GetUtf16PrefixAdvancesPx(text, ftFont);
            double scale = DpiScale;
            for (int index = 0; index < advances.Length; index++)
            {
                advances[index] /= scale;
            }
            return advances;
        }

        // Mirrors the approximate MeasureText fallback for non-FreeType fonts.
        double charWidth = (font.Size <= 0 ? 12 : font.Size) * 0.6;
        var fallback = new double[text.Length];
        for (int index = 0; index < fallback.Length; index++)
        {
            fallback[index] = (index + 1) * charWidth;
        }
        return fallback;
    }

    static partial void TryMeasureTextNative(
        ReadOnlySpan<char> text,
        IFont font,
        uint dpi,
        double dpiScale,
        double maxWidthDip,
        TextWrapping wrapping,
        ref bool handled,
        ref Size result)
    {
        if (handled)
        {
            return;
        }

        if (font is FreeTypeFont ftFont)
        {
            int maxWidthPx = maxWidthDip <= 0
                ? 0
                : Math.Max(1, (int)Math.Ceiling(maxWidthDip * dpiScale));

            var px = FreeTypeText.Measure(text, ftFont, maxWidthPx, wrapping);
            result = new Size(px.Width / dpiScale, px.Height / dpiScale);
            handled = true;
        }
    }
}
