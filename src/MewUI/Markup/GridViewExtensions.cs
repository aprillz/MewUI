namespace Aprillz.MewUI.Controls;

public static class GridViewExtensions
{
    public static GridView RowHeight(this GridView gridView, double rowHeight)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        gridView.RowHeight = rowHeight;
        return gridView;
    }

    public static GridView HeaderHeight(this GridView gridView, double headerHeight)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        gridView.HeaderHeight = headerHeight;
        return gridView;
    }

    public static GridView ZebraStriping(this GridView gridView, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        gridView.ZebraStriping = enabled;
        return gridView;
    }

    public static GridView ShowGridLines(this GridView gridView, bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        gridView.ShowGridLines = enabled;
        return gridView;
    }

    public static GridView Columns<TItem>(this GridView gridView, params GridViewColumn<TItem>[] columns)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        ArgumentNullException.ThrowIfNull(columns);
        gridView.AddColumns(columns);
        return gridView;
    }

    public static GridView ItemsSource<TItem>(this GridView gridView, IReadOnlyList<TItem> items)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        ArgumentNullException.ThrowIfNull(items);
        gridView.SetItemsSource(items);
        return gridView;
    }

    public static GridView ItemsSource<TItem>(this GridView gridView, ItemsView<TItem> itemsView)
    {
        ArgumentNullException.ThrowIfNull(gridView);
        ArgumentNullException.ThrowIfNull(itemsView);
        gridView.SetItemsSource(itemsView);
        return gridView;
    }

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

    public static GridView AddColumn<TItem>(
        this GridView gridView,
        string header,
        double width,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind)
        => AddColumn(gridView, header, width, new DelegateTemplate<TItem>(build, bind));

    public static GridViewColumn<TItem> Column<TItem>(
        string header,
        double width,
        IDataTemplate<TItem> template)
        => new GridViewColumn<TItem> { Header = header ?? string.Empty, Width = width, CellTemplate = template };

    public static GridViewColumn<TItem> Column<TItem>(
        string header,
        double width,
        Func<TemplateContext, FrameworkElement> build,
        Action<FrameworkElement, TItem, int, TemplateContext> bind)
        => new GridViewColumn<TItem> { Header = header ?? string.Empty, Width = width, CellTemplate = new DelegateTemplate<TItem>(build, bind) };

    public static GridViewColumn<TItem> Header<TItem>(this GridViewColumn<TItem> column, string header)
    {
        ArgumentNullException.ThrowIfNull(column);
        column.Header = header ?? string.Empty;
        return column;
    }

    public static GridViewColumn<TItem> Width<TItem>(this GridViewColumn<TItem> column, double width)
    {
        ArgumentNullException.ThrowIfNull(column);
        column.Width = width;
        return column;
    }

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

    public static GridViewColumn<TItem> Bind<TItem, TElement>(
        this GridViewColumn<TItem> column,
        Func<TemplateContext, TElement> build,
        Action<TElement, TItem, int, TemplateContext> bind) where TElement : FrameworkElement
        => Bind(column, new DelegateTemplate<TItem>(build, (a, b, c, d) => bind((TElement)a, b, c, d)));

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
            bind: (Label lb, TItem item) => lb.Text = textSelector(item) ?? string.Empty);
    }
}
