using Aprillz.MewUI.Rendering.FreeType;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed partial class OpenGLMeasurementContext
{
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
