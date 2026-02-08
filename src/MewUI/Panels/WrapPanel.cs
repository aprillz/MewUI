namespace Aprillz.MewUI.Controls;

/// <summary>
/// A panel that arranges children in a flowing layout, wrapping to the next line when needed.
/// </summary>
public class WrapPanel : Panel
{
    /// <summary>
    /// Gets or sets the orientation of the wrap panel.
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
    } = Orientation.Horizontal;

    /// <summary>
    /// Gets or sets the spacing between items and lines.
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

    /// <summary>
    /// Gets or sets a fixed width for all items. NaN means auto-size.
    /// </summary>
    public double ItemWidth
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = double.NaN;

    /// <summary>
    /// Gets or sets a fixed height for all items. NaN means auto-size.
    /// </summary>
    public double ItemHeight
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = double.NaN;

    protected override Size MeasureContent(Size availableSize)
    {
        var paddedSize = availableSize.Deflate(Padding);

        bool horizontal = Orientation == Orientation.Horizontal;
        double lineSize = 0;      // Size of current line (height for horizontal, width for vertical)
        double lineOffset = 0;    // Offset of current line
        double totalMain = 0;     // Total size in main direction
        double totalCross = 0;    // Total size in cross direction

        double maxMain = horizontal ? paddedSize.Width : paddedSize.Height;
        bool hasPrevious = false;

        foreach (var child in Children)
        {
            if (child is UIElement ui && !ui.IsVisible)
            {
                continue;
            }

            // Measure with item constraints if specified
            var measureSize = new Size(
                double.IsNaN(ItemWidth) ? paddedSize.Width : ItemWidth,
                double.IsNaN(ItemHeight) ? paddedSize.Height : ItemHeight
            );
            child.Measure(measureSize);

            double childWidth = double.IsNaN(ItemWidth) ? child.DesiredSize.Width : ItemWidth;
            double childHeight = double.IsNaN(ItemHeight) ? child.DesiredSize.Height : ItemHeight;
            double childMain = horizontal ? childWidth : childHeight;
            double childCross = horizontal ? childHeight : childWidth;

            double spacing = hasPrevious && lineOffset > 0 ? Spacing : 0;

            // Check if we need to wrap
            if (lineOffset + spacing + childMain > maxMain && lineOffset > 0)
            {
                // Wrap to next line
                totalCross += lineSize + Spacing;
                lineSize = childCross;
                lineOffset = childMain;
                totalMain = Math.Max(totalMain, lineOffset);
            }
            else
            {
                // Continue on current line
                lineOffset += spacing + childMain;
                lineSize = Math.Max(lineSize, childCross);
                totalMain = Math.Max(totalMain, lineOffset);
            }

            hasPrevious = true;
        }

        // Add last line
        totalCross += lineSize;

        var contentSize = horizontal
            ? new Size(totalMain, totalCross)
            : new Size(totalCross, totalMain);

        return contentSize.Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var contentBounds = bounds.Deflate(Padding);

        bool horizontal = Orientation == Orientation.Horizontal;
        double lineSize = 0;
        double lineOffset = 0;
        double crossOffset = 0;

        double maxMain = horizontal ? contentBounds.Width : contentBounds.Height;

        // First pass: calculate line sizes
        var lines = new List<(int start, int count, double size)>();
        int lineStart = 0;
        int lineCount = 0;

        for (int i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            if (child is UIElement ui && !ui.IsVisible)
            {
                continue;
            }

            double childWidth = double.IsNaN(ItemWidth) ? child.DesiredSize.Width : ItemWidth;
            double childHeight = double.IsNaN(ItemHeight) ? child.DesiredSize.Height : ItemHeight;
            double childMain = horizontal ? childWidth : childHeight;
            double childCross = horizontal ? childHeight : childWidth;

            double spacing = lineCount > 0 ? Spacing : 0;

            if (lineOffset + spacing + childMain > maxMain && lineCount > 0)
            {
                lines.Add((lineStart, lineCount, lineSize));
                lineStart = i;
                lineCount = 1;
                lineSize = childCross;
                lineOffset = childMain;
            }
            else
            {
                lineCount++;
                lineSize = Math.Max(lineSize, childCross);
                lineOffset += spacing + childMain;
            }
        }

        if (lineCount > 0)
        {
            lines.Add((lineStart, lineCount, lineSize));
        }

        // Second pass: arrange children
        crossOffset = 0;
        foreach (var (start, count, size) in lines)
        {
            double mainOffset = 0;
            int arranged = 0;

            for (int i = start; i < Children.Count && arranged < count; i++)
            {
                var child = Children[i];
                if (child is UIElement ui && !ui.IsVisible)
                {
                    continue;
                }

                double childWidth = double.IsNaN(ItemWidth) ? child.DesiredSize.Width : ItemWidth;
                double childHeight = double.IsNaN(ItemHeight) ? child.DesiredSize.Height : ItemHeight;

                var childRect = horizontal
                    ? new Rect(contentBounds.X + mainOffset, contentBounds.Y + crossOffset, childWidth, childHeight)
                    : new Rect(contentBounds.X + crossOffset, contentBounds.Y + mainOffset, childWidth, childHeight);

                child.Arrange(childRect);
                mainOffset += (horizontal ? childWidth : childHeight) + Spacing;
                arranged++;
            }

            crossOffset += size + Spacing;
        }
    }
}
