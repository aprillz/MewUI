namespace Aprillz.MewUI.Controls;

/// <summary>
/// Geometry of a value track: where the thumb and the two paging regions sit for a value in a range, and
/// the value a point or a drag distance stands for. The drawn ScrollBar and <see cref="Track"/> share it,
/// so a template part and the built-in drawing agree.
/// </summary>
internal readonly struct RangeTrackLayout
{
    private readonly Orientation _orientation;
    private readonly bool _isDirectionReversed;
    private readonly double _minimum;
    private readonly double _maximum;

    private RangeTrackLayout(
        Rect trackRect, Rect thumbRect, Rect decreaseRect, Rect increaseRect, double thumbLength, double usableLength,
        Orientation orientation, bool isDirectionReversed, double minimum, double maximum)
    {
        TrackRect = trackRect;
        ThumbRect = thumbRect;
        DecreaseRect = decreaseRect;
        IncreaseRect = increaseRect;
        ThumbLength = thumbLength;
        UsableLength = usableLength;
        _orientation = orientation;
        _isDirectionReversed = isDirectionReversed;
        _minimum = minimum;
        _maximum = maximum;
    }

    /// <summary>The whole track along the axis.</summary>
    public Rect TrackRect { get; }

    /// <summary>Where the thumb sits for the value; empty when the track is too short to show one.</summary>
    public Rect ThumbRect { get; }

    /// <summary>The region between the minimum end and the thumb.</summary>
    public Rect DecreaseRect { get; }

    /// <summary>The region between the thumb and the maximum end.</summary>
    public Rect IncreaseRect { get; }

    /// <summary>The thumb's length along the axis.</summary>
    public double ThumbLength { get; }

    /// <summary>How far the thumb can travel: the track length less the thumb length, never under 1.</summary>
    public double UsableLength { get; }

    /// <summary>
    /// Lays the track out for a value. A finite <paramref name="viewportSize"/> sizes the thumb like a scroll bar,
    /// from the viewport's share of the range and never under <paramref name="minThumbLength"/>; NaN keeps the
    /// given <paramref name="thumbLength"/>, as a slider does. <paramref name="isDirectionReversed"/> puts the
    /// minimum at the far end, where a vertical slider has it.
    /// </summary>
    public static RangeTrackLayout Compute(
        Rect area,
        Orientation orientation,
        bool isDirectionReversed,
        double minimum,
        double maximum,
        double value,
        double viewportSize,
        double thumbLength,
        double minThumbLength)
    {
        bool vertical = orientation == Orientation.Vertical;
        double length = vertical ? area.Height : area.Width;
        double thickness = vertical ? area.Width : area.Height;
        double min = Math.Min(minimum, maximum);
        double max = Math.Max(minimum, maximum);
        double range = Math.Max(0, max - min);

        bool scrollBarMode = !double.IsNaN(viewportSize);
        if (scrollBarMode)
        {
            double viewport = Math.Max(0, viewportSize);
            minThumbLength = Math.Max(0, minThumbLength);
            if (length < minThumbLength || thickness <= 0)
            {
                return new RangeTrackLayout(area, Rect.Empty, area, Rect.Empty, 0, Math.Max(1, length),
                    orientation, isDirectionReversed, min, max);
            }

            // Without a viewport the thumb takes a quarter of the track, so an unsized bar still shows one.
            double ratio = viewport > 0 && range + viewport > 0 ? viewport / (range + viewport) : 0.25;
            thumbLength = Math.Clamp(length * ratio, minThumbLength, length);
        }
        else
        {
            thumbLength = Math.Clamp(double.IsNaN(thumbLength) ? 0 : thumbLength, 0, Math.Max(0, length));
        }

        double usable = Math.Max(1, length - thumbLength);
        double fraction = range <= 0 ? 0 : Math.Clamp((value - min) / range, 0, 1);
        if (isDirectionReversed)
        {
            fraction = 1 - fraction;
        }

        double offset = usable * fraction;
        Rect thumb;
        Rect before;
        Rect after;
        if (vertical)
        {
            thumb = new Rect(area.X, area.Y + offset, thickness, thumbLength);
            before = new Rect(area.X, area.Y, thickness, offset);
            after = new Rect(area.X, thumb.Bottom, thickness, Math.Max(0, area.Bottom - thumb.Bottom));
        }
        else
        {
            thumb = new Rect(area.X + offset, area.Y, thumbLength, thickness);
            before = new Rect(area.X, area.Y, offset, thickness);
            after = new Rect(thumb.Right, area.Y, Math.Max(0, area.Right - thumb.Right), thickness);
        }

        // The decrease region is the one toward the minimum, whichever end that is.
        Rect decrease = isDirectionReversed ? after : before;
        Rect increase = isDirectionReversed ? before : after;
        return new RangeTrackLayout(area, thumb, decrease, increase, thumbLength, usable,
            orientation, isDirectionReversed, min, max);
    }

    /// <summary>The value whose thumb would be centered on <paramref name="point"/>, clamped to the range.</summary>
    public double ValueFromPoint(Point point)
    {
        bool vertical = _orientation == Orientation.Vertical;
        double position = vertical ? point.Y : point.X;
        double start = vertical ? TrackRect.Y : TrackRect.X;
        double fraction = Math.Clamp((position - start - ThumbLength / 2) / UsableLength, 0, 1);
        if (_isDirectionReversed)
        {
            fraction = 1 - fraction;
        }

        return _minimum + (_maximum - _minimum) * fraction;
    }

    /// <summary>The value change a thumb drag of <paramref name="distance"/> along the axis stands for.</summary>
    public double ValueFromDistance(double distance)
    {
        double range = _maximum - _minimum;
        double change = range <= 0 ? 0 : distance / UsableLength * range;
        return _isDirectionReversed ? -change : change;
    }
}
