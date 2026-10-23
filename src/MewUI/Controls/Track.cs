using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Lays a thumb and two paging regions out along an axis for a value in a range, and answers which value a
/// point or a drag distance stands for. A template registers it as a part; the owning control pushes the range
/// and value in and maps the thumb's drags back. It draws nothing itself.
/// </summary>
public sealed class Track : FrameworkElement, IVisualTreeHost
{
    public static readonly MewProperty<double> MinimumProperty =
        MewProperty<double>.Register<Track>(nameof(Minimum), 0.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> MaximumProperty =
        MewProperty<double>.Register<Track>(nameof(Maximum), 1.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> ValueProperty =
        MewProperty<double>.Register<Track>(nameof(Value), 0.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> ViewportSizeProperty =
        MewProperty<double>.Register<Track>(nameof(ViewportSize), double.NaN, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> ThumbLengthProperty =
        MewProperty<double>.Register<Track>(nameof(ThumbLength), double.NaN, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<Orientation> OrientationProperty =
        MewProperty<Orientation>.Register<Track>(nameof(Orientation), Orientation.Horizontal, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<bool> IsDirectionReversedProperty =
        MewProperty<bool>.Register<Track>(nameof(IsDirectionReversed), false, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<Thumb?> ThumbProperty =
        MewProperty<Thumb?>.Register<Track>(nameof(Thumb), null, MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnSlotChanged(oldValue, newValue));

    public static readonly MewProperty<Element?> DecreaseElementProperty =
        MewProperty<Element?>.Register<Track>(nameof(DecreaseElement), null, MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnSlotChanged(oldValue, newValue));

    public static readonly MewProperty<Element?> IncreaseElementProperty =
        MewProperty<Element?>.Register<Track>(nameof(IncreaseElement), null, MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnSlotChanged(oldValue, newValue));

    // Thumbs shorter than this are hard to grab, and a scroll bar never shrinks its thumb below it.
    private const double MIN_THUMB_LENGTH = 8;

    /// <summary>Gets or sets the minimum value.</summary>
    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>Gets or sets the maximum value.</summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Gets or sets the value the thumb is placed for.</summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Gets or sets the viewport size in value units, which sizes the thumb like a scroll bar; NaN keeps the
    /// thumb at <see cref="ThumbLength"/>, as a slider does.
    /// </summary>
    public double ViewportSize
    {
        get => GetValue(ViewportSizeProperty);
        set => SetValue(ViewportSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the thumb's length along the axis; NaN takes the thumb element's desired length.
    /// </summary>
    public double ThumbLength
    {
        get => GetValue(ThumbLengthProperty);
        set => SetValue(ThumbLengthProperty, value);
    }

    /// <summary>Gets or sets the axis the thumb travels along.</summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>Gets or sets whether the minimum sits at the far end, as it does on a vertical slider.</summary>
    public bool IsDirectionReversed
    {
        get => GetValue(IsDirectionReversedProperty);
        set => SetValue(IsDirectionReversedProperty, value);
    }

    /// <summary>Gets or sets the thumb element.</summary>
    public Thumb? Thumb
    {
        get => GetValue(ThumbProperty);
        set => SetValue(ThumbProperty, value);
    }

    /// <summary>Gets or sets the element filling the region between the minimum end and the thumb.</summary>
    public Element? DecreaseElement
    {
        get => GetValue(DecreaseElementProperty);
        set => SetValue(DecreaseElementProperty, value);
    }

    /// <summary>Gets or sets the element filling the region between the thumb and the maximum end.</summary>
    public Element? IncreaseElement
    {
        get => GetValue(IncreaseElementProperty);
        set => SetValue(IncreaseElementProperty, value);
    }

    /// <summary>The value whose thumb would be centered on <paramref name="point"/>, in the space of <see cref="Element.Bounds"/>.</summary>
    public double ValueFromPoint(Point point) => ComputeLayout(Bounds).ValueFromPoint(point);

    /// <summary>The value change a thumb drag of <paramref name="distance"/> along the axis stands for.</summary>
    public double ValueFromDistance(double distance) => ComputeLayout(Bounds).ValueFromDistance(distance);

    private void OnSlotChanged(Element? oldValue, Element? newValue)
    {
        if (ReferenceEquals(newValue, this))
        {
            throw new InvalidOperationException("A track cannot host itself.");
        }

        if (oldValue != null)
        {
            oldValue.Parent = null;
        }

        if (newValue != null)
        {
            newValue.Parent = this;
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        bool vertical = Orientation == Orientation.Vertical;
        double thickness = 0;
        foreach (var slot in Slots())
        {
            slot.Measure(availableSize);
            thickness = Math.Max(thickness, vertical ? slot.DesiredSize.Width : slot.DesiredSize.Height);
        }

        // Along the axis the track takes what it is given; unconstrained, three thumbs' worth is enough to show one.
        double availableLength = vertical ? availableSize.Height : availableSize.Width;
        double length = double.IsPositiveInfinity(availableLength) ? ResolveThumbLength() * 3 : availableLength;
        return vertical ? new Size(thickness, length) : new Size(length, thickness);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var layout = ComputeLayout(bounds);
        DecreaseElement?.Arrange(layout.DecreaseRect);
        IncreaseElement?.Arrange(layout.IncreaseRect);
        Thumb?.Arrange(layout.ThumbRect);
    }

    private RangeTrackLayout ComputeLayout(Rect area)
        => RangeTrackLayout.Compute(
            area, Orientation, IsDirectionReversed, Minimum, Maximum, Value, ViewportSize,
            ResolveThumbLength(), Math.Max(MIN_THUMB_LENGTH, ResolveThumbLength()));

    private double ResolveThumbLength()
    {
        double length = ThumbLength;
        if (!double.IsNaN(length) && length >= 0)
        {
            return length;
        }

        var thumb = Thumb;
        if (thumb == null)
        {
            return 0;
        }

        return Orientation == Orientation.Vertical ? thumb.DesiredSize.Height : thumb.DesiredSize.Width;
    }

    private IEnumerable<Element> Slots()
    {
        if (DecreaseElement != null)
        {
            yield return DecreaseElement;
        }

        if (IncreaseElement != null)
        {
            yield return IncreaseElement;
        }

        if (Thumb != null)
        {
            yield return Thumb;
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        foreach (var slot in Slots())
        {
            slot.Render(context);
        }
    }

    internal override void WriteComposition(Rendering.Retained.CompositionPlanBuilder builder)
    {
        builder.Content(0);
        foreach (var slot in Slots())
        {
            builder.Child(slot);
        }
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!Bounds.Contains(point) || !IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        // The thumb lies over the paging regions, so it is asked first.
        if (Thumb?.HitTest(point) is UIElement hitThumb)
        {
            return hitThumb;
        }

        if (IncreaseElement is UIElement increase && increase.HitTest(point) is UIElement hitIncrease)
        {
            return hitIncrease;
        }

        if (DecreaseElement is UIElement decrease && decrease.HitTest(point) is UIElement hitDecrease)
        {
            return hitDecrease;
        }

        return this;
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        foreach (var slot in Slots())
        {
            if (!visitor(slot))
            {
                return false;
            }
        }

        return true;
    }
}
