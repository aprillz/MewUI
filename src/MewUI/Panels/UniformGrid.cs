namespace Aprillz.MewUI.Controls;

/// <summary>
/// A panel that arranges children in a grid with equal-sized cells.
/// </summary>
public class UniformGrid : Panel
{
    /// <summary>
    /// Gets or sets the spacing between cells (both row/column gaps).
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
    /// Gets or sets the number of rows. 0 means auto-calculate.
    /// </summary>
    public int Rows
    {
        get;
        set
        {
            var v = Math.Max(0, value);
            if (Set(ref field, v))
            {
                InvalidateMeasure();
            }
        }
    }

    /// <summary>
    /// Gets or sets the number of columns. 0 means auto-calculate.
    /// </summary>
    public int Columns
    {
        get;
        set
        {
            var v = Math.Max(0, value);
            if (Set(ref field, v))
            {
                InvalidateMeasure();
            }
        }
    }

    private (int rows, int columns) CalculateGridSize()
    {
        int count = 0;
        foreach (var child in Children)
        {
            if (child is UIElement ui && !ui.IsVisible)
            {
                continue;
            }

            count++;
        }
        if (count == 0)
        {
            return (0, 0);
        }

        int rows = Rows;
        int columns = Columns;

        if (rows == 0 && columns == 0)
        {
            // Auto-calculate both: make it roughly square
            columns = (int)Math.Ceiling(Math.Sqrt(count));
            rows = (int)Math.Ceiling((double)count / columns);
        }
        else if (rows == 0)
        {
            rows = (int)Math.Ceiling((double)count / columns);
        }
        else if (columns == 0)
        {
            columns = (int)Math.Ceiling((double)count / rows);
        }

        return (rows, columns);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var paddedSize = availableSize.Deflate(Padding);
        var (rows, columns) = CalculateGridSize();

        if (rows == 0 || columns == 0)
        {
            return Size.Empty;
        }

        double colGaps = columns > 1 ? (columns - 1) * Spacing : 0;
        double rowGaps = rows > 1 ? (rows - 1) * Spacing : 0;

        double cellWidth = Math.Max(0, (paddedSize.Width - colGaps) / columns);
        double cellHeight = Math.Max(0, (paddedSize.Height - rowGaps) / rows);
        var cellSize = new Size(cellWidth, cellHeight);

        double maxChildWidth = 0;
        double maxChildHeight = 0;

        foreach (var child in Children)
        {
            if (child is UIElement ui && !ui.IsVisible)
            {
                continue;
            }

            child.Measure(cellSize);
            maxChildWidth = Math.Max(maxChildWidth, child.DesiredSize.Width);
            maxChildHeight = Math.Max(maxChildHeight, child.DesiredSize.Height);
        }

        double totalWidth = maxChildWidth * columns + colGaps;
        double totalHeight = maxChildHeight * rows + rowGaps;
        return new Size(totalWidth, totalHeight).Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var contentBounds = bounds.Deflate(Padding);
        var (rows, columns) = CalculateGridSize();

        if (rows == 0 || columns == 0)
        {
            return;
        }

        double colGaps = columns > 1 ? (columns - 1) * Spacing : 0;
        double rowGaps = rows > 1 ? (rows - 1) * Spacing : 0;

        double cellWidth = Math.Max(0, (contentBounds.Width - colGaps) / columns);
        double cellHeight = Math.Max(0, (contentBounds.Height - rowGaps) / rows);

        int index = 0;
        for (int row = 0; row < rows && index < Children.Count; row++)
        {
            for (int col = 0; col < columns && index < Children.Count; col++)
            {
                Element? child = null;
                while (index < Children.Count)
                {
                    var candidate = Children[index++];
                    if (candidate is UIElement ui && !ui.IsVisible)
                    {
                        continue;
                    }

                    child = candidate;
                    break;
                }

                if (child == null)
                {
                    return;
                }

                child.Arrange(new Rect(
                    contentBounds.X + col * (cellWidth + Spacing),
                    contentBounds.Y + row * (cellHeight + Spacing),
                    cellWidth,
                    cellHeight));
            }
        }
    }
}
