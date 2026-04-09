using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls.Text;

/// <summary>
/// Manages the lifecycle of <see cref="TextFormat"/> and <see cref="TextLayout"/>
/// for a single text owner (e.g. <see cref="TextBlock"/>).
/// </summary>
internal sealed class FormattedTextStore
{
    private TextFormat? _format;
    private TextLayout? _layout;
    private Rect _renderBounds;

    public TextFormat? Format => _format;
    public TextLayout? Layout => _layout;

    public void Invalidate()
    {
        _format = null;
        _layout = null;
        _renderBounds = default;
    }

    public void SetFormat(TextFormat format)
    {
        _format = format;
    }

    /// <summary>Measure phase: create layout for sizing. No native handle.</summary>
    public Size Measure(IGraphicsContext ctx, ReadOnlySpan<char> text, in TextLayoutConstraints constraints)
    {
        if (_format == null) return Size.Empty;
        _layout = ctx.CreateTextLayout(text, _format, in constraints);
        _renderBounds = default; // render layout needs refresh
        return _layout?.MeasuredSize ?? Size.Empty;
    }

    /// <summary>Render phase: ensure layout has native handle for actual bounds.</summary>
    public TextLayout? EnsureRenderLayout(IGraphicsContext ctx, ReadOnlySpan<char> text, Rect bounds)
    {
        if (_format == null || _layout == null) return null;
        if (_layout.BackendHandle != 0 && _renderBounds == bounds)
            return _layout;

        var constraints = new TextLayoutConstraints(bounds);
        _layout = ctx.CreateTextLayout(text, _format, in constraints);
        _renderBounds = bounds;
        return _layout;
    }
}
