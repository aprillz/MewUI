using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Grid unit type for row/column sizing.
/// </summary>
public enum GridUnitType
{
    /// <summary>Size to content.</summary>
    Auto,
    /// <summary>Fixed pixel size.</summary>
    Pixel,
    /// <summary>Proportional size (star sizing).</summary>
    Star
}

/// <summary>
/// Represents a grid length value.
/// </summary>
public readonly struct GridLength
{
    /// <summary>
    /// Numeric value.
    /// </summary>
    public double Value { get; }
    /// <summary>
    /// Unit type.
    /// </summary>
    public GridUnitType GridUnitType { get; }

    public GridLength(double value, GridUnitType type = GridUnitType.Pixel)
    {
        Value = value;
        GridUnitType = type;
    }

    /// <summary>
    /// Gets whether this is Auto sizing.
    /// </summary>
    public bool IsAuto => GridUnitType == GridUnitType.Auto;
    /// <summary>
    /// Gets whether this is Star sizing.
    /// </summary>
    public bool IsStar => GridUnitType == GridUnitType.Star;
    /// <summary>
    /// Gets whether this is absolute pixel sizing.
    /// </summary>
    public bool IsAbsolute => GridUnitType == GridUnitType.Pixel;

    /// <summary>
    /// Auto sizing (size to content).
    /// </summary>
    public static GridLength Auto => new(1, GridUnitType.Auto);
    /// <summary>
    /// Star sizing (1*).
    /// </summary>
    public static GridLength Star => new(1, GridUnitType.Star);
    /// <summary>
    /// Star sizing with specified value.
    /// </summary>
    /// <param name="value">Star value.</param>
    /// <returns>GridLength with star sizing.</returns>
    public static GridLength Stars(double value) => new(value, GridUnitType.Star);
    /// <summary>
    /// Absolute pixel sizing.
    /// </summary>
    /// <param name="value">Pixel value.</param>
    /// <returns>GridLength with pixel sizing.</returns>
    public static GridLength Pixels(double value) => new(value, GridUnitType.Pixel);

    public static implicit operator GridLength(double value) => new(value, GridUnitType.Pixel);
}

/// <summary>
/// Defines a row in a Grid.
/// </summary>
public class RowDefinition
{
    /// <summary>
    /// Gets or sets the row height.
    /// </summary>
    public GridLength Height { get; set; } = GridLength.Star;
    /// <summary>
    /// Gets or sets the minimum height.
    /// </summary>
    public double MinHeight { get; set; }
    /// <summary>
    /// Gets or sets the maximum height.
    /// </summary>
    public double MaxHeight { get; set; } = double.PositiveInfinity;
    internal double ActualHeight { get; set; }
    internal double Offset { get; set; }
}

/// <summary>
/// Defines a column in a Grid.
/// </summary>
public class ColumnDefinition
{
    /// <summary>
    /// Gets or sets the column width.
    /// </summary>
    public GridLength Width { get; set; } = GridLength.Star;
    /// <summary>
    /// Gets or sets the minimum width.
    /// </summary>
    public double MinWidth { get; set; }
    /// <summary>
    /// Gets or sets the maximum width.
    /// </summary>
    public double MaxWidth { get; set; } = double.PositiveInfinity;
    internal double ActualWidth { get; set; }
    internal double Offset { get; set; }
}

/// <summary>
/// A panel that arranges children in a grid of rows and columns.
/// </summary>
public class Grid : Panel
{
    private readonly List<RowDefinition> _rowDefinitions = new();
    private readonly List<ColumnDefinition> _columnDefinitions = new();

    private static readonly ConditionalWeakTable<Element, GridAttachedProperties> _attachedProperties = new();

    private sealed class GridAttachedProperties
    {
        public int Row;
        public bool HasRow;
        public int Column;
        public bool HasColumn;
        public int RowSpan = 1;
        public int ColumnSpan = 1;
    }

    /// <summary>
    /// Gets the row definitions collection.
    /// </summary>
    public IList<RowDefinition> RowDefinitions => _rowDefinitions;
    /// <summary>
    /// Gets the column definitions collection.
    /// </summary>
    public IList<ColumnDefinition> ColumnDefinitions => _columnDefinitions;

    /// <summary>
    /// Gets or sets whether children without explicit Row/Column are auto-placed.
    /// </summary>
    public bool AutoIndexing
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = true;

    /// <summary>
    /// Gets or sets the spacing between grid cells (both row/column gaps).
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

    #region Attached Properties

    /// <summary>
    /// Sets the row index for an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="row">Row index.</param>
    public static void SetRow(Element element, int row)
    {
        var props = GetOrCreate(element);
        props.Row = row;
        props.HasRow = true;
    }

    /// <summary>
    /// Gets the row index of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <returns>The row index.</returns>
    public static int GetRow(Element element) => TryGet(element, out var props) ? props.Row : 0;
    internal static bool HasRow(Element element) => TryGet(element, out var props) && props.HasRow;

    /// <summary>
    /// Sets the column index for an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="column">Column index.</param>
    public static void SetColumn(Element element, int column)
    {
        var props = GetOrCreate(element);
        props.Column = column;
        props.HasColumn = true;
    }

    /// <summary>
    /// Gets the column index of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <returns>The column index.</returns>
    public static int GetColumn(Element element) => TryGet(element, out var props) ? props.Column : 0;
    internal static bool HasColumn(Element element) => TryGet(element, out var props) && props.HasColumn;

    /// <summary>
    /// Sets the row span for an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="span">Number of rows to span.</param>
    public static void SetRowSpan(Element element, int span) => GetOrCreate(element).RowSpan = span;
    /// <summary>
    /// Gets the row span of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <returns>The row span.</returns>
    public static int GetRowSpan(Element element) => TryGet(element, out var props) ? props.RowSpan : 1;

    /// <summary>
    /// Sets the column span for an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <param name="span">Number of columns to span.</param>
    public static void SetColumnSpan(Element element, int span) => GetOrCreate(element).ColumnSpan = span;
    /// <summary>
    /// Gets the column span of an element.
    /// </summary>
    /// <param name="element">Target element.</param>
    /// <returns>The column span.</returns>
    public static int GetColumnSpan(Element element) => TryGet(element, out var props) ? props.ColumnSpan : 1;

    private static GridAttachedProperties GetOrCreate(Element element) => _attachedProperties.GetOrCreateValue(element);

    private static bool TryGet(Element element, [NotNullWhen(true)] out GridAttachedProperties? properties)
        => _attachedProperties.TryGetValue(element, out properties!);

    #endregion

    protected override void OnChildRemoved(Element child)
    {
        _attachedProperties.Remove(child);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var paddedSize = availableSize.Deflate(Padding);
        EnsureDefinitions();
        if (AutoIndexing)
        {
            EnsureAutoPlacement();
        }

        int rowCount = _rowDefinitions.Count;
        int colCount = _columnDefinitions.Count;

        // First pass: measure children with infinite size to get desired sizes
        foreach (var child in Children)
        {
            if (child is UIElement ui && !ui.IsVisible)
            {
                continue;
            }

            child.Measure(Size.Infinity);
        }

        double colGaps = colCount > 1 ? (colCount - 1) * Spacing : 0;
        double rowGaps = rowCount > 1 ? (rowCount - 1) * Spacing : 0;

        double availableWidth = Math.Max(0, paddedSize.Width - colGaps);
        double availableHeight = Math.Max(0, paddedSize.Height - rowGaps);

        // Calculate column widths
        CalculateLengths(_columnDefinitions, availableWidth, true);

        // Calculate row heights
        CalculateLengths(_rowDefinitions, availableHeight, false);

        // Calculate total size
        double totalWidth = 0;
        double totalHeight = 0;

        foreach (var col in _columnDefinitions)
        {
            totalWidth += col.ActualWidth;
        }

        foreach (var row in _rowDefinitions)
        {
            totalHeight += row.ActualHeight;
        }

        totalWidth += colGaps;
        totalHeight += rowGaps;

        return new Size(totalWidth, totalHeight).Inflate(Padding);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var contentBounds = bounds.Deflate(Padding);
        EnsureDefinitions();
        if (AutoIndexing)
        {
            EnsureAutoPlacement();
        }

        int rowCount = _rowDefinitions.Count;
        int colCount = _columnDefinitions.Count;

        double colGaps = colCount > 1 ? (colCount - 1) * Spacing : 0;
        double rowGaps = rowCount > 1 ? (rowCount - 1) * Spacing : 0;

        double availableWidth = Math.Max(0, contentBounds.Width - colGaps);
        double availableHeight = Math.Max(0, contentBounds.Height - rowGaps);

        // Recalculate with actual available space
        CalculateLengths(_columnDefinitions, availableWidth, true);
        CalculateLengths(_rowDefinitions, availableHeight, false);

        // Calculate offsets
        CalculateOffsets(_columnDefinitions, Spacing);
        CalculateOffsets(_rowDefinitions, Spacing);

        // Arrange children
        foreach (var child in Children)
        {
            if (child is UIElement ui && !ui.IsVisible)
            {
                continue;
            }

            int row = GetRow(child);
            int col = GetColumn(child);
            int rowSpan = GetRowSpan(child);
            int colSpan = GetColumnSpan(child);

            // Clamp to valid range
            row = Math.Clamp(row, 0, _rowDefinitions.Count - 1);
            col = Math.Clamp(col, 0, _columnDefinitions.Count - 1);
            rowSpan = Math.Clamp(rowSpan, 1, _rowDefinitions.Count - row);
            colSpan = Math.Clamp(colSpan, 1, _columnDefinitions.Count - col);

            // Calculate cell bounds
            double x = contentBounds.X + _columnDefinitions[col].Offset;
            double y = contentBounds.Y + _rowDefinitions[row].Offset;

            double width = 0;
            for (int i = col; i < col + colSpan; i++)
            {
                width += _columnDefinitions[i].ActualWidth;
            }

            double height = 0;
            for (int i = row; i < row + rowSpan; i++)
            {
                height += _rowDefinitions[i].ActualHeight;
            }

            if (colSpan > 1)
            {
                width += (colSpan - 1) * Spacing;
            }

            if (rowSpan > 1)
            {
                height += (rowSpan - 1) * Spacing;
            }

            child.Arrange(new Rect(x, y, width, height));
        }
    }

    private void EnsureDefinitions()
    {
        if (_rowDefinitions.Count == 0)
        {
            _rowDefinitions.Add(new RowDefinition());
        }

        if (_columnDefinitions.Count == 0)
        {
            _columnDefinitions.Add(new ColumnDefinition());
        }
    }

    private void EnsureAutoPlacement()
    {
        int rowCount = _rowDefinitions.Count;
        int colCount = _columnDefinitions.Count;

        if (rowCount <= 0 || colCount <= 0)
        {
            return;
        }

        var occupied = new bool[rowCount, colCount];

        // First mark explicitly placed cells (both axes specified)
        foreach (var child in Children)
        {
            if (child is UIElement ui && !ui.IsVisible)
            {
                continue;
            }

            bool hasRow = HasRow(child);
            bool hasCol = HasColumn(child);
            if (!hasRow || !hasCol)
            {
                continue;
            }

            int row = GetRow(child);
            int col = GetColumn(child);

            int rowSpan = Math.Max(1, GetRowSpan(child));
            int colSpan = Math.Max(1, GetColumnSpan(child));

            row = Math.Clamp(row, 0, rowCount - 1);
            col = Math.Clamp(col, 0, colCount - 1);
            rowSpan = Math.Clamp(rowSpan, 1, rowCount - row);
            colSpan = Math.Clamp(colSpan, 1, colCount - col);

            MarkOccupied(occupied, row, col, rowSpan, colSpan);
        }

        // Auto place remaining children
        foreach (var child in Children)
        {
            if (child is UIElement ui && !ui.IsVisible)
            {
                continue;
            }

            bool hasRow = HasRow(child);
            bool hasCol = HasColumn(child);
            if (hasRow && hasCol)
            {
                continue;
            }

            int rowSpan = Math.Max(1, GetRowSpan(child));
            int colSpan = Math.Max(1, GetColumnSpan(child));

            int placedRow = 0;
            int placedCol = 0;

            bool placed = false;

            if (hasRow && !hasCol)
            {
                int row = Math.Clamp(GetRow(child), 0, rowCount - 1);
                rowSpan = Math.Clamp(rowSpan, 1, rowCount - row);
                placed = TryFindInRow(occupied, rowCount, colCount, row, rowSpan, colSpan, out placedCol);
                placedRow = row;
            }
            else if (!hasRow && hasCol)
            {
                int col = Math.Clamp(GetColumn(child), 0, colCount - 1);
                colSpan = Math.Clamp(colSpan, 1, colCount - col);
                placed = TryFindInColumn(occupied, rowCount, colCount, col, rowSpan, colSpan, out placedRow);
                placedCol = col;
            }
            else
            {
                placed = TryFindFirstFit(occupied, rowCount, colCount, rowSpan, colSpan, out placedRow, out placedCol);
            }

            if (!placed)
            {
                placedRow = 0;
                placedCol = 0;
                rowSpan = Math.Clamp(rowSpan, 1, rowCount);
                colSpan = Math.Clamp(colSpan, 1, colCount);
            }

            if (!hasRow)
            {
                SetRow(child, placedRow);
            }

            if (!hasCol)
            {
                SetColumn(child, placedCol);
            }

            MarkOccupied(occupied, placedRow, placedCol,
                Math.Clamp(rowSpan, 1, rowCount - placedRow),
                Math.Clamp(colSpan, 1, colCount - placedCol));
        }
    }

    private static void MarkOccupied(bool[,] occupied, int row, int col, int rowSpan, int colSpan)
    {
        int rows = occupied.GetLength(0);
        int cols = occupied.GetLength(1);

        int maxRow = Math.Min(rows, row + rowSpan);
        int maxCol = Math.Min(cols, col + colSpan);

        for (int r = row; r < maxRow; r++)
        {
            for (int c = col; c < maxCol; c++)
            {
                occupied[r, c] = true;
            }
        }
    }

    private static bool CanPlace(bool[,] occupied, int row, int col, int rowSpan, int colSpan)
    {
        int rows = occupied.GetLength(0);
        int cols = occupied.GetLength(1);

        if (row < 0 || col < 0 || row >= rows || col >= cols)
        {
            return false;
        }

        if (row + rowSpan > rows || col + colSpan > cols)
        {
            return false;
        }

        for (int r = row; r < row + rowSpan; r++)
        {
            for (int c = col; c < col + colSpan; c++)
            {
                if (occupied[r, c])
                {
                    return false;
                }
            }
        }

        return true;
    }

    private static bool TryFindFirstFit(bool[,] occupied, int rows, int cols, int rowSpan, int colSpan, out int row, out int col)
    {
        row = 0;
        col = 0;

        rowSpan = Math.Clamp(rowSpan, 1, rows);
        colSpan = Math.Clamp(colSpan, 1, cols);

        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                if (CanPlace(occupied, r, c, rowSpan, colSpan))
                {
                    row = r;
                    col = c;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryFindInRow(bool[,] occupied, int rows, int cols, int row, int rowSpan, int colSpan, out int col)
    {
        col = 0;
        rowSpan = Math.Clamp(rowSpan, 1, rows - row);
        colSpan = Math.Clamp(colSpan, 1, cols);

        for (int c = 0; c < cols; c++)
        {
            if (CanPlace(occupied, row, c, rowSpan, colSpan))
            {
                col = c;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindInColumn(bool[,] occupied, int rows, int cols, int col, int rowSpan, int colSpan, out int row)
    {
        row = 0;
        rowSpan = Math.Clamp(rowSpan, 1, rows);
        colSpan = Math.Clamp(colSpan, 1, cols - col);

        for (int r = 0; r < rows; r++)
        {
            if (CanPlace(occupied, r, col, rowSpan, colSpan))
            {
                row = r;
                return true;
            }
        }

        return false;
    }

    private void CalculateLengths<T>(IList<T> definitions, double available, bool isColumn) where T : class
    {
        bool isInfinite = double.IsPositiveInfinity(available);
        double totalFixed = 0;
        double totalStars = 0;

        foreach (var def in definitions)
        {
            var length = isColumn ? ((ColumnDefinition)(object)def).Width : ((RowDefinition)(object)def).Height;
            var min = isColumn ? ((ColumnDefinition)(object)def).MinWidth : ((RowDefinition)(object)def).MinHeight;
            var max = isColumn ? ((ColumnDefinition)(object)def).MaxWidth : ((RowDefinition)(object)def).MaxHeight;

            if (length.IsAbsolute)
            {
                var size = Math.Clamp(length.Value, min, max);
                SetActualSize(def, size, isColumn);
                totalFixed += size;
            }
            else if (length.IsAuto)
            {
                // Find max desired size of children in this row/column
                double maxDesired = 0;
                foreach (var child in Children)
                {
                    if (child is UIElement ui && !ui.IsVisible)
                    {
                        continue;
                    }

                    int index = isColumn ? GetColumn(child) : GetRow(child);
                    int span = isColumn ? GetColumnSpan(child) : GetRowSpan(child);
                    int defIndex = definitions.IndexOf(def);

                    if (index <= defIndex && index + span > defIndex)
                    {
                        double desired = isColumn ? child.DesiredSize.Width : child.DesiredSize.Height;
                        if (span > 1)
                        {
                            desired = Math.Max(0, desired - (span - 1) * Spacing);
                        }

                        maxDesired = Math.Max(maxDesired, desired / span);
                    }
                }
                var size = Math.Clamp(maxDesired, min, max);
                SetActualSize(def, size, isColumn);
                totalFixed += size;
            }
            else // Star
            {
                if (isInfinite)
                {
                    // WPF-like behavior: when unconstrained, star sizing behaves like "size to content" in Measure.
                    // Actual star distribution still happens during Arrange with the real available size.
                    double maxDesired = 0;
                    foreach (var child in Children)
                    {
                        if (child is UIElement ui && !ui.IsVisible)
                        {
                            continue;
                        }

                        int index = isColumn ? GetColumn(child) : GetRow(child);
                        int span = isColumn ? GetColumnSpan(child) : GetRowSpan(child);
                        int defIndex = definitions.IndexOf(def);

                        if (index <= defIndex && index + span > defIndex)
                        {
                            double desired = isColumn ? child.DesiredSize.Width : child.DesiredSize.Height;
                            if (span > 1)
                            {
                                desired = Math.Max(0, desired - (span - 1) * Spacing);
                            }

                            maxDesired = Math.Max(maxDesired, desired / span);
                        }
                    }

                    var size = Math.Clamp(maxDesired, min, max);
                    SetActualSize(def, size, isColumn);
                    totalFixed += size;
                }
                else
                {
                    totalStars += length.Value;
                }
            }
        }

        // Distribute remaining space to star-sized definitions
        double remaining = Math.Max(0, available - totalFixed);

        if (totalStars > 0)
        {
            foreach (var def in definitions)
            {
                var length = isColumn ? ((ColumnDefinition)(object)def).Width : ((RowDefinition)(object)def).Height;
                var min = isColumn ? ((ColumnDefinition)(object)def).MinWidth : ((RowDefinition)(object)def).MinHeight;
                var max = isColumn ? ((ColumnDefinition)(object)def).MaxWidth : ((RowDefinition)(object)def).MaxHeight;

                if (length.IsStar)
                {
                    var size = Math.Clamp(remaining * length.Value / totalStars, min, max);
                    SetActualSize(def, size, isColumn);
                }
            }
        }
    }

    private static void SetActualSize<T>(T def, double size, bool isColumn) where T : class
    {
        if (isColumn)
        {
            ((ColumnDefinition)(object)def).ActualWidth = size;
        }
        else
        {
            ((RowDefinition)(object)def).ActualHeight = size;
        }
    }

    private static void CalculateOffsets<T>(IList<T> definitions, double spacing) where T : class
    {
        double offset = 0;
        foreach (var def in definitions)
        {
            if (def is ColumnDefinition col)
            {
                col.Offset = offset;
                offset += col.ActualWidth + spacing;
            }
            else if (def is RowDefinition row)
            {
                row.Offset = offset;
                offset += row.ActualHeight + spacing;
            }
        }
    }
}
