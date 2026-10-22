namespace Aprillz.MewUI.Controls;

/// <summary>
/// A column of a <see cref="GridView"/>: header, width constraints, sort comparer and cell
/// template for items of <typeparamref name="TItem"/>.
/// </summary>
public sealed class GridViewColumn<TItem>
{
    /// <summary>Gets or sets the header text.</summary>
    public string Header { get; set; } = string.Empty;

    /// <summary>Gets or sets the horizontal alignment of the header text.</summary>
    public TextAlignment HeaderTextAlignment { get; set; } = TextAlignment.Left;

    /// <summary>
    /// Gets or sets the column width. Numeric values are treated as fixed pixel widths.
    /// </summary>
    public GridLength Width { get; set; } = GridLength.Pixels(0);

    /// <summary>
    /// Gets or sets the minimum column width enforced during resize; 0 (the default) means no
    /// minimum.
    /// </summary>
    public double MinWidth { get; set; }

    /// <summary>
    /// Gets or sets the maximum column width enforced by layout and resize; the default is positive
    /// infinity.
    /// </summary>
    public double MaxWidth { get; set; } = double.PositiveInfinity;

    /// <summary>
    /// Gets or sets whether the column can be resized by dragging the header separator; the default
    /// is true.
    /// </summary>
    public bool IsResizable { get; set; } = true;

    /// <summary>
    /// Gets or sets the item comparer used when this column is sorted; null makes the column
    /// non-sortable.
    /// </summary>
    public IComparer<TItem>? SortComparer { get; set; }

    /// <summary>Gets or sets the template that builds a row's cell for this column.</summary>
    public IDataTemplate<TItem>? CellTemplate { get; set; }
}
