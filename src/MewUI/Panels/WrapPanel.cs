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
        var measuredMain = horizontal ? paddedSize.Width : paddedSize.Height;
        measuredMain = RoundMainToPixels(measuredMain);

        var items = CollectVisibleChildren(paddedSize, measureChildren: true);
        var lines = BuildLines(items, measuredMain);

        double totalMain = 0;
        double totalCross = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            totalMain = Math.Max(totalMain, lines[i].main);
            totalCross += lines[i].size;
            if (i < lines.Count - 1)
            {
                totalCross += Spacing;
            }
        }

        var contentSize = horizontal
            ? new Size(totalMain, totalCross)
            : new Size(totalCross, totalMain);

        return contentSize.Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var contentBounds = bounds.Deflate(Padding);

        bool horizontal = Orientation == Orientation.Horizontal;
        var arrangedMain = horizontal ? contentBounds.Width : contentBounds.Height;
        arrangedMain = RoundMainToPixels(arrangedMain);
        var items = CollectVisibleChildren(contentBounds.Size, measureChildren: false);
        var lines = BuildLines(items, arrangedMain);

        double totalMain = 0;
        double totalCross = 0;
        for (int i = 0; i < lines.Count; i++)
        {
            totalMain = Math.Max(totalMain, lines[i].main);
            totalCross += lines[i].size;
            if (i < lines.Count - 1)
            {
                totalCross += Spacing;
            }
        }

        double crossOffset = 0;
        for (int lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            double mainOffset = 0;

            for (int i = 0; i < line.count; i++)
            {
                var item = items[line.start + i];
                var child = item.child;

                var childRect = horizontal
                    ? new Rect(contentBounds.X + mainOffset, contentBounds.Y + crossOffset, item.width, item.height)
                    : new Rect(contentBounds.X + crossOffset, contentBounds.Y + mainOffset, item.width, item.height);

                child.Arrange(childRect);
                mainOffset += item.main;
                if (i < line.count - 1)
                {
                    mainOffset += Spacing;
                }
            }

            crossOffset += line.size;
            if (lineIndex < lines.Count - 1)
            {
                crossOffset += Spacing;
            }
        }
    }

    private List<ChildInfo> CollectVisibleChildren(Size constraintSize, bool measureChildren)
    {
        var items = new List<ChildInfo>(Children.Count);
        foreach (var child in Children)
        {
            if (child is UIElement ui && !ui.IsVisible)
            {
                continue;
            }

            if (measureChildren)
            {
                var measureSize = new Size(
                    double.IsNaN(ItemWidth) ? constraintSize.Width : ItemWidth,
                    double.IsNaN(ItemHeight) ? constraintSize.Height : ItemHeight
                );
                child.Measure(measureSize);
            }

            double width = double.IsNaN(ItemWidth) ? child.DesiredSize.Width : ItemWidth;
            double height = double.IsNaN(ItemHeight) ? child.DesiredSize.Height : ItemHeight;
            double main = Orientation == Orientation.Horizontal ? width : height;
            double cross = Orientation == Orientation.Horizontal ? height : width;

            items.Add(new ChildInfo(child, width, height, main, cross));
        }

        return items;
    }

    private List<LineInfo> BuildLines(List<ChildInfo> items, double maxMain)
    {
        var lines = new List<LineInfo>();
        double lineSize = 0;
        double lineOffset = 0;
        int lineStart = 0;
        int lineCount = 0;

        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            double addSpacing = lineCount > 0 ? Spacing : 0;
            double nextOffset = lineOffset + addSpacing + item.main;

            if (lineCount > 0 && nextOffset > maxMain)
            {
                lines.Add(new LineInfo(lineStart, lineCount, lineSize, lineOffset));
                lineStart = i;
                lineCount = 1;
                lineSize = item.cross;
                lineOffset = item.main;
            }
            else
            {
                lineOffset = nextOffset;
                lineCount++;
                lineSize = Math.Max(lineSize, item.cross);
            }
        }

        if (lineCount > 0)
        {
            lines.Add(new LineInfo(lineStart, lineCount, lineSize, lineOffset));
        }

        return lines;
    }

    private double RoundMainToPixels(double value)
    {
        var dpiScale = GetDpi() / 96.0;
        if (dpiScale <= 0 || double.IsNaN(dpiScale) || double.IsInfinity(dpiScale))
        {
            return value;
        }

        return LayoutRounding.RoundToPixel(value, dpiScale);
    }

    private readonly record struct ChildInfo(Element child, double width, double height, double main, double cross);

    private readonly record struct LineInfo(int start, int count, double size, double main);
}
