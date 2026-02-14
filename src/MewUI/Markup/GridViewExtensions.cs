namespace Aprillz.MewUI.Controls;

/// <summary>
/// Fluent extension methods for configuring <see cref="GridView"/> and <see cref="GridViewColumn{TItem}"/>.
/// </summary>
public static class GridViewExtensions
{
    /// <summary>
    /// Sets the row height.
    /// </summary>
    /// <param name="gridView">Target grid view.</param>
    /// <param name="rowHeight">Row height in DIPs.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView RowHeight(this GridView gridView, double rowHeight)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        gridView.RowHeight = rowHeight;
        return gridView;
    }

    /// <summary>
    /// Sets the header row height.
    /// </summary>
    /// <param name="gridView">Target grid view.</param>
    /// <param name="headerHeight">Header height in DIPs.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView HeaderHeight(this GridView gridView, double headerHeight)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        gridView.HeaderHeight = headerHeight;
        return gridView;
    }

    /// <summary>
    /// Enables or disables zebra striping.
    /// </summary>
    /// <param name="gridView">Target grid view.</param>
    /// <param name="enabled"><see langword="true"/> to enable striping; otherwise, <see langword="false"/>.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView ZebraStriping(this GridView gridView, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        gridView.ZebraStriping = enabled;
        return gridView;
    }

    /// <summary>
    /// Enables or disables grid lines.
    /// </summary>
    /// <param name="gridView">Target grid view.</param>
    /// <param name="enabled"><see langword="true"/> to show grid lines; otherwise, <see langword="false"/>.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView ShowGridLines(this GridView gridView, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        gridView.ShowGridLines = enabled;
        return gridView;
    }

    /// <summary>
    /// Adds one or more columns to the grid view.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="gridView">Target grid view.</param>
    /// <param name="columns">Columns to add.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView Columns<TItem>(this GridView gridView, params GridViewColumn<TItem>[] columns)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        ArgumentNullException.ThrowIfNull(columns);
        gridView.AddColumns(columns);
        return gridView;
    }

    /// <summary>
    /// Sets the item source from a list.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="gridView">Target grid view.</param>
    /// <param name="items">Items to display.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView ItemsSource<TItem>(this GridView gridView, IReadOnlyList<TItem> items)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        ArgumentNullException.ThrowIfNull(items);
        gridView.SetItemsSource(items);
        return gridView;
    }

    /// <summary>
    /// Sets the item source from an <see cref="ItemsView{T}"/>.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="gridView">Target grid view.</param>
    /// <param name="itemsView">Items view to display.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView ItemsSource<TItem>(this GridView gridView, ItemsView<TItem> itemsView)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        ArgumentNullException.ThrowIfNull(itemsView);
        gridView.SetItemsSource(itemsView);
        return gridView;
    }

    /// <summary>
    /// Adds a column.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="gridView">Target grid view.</param>
    /// <param name="header">Column header text.</param>
    /// <param name="width">Column width in DIPs.</param>
    /// <param name="template">Cell template.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView AddColumn<TItem>(
        this GridView gridView,
        string header,
        double width,
        IDataTemplate<TItem> template)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        ArgumentNullException.ThrowIfNull(template);

        gridView.AddColumns(new GridViewColumn<TItem> { Header = header ?? string.Empty, Width = width, CellTemplate = template });
        return gridView;
    }

    /// <summary>
    /// Adds a column using delegate-based templating.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="gridView">Target grid view.</param>
    /// <param name="header">Column header text.</param>
    /// <param name="width">Column width in DIPs.</param>
    /// <param name="build">Template build callback.</param>
    /// <param name="bind">Template bind callback.</param>
    /// <returns>The grid view for chaining.</returns>
    public static GridView AddColumn<TItem>(
        this GridView gridView,
        string header,
        double width,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind)
        => AddColumn(gridView, header, width, new DelegateTemplate<TItem>(build, bind));

    /// <summary>
    /// Creates a column definition.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="header">Column header text.</param>
    /// <param name="width">Column width in DIPs.</param>
    /// <param name="template">Cell template.</param>
    public static GridViewColumn<TItem> Column<TItem>(
        string header,
        double width,
        IDataTemplate<TItem> template)
        => new GridViewColumn<TItem> { Header = header ?? string.Empty, Width = width, CellTemplate = template };

    /// <summary>
    /// Creates a column definition using delegate-based templating.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="header">Column header text.</param>
    /// <param name="width">Column width in DIPs.</param>
    /// <param name="build">Template build callback.</param>
    /// <param name="bind">Template bind callback.</param>
    public static GridViewColumn<TItem> Column<TItem>(
        string header,
        double width,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind)
        => new GridViewColumn<TItem> { Header = header ?? string.Empty, Width = width, CellTemplate = new DelegateTemplate<TItem>(build, bind) };

    /// <summary>
    /// Sets the column header text.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="column">Target column.</param>
    /// <param name="header">Header text.</param>
    /// <returns>The column for chaining.</returns>
    public static GridViewColumn<TItem> Header<TItem>(this GridViewColumn<TItem> column, string header)
    {
        ArgumentNullException.ThrowIfNull(column);
        column.Header = header ?? string.Empty;
        return column;
    }

    /// <summary>
    /// Sets the column width.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="column">Target column.</param>
    /// <param name="width">Width in DIPs.</param>
    /// <returns>The column for chaining.</returns>
    public static GridViewColumn<TItem> Width<TItem>(this GridViewColumn<TItem> column, double width)
    {
        ArgumentNullException.ThrowIfNull(column);
        column.Width = width;
        return column;
    }

    /// <summary>
    /// Sets the column cell template.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <param name="column">Target column.</param>
    /// <param name="template">Cell template.</param>
    /// <returns>The column for chaining.</returns>
    public static GridViewColumn<TItem> Bind<TItem>(
        this GridViewColumn<TItem> column,
        IDataTemplate<TItem> template)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(template);

        column.CellTemplate = template;
        return column;
    }

    public static GridViewColumn<TItem> Template<TItem>(
        this GridViewColumn<TItem> column,
        IDataTemplate<TItem> template)
        => Bind(column, template);

    /// <summary>
    /// Sets the cell template using delegate-based templating.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    /// <param name="column">Target column.</param>
    /// <param name="build">Template build callback.</param>
    /// <param name="bind">Template bind callback.</param>
    /// <returns>The column for chaining.</returns>
    public static GridViewColumn<TItem> Bind<TItem, TElement>(
        this GridViewColumn<TItem> column,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem, int, TemplateContext> bind) where TElement : FrameworkElement
        => Bind(column, new DelegateTemplate<TItem>(build, (a, b, c, d) => bind((TElement)a, b, c, d)));

    /// <summary>
    /// Sets the cell template using delegate-based templating with a simple bind callback.
    /// </summary>
    /// <typeparam name="TItem">Item type.</typeparam>
    /// <typeparam name="TElement">Template root element type.</typeparam>
    /// <param name="column">Target column.</param>
    /// <param name="build">Template build callback.</param>
    /// <param name="bind">Template bind callback.</param>
    /// <returns>The column for chaining.</returns>
    public static GridViewColumn<TItem> Bind<TItem, TElement>(
        this GridViewColumn<TItem> column,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem> bind) where TElement : FrameworkElement
        => Bind(column, new DelegateTemplate<TItem>(build, (view, item, index, ctx) => bind((TElement)view, item)));

    public static GridViewColumn<TItem> Template<TItem, TElement>(
        this GridViewColumn<TItem> column,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem, int, TemplateContext> bind) where TElement : FrameworkElement
        => Bind(column, build, bind);

    public static GridViewColumn<TItem> Template<TItem, TElement>(
        this GridViewColumn<TItem> column,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem> bind) where TElement : FrameworkElement
        => Bind(column, build, bind);

    public static GridViewColumn<TItem> Text<TItem>(
        this GridViewColumn<TItem> column,
        Func<TItem, string> textSelector)
    {
        ArgumentNullException.ThrowIfNull(column);
        ArgumentNullException.ThrowIfNull(textSelector);

        return Template(
            column,
            build: _ => new Label().Padding(8, 0).CenterVertical(),
            bind: (Label tb, TItem item) => tb.Text = textSelector(item) ?? string.Empty);
    }
}
