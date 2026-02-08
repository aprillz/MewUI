namespace Aprillz.MewUI.Controls;

/// <summary>
/// A panel that arranges children in a stack.
/// </summary>
public class StackPanel : Panel
{
    /// <summary>
    /// Gets or sets the orientation of the stack.
    /// </summary>
    public Orientation Orientation
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = Orientation.Vertical;

    /// <summary>
    /// Gets or sets the spacing between children.
    /// </summary>
    public double Spacing
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        double usedMain = 0;
        double maxCross = 0;

        var paddedSize = availableSize.Deflate(Padding);
        bool hasPrevious = false;

        foreach (var child in Children)
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
                child.Measure(new Size(double.PositiveInfinity, paddedSize.Height));
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

        foreach (var child in Children)
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
