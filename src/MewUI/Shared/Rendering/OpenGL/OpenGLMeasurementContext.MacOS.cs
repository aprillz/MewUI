using Aprillz.MewUI.Rendering.CoreText;

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

        if (font is not CoreTextFont ctFont)
        {
            return;
        }

        int maxWidthPx = maxWidthDip <= 0 ? 0 : (int)Math.Ceiling(maxWidthDip * dpiScale);
        var px = CoreTextText.Measure(ctFont, text, maxWidthPx, wrapping, dpi);
        result = new Size(Math.Ceiling(px.Width) / dpiScale, Math.Ceiling(px.Height) / dpiScale);
        handled = true;
    }
}

