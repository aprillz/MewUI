using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls.Text;

public struct TextMeasureCache
{
    private Key _lastKey;
    private Size _lastMeasuredSize;
    private bool _hasValue;

    private readonly record struct Key(
        string Text,
        string Family,
        double Size,
        FontWeight Weight,
        TextWrapping Wrapping,
        double MaxWidthDip,
        uint Dpi);

    public void Invalidate()
    {
        _lastKey = default;
        _lastMeasuredSize = default;
        _hasValue = false;
    }

    public Size Measure(
        IGraphicsFactory factory,
        uint dpi,
        IFont font,
        string text,
        TextWrapping wrapping,
        double maxWidthDip)
    {
        if (string.IsNullOrEmpty(text))
        {
            return Size.Empty;
        }

        var key = new Key(
            text,
            font.Family,
            font.Size,
            font.Weight,
            wrapping,
            wrapping == TextWrapping.NoWrap ? 0 : maxWidthDip,
            dpi);

        if (_hasValue && key == _lastKey)
        {
            return _lastMeasuredSize;
        }

        using var ctx = factory.CreateMeasurementContext(dpi);
        _lastMeasuredSize = wrapping == TextWrapping.NoWrap
            ? ctx.MeasureText(text, font)
            : ctx.MeasureText(text, font, maxWidthDip);

        _lastKey = key;
        _hasValue = true;
        return _lastMeasuredSize;
    }
}

