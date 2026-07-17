using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls.Text;

/// <summary>
/// Manages the lifecycle of <see cref="TextFormat"/> and <see cref="TextLayout"/>
/// for a single text owner (e.g. <see cref="TextBlock"/>).
/// </summary>
internal sealed class FormattedTextStore
{
    private TextFormat? _format;
    private Size _measuredSize;
    private TextLayout? _renderLayout;
    private double _renderWidth;
    private double _renderHeight;
    private double _measureConstraintWidth;
    private double _measureConstraintHeight;
    private bool _hasMeasuredSize;
    private bool _dirty = true;

    public TextFormat? Format => _format;
    public TextLayout? Layout => _renderLayout;
    public Size MeasuredSize => _measuredSize;

    /// <summary>
    /// Invalidates everything including format. Use when font, alignment, wrapping, or trimming changes.
    /// </summary>
    public void Invalidate()
    {
        _format = null;
        InvalidateLayout();
    }

    /// <summary>
    /// Invalidates layout only, keeping the current format. Use when only text content changes.
    /// </summary>
    public void InvalidateLayout()
    {
        _renderLayout = null;
        _measuredSize = Size.Empty;
        _renderWidth = 0;
        _renderHeight = 0;
        _hasMeasuredSize = false;
        _dirty = true;
    }

    public void SetFormat(TextFormat format)
    {
        ArgumentNullException.ThrowIfNull(format);

        var previous = _format;
        if (previous != null
            && ReferenceEquals(previous.Font, format.Font)
            && previous.HorizontalAlignment == format.HorizontalAlignment
            && previous.VerticalAlignment == format.VerticalAlignment
            && previous.Wrapping == format.Wrapping
            && previous.Trimming == format.Trimming)
        {
            _format = format;
            return;
        }

        _format = format;
        InvalidateLayout();
    }

    public bool TryGetMeasuredSize(in TextLayoutConstraints constraints, out Size measuredSize)
    {
        if (_hasMeasuredSize &&
            _measureConstraintWidth == constraints.Bounds.Width &&
            _measureConstraintHeight == constraints.Bounds.Height)
        {
            measuredSize = _measuredSize;
            return true;
        }

        measuredSize = Size.Empty;
        return false;
    }

    /// <summary>Measure phase: compute size only. Native layout created and released immediately by MeasurementContext.</summary>
    public Size Measure(IGraphicsContext ctx, ReadOnlySpan<char> text, in TextLayoutConstraints constraints)
    {
        if (_format == null) return Size.Empty;

        // Skip re-measurement if constraints haven't changed since last measure
        if (TryGetMeasuredSize(in constraints, out var measuredSize))
            return measuredSize;

        var layout = ctx.CreateTextLayout(text, _format, in constraints);
        _measuredSize = layout?.MeasuredSize ?? Size.Empty;
        _measureConstraintWidth = constraints.Bounds.Width;
        _measureConstraintHeight = constraints.Bounds.Height;
        _hasMeasuredSize = true;
        return _measuredSize;
    }

    /// <summary>Render phase: ensure layout with native handle for actual bounds. Cached until dirty or size changes.</summary>
    public TextLayout? EnsureRenderLayout(IGraphicsContext ctx, ReadOnlySpan<char> text, Rect bounds)
    {
        if (_format == null) return null;
        if (!_dirty && _renderLayout != null &&
            _renderWidth == bounds.Width && _renderHeight == bounds.Height)
            return _renderLayout;

        var constraints = new TextLayoutConstraints(bounds);
        _renderLayout = ctx.CreateTextLayout(text, _format, in constraints);
        _renderWidth = bounds.Width;
        _renderHeight = bounds.Height;
        _dirty = false;
        return _renderLayout;
    }
}
