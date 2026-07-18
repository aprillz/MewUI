namespace Aprillz.MewUI.Controls;

public sealed class GridViewColumn<TItem>
{
    public string Header { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the column width. Numeric values are treated as fixed pixel widths.
    /// </summary>
    public GridLength Width { get; set; } = GridLength.Pixels(0);

    /// <summary>
    /// Minimum column width enforced during resize. Default is 0 (no minimum).
    /// </summary>
    public double MinWidth { get; set; }

    /// <summary>
    /// Maximum column width enforced by layout and resize. Default is positive infinity.
    /// </summary>
    public double MaxWidth { get; set; } = double.PositiveInfinity;

    /// <summary>
    /// Whether the column can be resized by dragging the header separator. Default is true.
    /// </summary>
    public bool IsResizable { get; set; } = true;

    /// <summary>
    /// Gets or sets the item comparer used when this column is sorted. A null comparer makes
    /// the column non-sortable.
    /// </summary>
    public IComparer<TItem>? SortComparer { get; set; }

    public IDataTemplate<TItem>? CellTemplate { get; set; }
}
