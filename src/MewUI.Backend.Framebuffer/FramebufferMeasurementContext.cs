namespace Aprillz.MewUI.Rendering.Framebuffer;

internal sealed class FramebufferMeasurementContext : MeasureGraphicsContextBase
{
    private readonly uint _dpi;

    public FramebufferMeasurementContext(uint dpi)
    {
        _dpi = dpi == 0 ? 96 : dpi;
    }

    public override double DpiScale => _dpi / 96.0;

    public override TextLayout? CreateTextLayout(ReadOnlySpan<char> text, TextFormat format, in TextLayoutConstraints constraints)
        => FramebufferText.CreateLayout(text, format, constraints);

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font)
        => FramebufferText.MeasureText(text, font);

    public override Size MeasureText(ReadOnlySpan<char> text, IFont font, double maxWidth)
        => FramebufferText.MeasureText(text, font, maxWidth);
}
