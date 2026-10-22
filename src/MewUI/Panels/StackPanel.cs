namespace Aprillz.MewUI.Controls;

/// <summary>
/// A panel that arranges children in a stack.
/// </summary>
public class StackPanel : Panel
{
    /// <summary>The axis along which children are stacked; the default is <see cref="Orientation.Vertical"/>.</summary>
    public static readonly MewProperty<Orientation> OrientationProperty =
        MewProperty<Orientation>.Register<StackPanel>(nameof(Orientation), Orientation.Vertical, MewPropertyOptions.AffectsLayout);

    /// <summary>The spacing between children in DIPs.</summary>
    public static readonly MewProperty<double> SpacingProperty =
        MewProperty<double>.Register<StackPanel>(nameof(Spacing), 0.0, MewPropertyOptions.AffectsLayout);

    /// <summary>
    /// Gets or sets the axis along which children are stacked; the default is <see cref="Orientation.Vertical"/>.
    /// </summary>
    public Orientation Orientation
    {
        get => GetValue(OrientationProperty);
        set => SetValue(OrientationProperty, value);
    }

    /// <summary>
    /// Gets or sets the spacing between children in DIPs.
    /// </summary>
    public double Spacing
    {
        get => GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        double usedMain = 0;
        double maxCross = 0;

        var paddedSize = availableSize.Deflate(Padding);
        bool hasPrevious = false;

        foreach (var child in ChildrenList)
        {
            if (child is UIElement ui && !ui.IsVisible)
            {
                continue;
            }

            if (hasPrevious)
            {
                usedMain += Spacing;
            }

            if (Orientation == Orientation.Vertical)
            {
                child.Measure(new Size(paddedSize.Width, double.PositiveInfinity));
                usedMain += child.DesiredSize.Height;
                maxCross = Math.Max(maxCross, child.DesiredSize.Width);
            }
            else
            {
                var widthConstraint = double.IsPositiveInfinity(paddedSize.Width)
                    ? double.PositiveInfinity
                    : paddedSize.Width;
                child.Measure(new Size(widthConstraint, paddedSize.Height));
                usedMain += child.DesiredSize.Width;
                maxCross = Math.Max(maxCross, child.DesiredSize.Height);
            }

            hasPrevious = true;
        }

        var contentSize = Orientation == Orientation.Vertical
            ? new Size(maxCross, usedMain)
            : new Size(usedMain, maxCross);

        return contentSize.Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var contentBounds = bounds.Deflate(Padding);
        double offset = 0;
        bool hasPrevious = false;

        foreach (var child in ChildrenList)
        {
            if (child is UIElement ui && !ui.IsVisible)
            {
                continue;
            }

            if (hasPrevious)
            {
                offset += Spacing;
            }

            if (Orientation == Orientation.Vertical)
            {
                var childHeight = child.DesiredSize.Height;
                child.Arrange(new Rect(
                    contentBounds.X,
                    contentBounds.Y + offset,
                    contentBounds.Width,
                    childHeight));
                offset += childHeight;
            }
            else
            {
                var childWidth = child.DesiredSize.Width;
                child.Arrange(new Rect(
                    contentBounds.X + offset,
                    contentBounds.Y,
                    childWidth,
                    contentBounds.Height));
                offset += childWidth;
            }

            hasPrevious = true;
        }
    }
}
