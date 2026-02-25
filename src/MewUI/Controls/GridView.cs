using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class GridView : VirtualizedItemsBase, IFocusIntoViewHost, IVirtualizedTabNavigationHost
{
    private object? _itemTypeToken;
    private readonly GridViewCore _core = new();

    private readonly HeaderRow _header;
    private readonly FixedHeightItemsPresenter _presenter;

    private double _rowsExtentHeight;
    private double _columnsExtentWidth;
    private double _rowsViewportHeight;
    private double _rowsViewportWidth;

    public GridView()
    {
        CellPadding = Theme.Metrics.ItemPadding;

        _scrollViewer.Padding = new Thickness(0);
        _scrollViewer.ViewportCornerRadius = 0;

        _header = new HeaderRow(this) { Parent = this };
        _presenter = new FixedHeightItemsPresenter
        {
            BorderThickness = 0,
            Padding = new Thickness(0),
            UseHorizontalExtentForLayout = true,
        };

        var rowTemplate = new DelegateTemplate<object?>(
            build: _ => new Row(this),
            bind: BindRowTemplate);

        _presenter.ItemTemplate = rowTemplate;
        _presenter.ItemsSource = _core.ItemsSource;
        _presenter.BeforeItemRender = BeforeRowRender;

        _scrollViewer.Content = _presenter;
        _scrollViewer.ScrollChanged += () =>
        {
            _header.HorizontalOffset = _scrollViewer.HorizontalOffset;
        };

        _core.ItemsChanged += OnItemsChanged;
        _core.SelectionChanged += _ => OnItemsSelectionChanged();
        _core.ColumnsChanged += () =>
        {
            _header.SetColumns(_core.Columns);
            _presenter.RecycleAll();
            _rebindVisibleOnNextRender = true;
            InvalidateMeasure();
            InvalidateArrange();
            InvalidateVisual();
        };

        _tabFocusHelper = new PendingTabFocusHelper(
            getWindow: () => FindVisualRoot() as Window,
            getContainer: idx =>
            {
                FrameworkElement? container = null;
                _presenter.VisitRealized((i, el) => { if (i == idx) container = el; });
                return container;
            });
    }

    public event Action<object?>? SelectionChanged;

    public bool ZebraStriping
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateVisual();
            }
        }
    } = true;

    public bool ShowGridLines
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateVisual();
            }
        }
    }

    public double RowHeight
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
                InvalidateArrange();
            }
        }
    } = double.NaN;

    public double HeaderHeight
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
                InvalidateArrange();
            }
        }
    } = double.NaN;

    public Thickness CellPadding
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                _rebindVisibleOnNextRender = true;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    public double MaxAutoViewportHeight
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
                InvalidateArrange();
            }
        }
    } = 320;

    public int SelectedIndex
    {
        get => _core.SelectedIndex;
        set => _core.SelectedIndex = value;
    }

    public object? SelectedItem => _core.SelectedItem;

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _rebindVisibleOnNextRender = true;
        InvalidateVisual();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        int count = _core.ItemsSource.Count;
        if (count <= 0)
        {
            return;
        }

        int current = SelectedIndex >= 0 ? SelectedIndex : 0;

        switch (e.Key)
        {
            case Key.Up:
                SelectedIndex = Math.Max(0, current - 1);
                e.Handled = true;
                break;

            case Key.Down:
                SelectedIndex = Math.Min(count - 1, current + 1);
                e.Handled = true;
                break;

            case Key.Home:
                SelectedIndex = 0;
                e.Handled = true;
                break;

            case Key.End:
                SelectedIndex = count - 1;
                e.Handled = true;
                break;

            case Key.PageUp:
                SelectedIndex = Math.Max(0, current - ResolvePageStep(count));
                e.Handled = true;
                break;

            case Key.PageDown:
                SelectedIndex = Math.Min(count - 1, current + ResolvePageStep(count));
                e.Handled = true;
                break;
        }

        if (e.Handled)
        {
            Focus();
            InvalidateVisual();
        }
    }

    private int ResolvePageStep(int count)
    {
        double rowH = ResolveRowHeight();
        if (rowH <= 0 || double.IsNaN(rowH) || double.IsInfinity(rowH))
        {
            return 1;
        }

        double viewport = _rowsViewportHeight;
        if (viewport <= 0 || double.IsNaN(viewport) || double.IsInfinity(viewport))
        {
            return 1;
        }

        int step = (int)Math.Floor(viewport / rowH);
        return Math.Clamp(step, 1, Math.Max(1, count));
    }

    bool IFocusIntoViewHost.OnDescendantFocused(UIElement focusedElement)
    {
        if (focusedElement == this)
        {
            return false;
        }

        EnsureHorizontalIntoView(focusedElement);

        int found = -1;
        _presenter.VisitRealized((i, element) =>
        {
            if (found != -1)
            {
                return;
            }

            if (VisualTree.IsInSubtreeOf(focusedElement, element))
            {
                found = i;
            }
        });

        if (found < 0 || found >= _core.ItemsSource.Count)
        {
            return false;
        }

        if (SelectedIndex != found)
        {
            SelectedIndex = found;
        }
        else
        {
            ScrollIntoView(found);
        }

        return true;
    }

    private void EnsureHorizontalIntoView(UIElement focusedElement)
    {
        if (_core.Columns.Count == 0)
        {
            return;
        }

        if (!TryGetContentBounds(out var contentLocal, out double headerH))
        {
            return;
        }

        double viewportW = Math.Max(0, contentLocal.Width);
        if (viewportW <= 0 || double.IsNaN(viewportW) || double.IsInfinity(viewportW))
        {
            return;
        }

        double extentW = _columnsExtentWidth;
        if (extentW <= 0 || double.IsNaN(extentW) || double.IsInfinity(extentW))
        {
            extentW = ComputeColumnsExtentWidth();
        }

        if (extentW <= viewportW + 0.5)
        {
            return;
        }

        var size = focusedElement.RenderSize;
        var localRect = new Rect(0, 0, size.Width, size.Height);

        Rect rectInGrid;
        try
        {
            rectInGrid = focusedElement.TranslateRect(localRect, this);
        }
        catch (InvalidOperationException)
        {
            return;
        }

        // Both rectInGrid and contentLocal are in this GridView's coordinate space.
        double viewportLeft = contentLocal.X;
        double viewportRight = viewportLeft + viewportW;

        double oldOffset = _scrollViewer.HorizontalOffset;
        double newOffset = oldOffset;

        if (rectInGrid.Left < viewportLeft)
        {
            newOffset = oldOffset - (viewportLeft - rectInGrid.Left);
        }
        else if (rectInGrid.Right > viewportRight)
        {
            newOffset = oldOffset + (rectInGrid.Right - viewportRight);
        }
        else
        {
            return;
        }

        newOffset = Math.Clamp(newOffset, 0, Math.Max(0, extentW - viewportW));

        if (!newOffset.Equals(oldOffset))
        {
            _scrollViewer.SetScrollOffsets(newOffset, _scrollViewer.VerticalOffset);
        }
    }

    bool IVirtualizedTabNavigationHost.TryMoveFocusFromDescendant(UIElement focusedElement, bool moveForward)
    {
        if (!IsEffectivelyEnabled || _core.ItemsSource.Count == 0)
        {
            return false;
        }

        int found = -1;
        _presenter.VisitRealized((i, element) =>
        {
            if (found != -1)
            {
                return;
            }

            if (VisualTree.IsInSubtreeOf(focusedElement, element))
            {
                found = i;
            }
        });

        if (found < 0)
        {
            return false;
        }

        int targetIndex = moveForward ? found + 1 : found - 1;
        if (targetIndex < 0 || targetIndex >= _core.ItemsSource.Count)
        {
            return false;
        }

        SelectedIndex = targetIndex;
        ScrollIntoView(targetIndex);
        _tabFocusHelper.Schedule(targetIndex);
        return true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled)
        {
            return;
        }

        bool canVScroll = _rowsExtentHeight > _rowsViewportHeight + 0.5;
        bool canHScroll = _columnsExtentWidth > _rowsViewportWidth + 0.5;

        // Prefer vertical scroll unless a horizontal wheel event is explicit.
        if (!e.IsHorizontal && canVScroll)
        {
            _scrollViewer.ScrollBy(-e.Delta);
            e.Handled = true;
            return;
        }

        if (e.IsHorizontal && canHScroll)
        {
            _scrollViewer.ScrollByHorizontal(-e.Delta);
            e.Handled = true;
        }
    }

    public void SetItemsSource<TItem>(IReadOnlyList<TItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        EnsureConfiguredFor<TItem>();
        _core.SetItems(ItemsView.Create(items));
    }

    public void SetItemsSource<TItem>(ItemsView<TItem> itemsView)
    {
        ArgumentNullException.ThrowIfNull(itemsView);
        EnsureConfiguredFor<TItem>();
        _core.SetItems(itemsView);
    }

    public void SetColumns<TItem>(IReadOnlyList<GridViewColumn<TItem>> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        EnsureConfiguredFor<TItem>();
        _core.SetColumns(ConvertColumns(columns));
    }

    /// <summary>
    /// Attempts to find the item (row) index at the specified position in this control's coordinates.
    /// </summary>
    public bool TryGetItemIndexAt(Point position, out int index)
        => TryGetItemIndexAtCore(position, out index);

    /// <summary>
    /// Attempts to find the item (row) index for a mouse event routed by the window input router.
    /// </summary>
    public bool TryGetItemIndexAt(MouseEventArgs e, out int index)
    {
        ArgumentNullException.ThrowIfNull(e);
        return TryGetItemIndexAtCore(e.GetPosition(this), out index);
    }

    private bool TryGetItemIndexAtCore(Point position, out int index)
    {
        index = -1;

        // Don't treat scrollbar interaction as item hit/activation.
        var windowPoint = new Point(Bounds.X + position.X, Bounds.Y + position.Y);
        if (_scrollViewer.HitTest(windowPoint) is ScrollBar)
        {
            return false;
        }

        if (!TryGetContentBounds(out var contentBounds, out double headerH))
        {
            return false;
        }

        double rowsHeight = Math.Max(0, contentBounds.Height - headerH);
        double rowsY = contentBounds.Y + headerH;
        if (rowsHeight <= 0)
        {
            return false;
        }

        if (position.Y < rowsY || position.Y >= rowsY + rowsHeight)
        {
            return false;
        }

        double rowH = ResolveRowHeight();
        if (rowH <= 0 || double.IsNaN(rowH) || double.IsInfinity(rowH))
        {
            return false;
        }

        return ItemsViewportMath.TryGetItemIndexAtY(
            position.Y,
            rowsY,
            _scrollViewer.VerticalOffset,
            rowH,
            _core.ItemsSource.Count,
            out index);
    }

    /// <summary>
    /// Attempts to find the column index at the specified position in this control's coordinates.
    /// Returns <see langword="true"/> only when the position is over the header or a row area.
    /// </summary>
    public bool TryGetColumnIndexAt(Point position, out int columnIndex)
        => TryGetColumnIndexAtCore(position, out columnIndex);

    /// <summary>
    /// Attempts to find the column index for a mouse event routed by the window input router.
    /// </summary>
    public bool TryGetColumnIndexAt(MouseEventArgs e, out int columnIndex)
    {
        ArgumentNullException.ThrowIfNull(e);
        return TryGetColumnIndexAtCore(e.GetPosition(this), out columnIndex);
    }

    private bool TryGetColumnIndexAtCore(Point position, out int columnIndex)
    {
        columnIndex = -1;

        // Don't treat scrollbar interaction as column hit.
        var windowPoint = new Point(Bounds.X + position.X, Bounds.Y + position.Y);
        if (_scrollViewer.HitTest(windowPoint) is ScrollBar)
        {
            return false;
        }

        if (!TryGetContentBounds(out var contentBounds, out double headerH))
        {
            return false;
        }

        double y0 = contentBounds.Y;
        double y1 = contentBounds.Y + contentBounds.Height;
        if (position.Y < y0 || position.Y >= y1)
        {
            return false;
        }

        return TryGetColumnIndexAtX(position.X, contentBounds.X, contentBounds.Width, out columnIndex);
    }

    /// <summary>
    /// Attempts to find the cell (row/column) indices at the specified position in this control's coordinates.
    /// When the position is over the header, returns <see langword="true"/> with <paramref name="isHeader"/> set
    /// and <paramref name="rowIndex"/> set to -1.
    /// </summary>
    public bool TryGetCellIndexAt(Point position, out int rowIndex, out int columnIndex, out bool isHeader)
        => TryGetCellIndexAtCore(position, out rowIndex, out columnIndex, out isHeader);

    /// <summary>
    /// Attempts to find the cell (row/column) indices for a mouse event routed by the window input router.
    /// </summary>
    public bool TryGetCellIndexAt(MouseEventArgs e, out int rowIndex, out int columnIndex, out bool isHeader)
    {
        ArgumentNullException.ThrowIfNull(e);
        return TryGetCellIndexAtCore(e.GetPosition(this), out rowIndex, out columnIndex, out isHeader);
    }

    private bool TryGetCellIndexAtCore(Point position, out int rowIndex, out int columnIndex, out bool isHeader)
    {
        rowIndex = -1;
        columnIndex = -1;
        isHeader = false;

        // Don't treat scrollbar interaction as cell hit.
        var windowPoint = new Point(Bounds.X + position.X, Bounds.Y + position.Y);
        if (_scrollViewer.HitTest(windowPoint) is ScrollBar)
        {
            return false;
        }

        if (!TryGetContentBounds(out var contentBounds, out double headerH))
        {
            return false;
        }

        double headerY0 = contentBounds.Y;
        double headerY1 = contentBounds.Y + headerH;
        if (position.Y >= headerY0 && position.Y < headerY1)
        {
            if (!TryGetColumnIndexAtX(position.X, contentBounds.X, contentBounds.Width, out columnIndex))
            {
                return false;
            }

            isHeader = true;
            rowIndex = -1;
            return true;
        }

        if (!TryGetItemIndexAtCore(position, out rowIndex))
        {
            return false;
        }

        if (!TryGetColumnIndexAtX(position.X, contentBounds.X, contentBounds.Width, out columnIndex))
        {
            return false;
        }

        isHeader = false;
        return true;
    }

    private bool TryGetContentBounds(out Rect contentBounds, out double headerHeight)
    {
        contentBounds = default;
        headerHeight = 0;

        var bounds = GetSnappedBorderBounds(new Rect(0, 0, Bounds.Width, Bounds.Height));
        var dpiScale = GetDpi() / 96.0;
        var innerBounds = bounds.Deflate(new Thickness(GetBorderVisualInset()));
        var viewportBounds = innerBounds;
        // Viewport/clip rect should not shrink due to edge rounding; snap outward.
        contentBounds = LayoutRounding.SnapViewportRectToPixels(viewportBounds.Deflate(Padding), dpiScale);
        headerHeight = ResolveHeaderHeight();

        if (contentBounds.Width <= 0 || contentBounds.Height <= 0 || headerHeight < 0 ||
            double.IsNaN(contentBounds.Width) || double.IsNaN(contentBounds.Height) ||
            double.IsInfinity(contentBounds.Width) || double.IsInfinity(contentBounds.Height))
        {
            return false;
        }

        return true;
    }

    private bool TryGetColumnIndexAtX(double x, double contentX, double contentWidth, out int columnIndex)
    {
        columnIndex = -1;

        if (x < contentX || x >= contentX + contentWidth)
        {
            return false;
        }

        // Account for horizontal scroll: columns are laid out starting at (contentX - offset).
        x += _scrollViewer.HorizontalOffset;

        // Hit-test column by accumulated widths.
        double cur = contentX;
        for (int i = 0; i < _core.Columns.Count; i++)
        {
            double w = Math.Max(0, _core.Columns[i].Width);
            double next = cur + w;
            if (x >= cur && x < next)
            {
                columnIndex = i;
                return true;
            }
            cur = next;
        }

        return false;
    }

    public void AddColumns<TItem>(params GridViewColumn<TItem>[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);
        EnsureConfiguredFor<TItem>();
        _core.AddColumns(ConvertColumns(columns));
    }

    private void EnsureConfiguredFor<TItem>()
    {
        if (_itemTypeToken == null)
        {
            _itemTypeToken = typeof(TItem);
            return;
        }

        if (!ReferenceEquals(_itemTypeToken, typeof(TItem)))
        {
            throw new InvalidOperationException($"GridView is already configured for item type '{((Type)_itemTypeToken).Name}'. Create a new GridView for a different TItem.");
        }
    }

    private static IReadOnlyList<GridViewCore.ColumnDefinition> ConvertColumns<TItem>(IReadOnlyList<GridViewColumn<TItem>> columns)
    {
        var list = new List<GridViewCore.ColumnDefinition>(columns.Count);
        for (int i = 0; i < columns.Count; i++)
        {
            var c = columns[i];
            if (c.CellTemplate == null)
            {
                throw new InvalidOperationException("GridViewColumn.CellTemplate is required.");
            }

            list.Add(new GridViewCore.ColumnDefinition(c.Header, c.Width, c.CellTemplate));
        }

        return list;
    }

    protected override bool VisitScrollChildren(Func<Element, bool> visitor)
        => visitor(_header) && visitor(_scrollViewer);

    protected override Size MeasureContent(Size availableSize)
    {
        var dpiScale = GetDpi() / 96.0;
        var borderInset = GetBorderVisualInset();

        double widthLimit = double.IsPositiveInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width - Padding.HorizontalThickness - borderInset * 2);

        _columnsExtentWidth = 0;
        for (int i = 0; i < _core.Columns.Count; i++)
        {
            _columnsExtentWidth += Math.Max(0, _core.Columns[i].Width);
        }

        double contentWidth = double.IsPositiveInfinity(widthLimit)
            ? _columnsExtentWidth
            : Math.Min(_columnsExtentWidth, widthLimit);

        double headerH = ResolveHeaderHeight();
        double rowH = ResolveRowHeight();

        int count = _core.ItemsSource.Count;
        _rowsExtentHeight = count > 0 && rowH > 0 ? count * rowH : 0;

        double desiredRowsHeight;
        if (double.IsPositiveInfinity(availableSize.Height))
        {
            desiredRowsHeight = _rowsExtentHeight <= 0 ? 0 : Math.Min(_rowsExtentHeight, MaxAutoViewportHeight);
        }
        else
        {
            desiredRowsHeight = Math.Max(0, availableSize.Height - headerH - Padding.VerticalThickness - borderInset * 2);
        }

        _presenter.ItemHeight = rowH;
        _presenter.ExtentWidth = _columnsExtentWidth;

        _header.HorizontalOffset = _scrollViewer.HorizontalOffset;
        _header.Measure(new Size(Math.Max(0, contentWidth), headerH));

        _scrollViewer.Measure(new Size(
            double.IsPositiveInfinity(contentWidth) ? double.PositiveInfinity : Math.Max(0, contentWidth),
            double.IsPositiveInfinity(desiredRowsHeight) ? double.PositiveInfinity : Math.Max(0, desiredRowsHeight)));

        var desired = new Size(Math.Max(0, contentWidth), Math.Max(0, headerH + desiredRowsHeight));
        return desired
            .Inflate(Padding)
            .Inflate(new Thickness(borderInset));
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var dpiScale = GetDpi() / 96.0;
        var borderInset = GetBorderVisualInset();

        var snapped = GetSnappedBorderBounds(bounds);
        var innerBounds = snapped.Deflate(new Thickness(borderInset));
        var contentBounds = innerBounds.Deflate(Padding);

        double headerH = ResolveHeaderHeight();

        _rowsViewportWidth = LayoutRounding.RoundToPixel(Math.Max(0, contentBounds.Width), dpiScale);
        _rowsViewportHeight = LayoutRounding.RoundToPixel(Math.Max(0, contentBounds.Height - headerH), dpiScale);

        _header.HorizontalOffset = _scrollViewer.HorizontalOffset;
        _header.Arrange(new Rect(contentBounds.X, contentBounds.Y, Math.Max(0, contentBounds.Width), headerH));

        var rowsViewport = new Rect(
            contentBounds.X,
            contentBounds.Y + headerH,
            Math.Max(0, contentBounds.Width),
            Math.Max(0, contentBounds.Height - headerH));
        _scrollViewer.Arrange(rowsViewport);

        if (!_scrollIntoViewRequest.IsNone)
        {
            var request = _scrollIntoViewRequest;
            _scrollIntoViewRequest.Clear();

            if (request.Kind == ScrollIntoViewRequestKind.Selected)
            {
                ScrollSelectedIntoView();
            }
            else if (request.Kind == ScrollIntoViewRequestKind.Index)
            {
                ScrollIntoView(request.Index);
            }
        }

        // Ensure newly realized items bind against the latest state.
        _presenter.RebindExisting = _rebindVisibleOnNextRender;
        _rebindVisibleOnNextRender = false;
    }

    public override void Render(IGraphicsContext context)
    {
        if (!IsVisible)
        {
            return;
        }

        OnRender(context);

        var dpiScale = GetDpi() / 96.0;
        var borderInset = GetBorderVisualInset();

        var contentBounds = GetSnappedBorderBounds(Bounds)
            .Deflate(new Thickness(borderInset))
            .Deflate(Padding);

        var clipRect = LayoutRounding.MakeClipRect(contentBounds, dpiScale);
        var clipRadius = LayoutRounding.RoundToPixel(Math.Max(0, Theme.Metrics.ControlCornerRadius - BorderThickness), dpiScale);
        clipRadius = Math.Min(clipRadius, Math.Min(clipRect.Width, clipRect.Height) / 2);

        context.Save();
        if (clipRadius > 0)
        {
            context.SetClipRoundedRect(clipRect, clipRadius, clipRadius);
        }
        else
        {
            context.SetClip(clipRect);
        }

        try
        {
            _header.Render(context);
            _scrollViewer.Render(context);
        }
        finally
        {
            context.Restore();
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var theme = Theme;
        var bounds = GetSnappedBorderBounds(Bounds);
        var bg = IsEnabled ? Background : theme.Palette.DisabledControlBackground;

        var borderColor = BorderBrush;
        if (IsEnabled)
        {
            if (IsFocused)
            {
                borderColor = theme.Palette.Accent;
            }
            else if (IsMouseOver)
            {
                borderColor = BorderBrush.Lerp(theme.Palette.Accent, 0.6);
            }
        }

        DrawBackgroundAndBorder(context, bounds, bg, borderColor, theme.Metrics.ControlCornerRadius);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        // IMPORTANT: children may be arranged outside this control's bounds due to scrolling.
        // Hit testing must be clipped to the control bounds to avoid "ghost" input on clipped content.
        if (!Bounds.Contains(point))
        {
            return null;
        }

        var scrollHit = _scrollViewer.HitTest(point);
        if (scrollHit != null)
        {
            return scrollHit;
        }

        var headerHit = _header.HitTest(point);
        if (headerHit != null)
        {
            return headerHit;
        }

        return Bounds.Contains(point) ? this : null;
    }

    private void BeforeRowRender(IGraphicsContext context, int index, Rect itemRect)
    {
        if (!ZebraStriping)
        {
            return;
        }

        if ((index & 1) == 1)
        {
            var theme = Theme;
            var snapped = LayoutRounding.SnapViewportRectToPixels(itemRect, GetDpi() / 96.0);
            context.FillRectangle(snapped, theme.Palette.ControlBackground.Lerp(theme.Palette.ButtonFace, theme.IsDark ? 0.45 : 0.33));
        }
    }

    private double ComputeColumnsExtentWidth()
    {
        double total = 0;
        for (int i = 0; i < _core.Columns.Count; i++)
        {
            total += Math.Max(0, _core.Columns[i].Width);
        }
        return total;
    }

    private void BindRowTemplate(FrameworkElement element, object? item, int index, TemplateContext _)
    {
        var row = (Row)element;
        row.EnsureDpi(GetDpi());
        row.EnsureColumns(_core.Columns, _core.ColumnsVersion);
        row.EnsureTheme(ThemeInternal);
        row.Bind(item, index);
    }

    private void OnItemsChanged(ItemsChange _)
    {
        _presenter.ItemsSource = _core.ItemsSource;
        _presenter.RecycleAll();
        _rebindVisibleOnNextRender = true;
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    private void OnItemsSelectionChanged()
    {
        SelectionChanged?.Invoke(SelectedItem);
        _rebindVisibleOnNextRender = true;
        ScrollSelectedIntoView();
        InvalidateVisual();
    }

    private void ScrollSelectedIntoView()
    {
        int index = SelectedIndex;
        int count = _core.ItemsSource.Count;
        if (index < 0 || index >= count)
        {
            return;
        }

        double viewport = _rowsViewportHeight;
        if (viewport <= 0 || double.IsNaN(viewport) || double.IsInfinity(viewport))
        {
            _scrollIntoViewRequest = ScrollIntoViewRequest.Selected();
            return;
        }

        double rowH = ResolveRowHeight();
        if (rowH <= 0 || double.IsNaN(rowH) || double.IsInfinity(rowH))
        {
            return;
        }

        _rowsExtentHeight = count * rowH;

        double oldOffset = _scrollViewer.VerticalOffset;
        double newOffset = ItemsViewportMath.ComputeScrollOffsetToBringItemIntoView(index, rowH, viewport, oldOffset);
        if (!newOffset.Equals(oldOffset))
        {
            _scrollViewer.SetScrollOffsets(_scrollViewer.HorizontalOffset, newOffset);
        }
    }

    public void ScrollIntoView(int index)
    {
        int count = _core.ItemsSource.Count;
        if (index < 0 || index >= count)
        {
            return;
        }

        double viewport = _rowsViewportHeight;
        if (viewport <= 0 || double.IsNaN(viewport) || double.IsInfinity(viewport))
        {
            _scrollIntoViewRequest = ScrollIntoViewRequest.IndexRequest(index);
            return;
        }

        double rowH = ResolveRowHeight();
        if (rowH <= 0 || double.IsNaN(rowH) || double.IsInfinity(rowH))
        {
            return;
        }

        _rowsExtentHeight = count * rowH;

        double oldOffset = _scrollViewer.VerticalOffset;
        double newOffset = ItemsViewportMath.ComputeScrollOffsetToBringItemIntoView(index, rowH, viewport, oldOffset);
        if (!newOffset.Equals(oldOffset))
        {
            _scrollViewer.SetScrollOffsets(_scrollViewer.HorizontalOffset, newOffset);
        }
    }

    private double ResolveRowHeight()
    {
        if (!double.IsNaN(RowHeight) && RowHeight > 0)
        {
            return RowHeight;
        }

        return Math.Max(Theme.Metrics.BaseControlHeight, 24);
    }

    private double ResolveHeaderHeight()
    {
        if (!double.IsNaN(HeaderHeight) && HeaderHeight > 0)
        {
            return HeaderHeight;
        }

        return Math.Max(Theme.Metrics.BaseControlHeight, 24);
    }

    private sealed class HeaderRow : Panel
    {
        private readonly GridView _owner;
        private readonly List<Label> _cells = new();
        private double _horizontalOffset;

        public HeaderRow(GridView owner) => _owner = owner;

        public double HorizontalOffset
        {
            get => _horizontalOffset;
            set
            {
                if (SetDouble(ref _horizontalOffset, value))
                {
                    InvalidateArrange();
                    InvalidateVisual();
                }
            }
        }

        public void SetColumns(IReadOnlyList<GridViewCore.ColumnDefinition> columns)
        {
            while (_cells.Count < columns.Count)
            {
                var text = new Label { Parent = this, VerticalTextAlignment = TextAlignment.Center };
                _cells.Add(text);
                Add(text);
            }

            while (_cells.Count > columns.Count)
            {
                RemoveAt(_cells.Count - 1);
                _cells.RemoveAt(_cells.Count - 1);
            }

            for (int i = 0; i < columns.Count; i++)
            {
                _cells[i].Text = columns[i].Header;
                _cells[i].Padding = new Thickness(6, 0, 6, 0);
            }
        }

        protected override Size MeasureContent(Size availableSize)
        {
            foreach (var cell in _cells)
            {
                cell.Measure(new Size(double.PositiveInfinity, availableSize.Height));
            }

            return new Size(availableSize.Width, availableSize.Height);
        }

        protected override void ArrangeContent(Rect bounds)
        {
            double x = bounds.X - HorizontalOffset;
            for (int i = 0; i < _cells.Count; i++)
            {
                double w = Math.Max(0, _owner._core.Columns[i].Width);
                _cells[i].Arrange(new Rect(x, bounds.Y, w, bounds.Height));
                x += w;
            }
        }

        protected override void OnRender(IGraphicsContext context)
        {
            var theme = Theme;
            var bounds = GetSnappedBorderBounds(Bounds);
            var bg = theme.Palette.ButtonFace;

            context.FillRectangle(bounds, bg);

            var stroke = theme.Palette.ControlBorder;

            // Simple bottom separator.
            var dpiScale = GetDpi() / 96.0;
            var thickness = LayoutRounding.SnapThicknessToPixels(1.0 / dpiScale, dpiScale, 1);
            var rect = LayoutRounding.SnapBoundsRectToPixels(
                new Rect(bounds.X, bounds.Bottom - thickness, Math.Max(0, bounds.Width), thickness),
                dpiScale);
            context.FillRectangle(rect, Theme.Palette.ControlBorder);

            double x = bounds.X - HorizontalOffset;
            double inset = Math.Min(6, Math.Max(0, (bounds.Height - 2) / 2));
            for (int i = 0; i < _owner._core.Columns.Count; i++)
            {
                x += Math.Max(0, _owner._core.Columns[i].Width);
                if (x >= bounds.Right - 0.5)
                {
                    break;
                }

                context.DrawLine(new Point(x, bounds.Y + inset), new Point(x, bounds.Bottom - inset), stroke, 1);
            }
        }
    }

    private sealed class Row : Panel
    {
        private readonly GridView _owner;
        private readonly List<Cell> _cells = new();
        private int _rowIndex;
        private uint _lastDpi;
        private int _lastColumnsVersion = -1;
        private Theme? _lastTheme;

        public Row(GridView owner)
        {
            _owner = owner;
            IsHitTestVisible = true;
        }

        protected override bool InvalidateOnMouseOverChanged => true;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);

            if (e.Handled || e.Button != MouseButton.Left)
            {
                return;
            }

            if (!_owner.IsEffectivelyEnabled)
            {
                return;
            }

            _owner.SelectedIndex = _rowIndex;
        }

        public void EnsureDpi(uint dpi)
        {
            if (_lastDpi == dpi)
            {
                return;
            }

            var old = _lastDpi;
            _lastDpi = dpi;

            VisualTree.Visit(this, e =>
            {
                if (e is Control c)
                {
                    c.NotifyDpiChanged(old, dpi);
                }
            });

            InvalidateMeasure();
        }

        public void EnsureColumns(IReadOnlyList<GridViewCore.ColumnDefinition> columns, int columnsVersion)
        {
            if (_lastColumnsVersion == columnsVersion)
            {
                return;
            }

            _lastColumnsVersion = columnsVersion;

            while (_cells.Count < columns.Count)
            {
                var ctx = new TemplateContext();
                var cell = new Cell(this, ctx);
                _cells.Add(cell);
                Add(cell.View);
            }

            while (_cells.Count > columns.Count)
            {
                int idx = _cells.Count - 1;
                _cells[idx].Context.Dispose();
                RemoveAt(idx);
                _cells.RemoveAt(idx);
            }

            for (int i = 0; i < columns.Count; i++)
            {
                _cells[i].Template = columns[i].CellTemplate;
                _cells[i].EnsureViewBuilt(this);
            }

            InvalidateMeasure();
        }

        public void EnsureTheme(Theme theme)
        {
            if (ReferenceEquals(_lastTheme, theme))
            {
                return;
            }

            // If this row was recycled during a theme change, it won't be in the window visual tree and will miss
            // the broadcast. Sync the whole subtree on reuse so templates don't render with a stale cached ThemeInternal.
            _lastTheme = theme;
            VisualTree.Visit(this, e =>
            {
                if (e is FrameworkElement fe && !ReferenceEquals(fe.ThemeInternal, theme))
                {
                    fe.NotifyThemeChanged(fe.ThemeInternal, theme);
                }
            });
        }

        public void Bind(object? item, int index)
        {
            _rowIndex = index;
            for (int i = 0; i < _cells.Count; i++)
            {
                _cells[i].Context.Reset();
                _cells[i].Template!.Bind(_cells[i].View, item, index, _cells[i].Context);
            }

            InvalidateMeasure();
        }

        public void Recycle()
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                _cells[i].Context.Reset();
            }

            InvalidateMeasure();
        }

        protected override Size MeasureContent(Size availableSize)
        {
            var pad = _owner.CellPadding;
            double padH = pad.HorizontalThickness;
            double padV = pad.VerticalThickness;
            for (int i = 0; i < _cells.Count; i++)
            {
                double w = Math.Max(0, _owner._core.Columns[i].Width - padH);
                double h = Math.Max(0, availableSize.Height - padV);
                _cells[i].View.Measure(new Size(w, h));
            }

            return new Size(availableSize.Width, availableSize.Height);
        }

        protected override void ArrangeContent(Rect bounds)
        {
            double x = bounds.X;
            var pad = _owner.CellPadding;
            for (int i = 0; i < _cells.Count; i++)
            {
                double w = Math.Max(0, _owner._core.Columns[i].Width);
                var cellRect = new Rect(
                    x + pad.Left,
                    bounds.Y + pad.Top,
                    Math.Max(0, w - pad.HorizontalThickness),
                    Math.Max(0, bounds.Height - pad.VerticalThickness));
                _cells[i].View.Arrange(cellRect);
                x += w;
            }
        }

        protected override void OnRender(IGraphicsContext context)
        {
            var theme = Theme;
            var snapped = GetSnappedBorderBounds(Bounds);
            var isSelected = _rowIndex == _owner.SelectedIndex;

            var r = theme.Metrics.ControlCornerRadius - 2;
            if (isSelected)
            {
                if (r > 0)
                {
                    context.FillRoundedRectangle(snapped, r, r, theme.Palette.SelectionBackground);
                }
                else
                {
                    context.FillRectangle(snapped, theme.Palette.SelectionBackground);
                }
            }
            else if (IsMouseOver && _owner.IsEffectivelyEnabled)
            {
                var hoverBg = theme.Palette.ControlBackground.Lerp(theme.Palette.Accent, 0.15);

                if (r > 0)
                {
                    context.FillRoundedRectangle(snapped, r, r, hoverBg);
                }
                else
                {
                    context.FillRectangle(snapped, hoverBg);
                }
            }

            if (_owner.ShowGridLines)
            {
                var stroke = theme.Palette.ControlBorder;
                context.DrawLine(new Point(snapped.X, snapped.Bottom - 1), new Point(snapped.Right, snapped.Bottom - 1), stroke, 1);

                double x = snapped.X;
                for (int i = 0; i < _owner._core.Columns.Count; i++)
                {
                    x += Math.Max(0, _owner._core.Columns[i].Width);
                    if (x >= snapped.Right - 0.5)
                    {
                        break;
                    }

                    context.DrawLine(new Point(x, snapped.Y), new Point(x, snapped.Bottom), stroke, 1);
                }
            }
        }

        private sealed class Cell
        {
            private readonly Row _row;
            private bool _built;
            private bool _selectionHooked;

            public Cell(Row row, TemplateContext context)
            {
                _row = row;
                Context = context;
                View = new Label();
            }

            public TemplateContext Context { get; }

            public IDataTemplate? Template { get; set; }

            public FrameworkElement View { get; private set; }

            public void EnsureViewBuilt(Row row)
            {
                if (_built || Template == null)
                {
                    return;
                }

                var built = Template.Build(Context);
                built.Parent = row;

                int idx = -1;
                for (int i = 0; i < row.Children.Count; i++)
                {
                    if (ReferenceEquals(row.Children[i], View))
                    {
                        idx = i;
                        break;
                    }
                }

                if (idx >= 0)
                {
                    row.RemoveAt(idx);
                    row.Insert(idx, built);
                }

                View = built;
                _built = true;

                HookSelection(View);
            }

            private static void TraverseVisualTree(Element? element, Action<Element> visitor)
            {
                if (element == null)
                {
                    return;
                }

                visitor(element);

                if (element is Panel panel)
                {
                    foreach (var child in panel.Children)
                    {
                        TraverseVisualTree(child, visitor);
                    }

                    return;
                }

                if (element is HeaderedContentControl headered && headered.Content != null)
                {
                    TraverseVisualTree(headered.Content, visitor);
                    return;
                }

                if (element is ContentControl contentControl && contentControl.Content != null)
                {
                    TraverseVisualTree(contentControl.Content, visitor);
                }
            }

            private void HookSelection(UIElement view)
            {
                if (_selectionHooked)
                {
                    return;
                }

                _selectionHooked = true;
                TraverseVisualTree(view, element =>
                {
                    if (element is UIElement ui)
                    {
                        ui.MouseDown += OnCellMouseDown;
                    }
                });
            }

            private void OnCellMouseDown(MouseEventArgs e)
            {
                if (e.Button != MouseButton.Left)
                {
                    return;
                }

                if (e.Handled)
                {
                    return;
                }

                if (!_row._owner.IsEffectivelyEnabled)
                {
                    return;
                }

                _row._owner.SelectedIndex = _row._rowIndex;
            }
        }
    }

    internal sealed class GridViewCore
    {
        internal readonly record struct ColumnDefinition(string Header, double Width, IDataTemplate CellTemplate);

        private ISelectableItemsView _itemsView = ItemsView.EmptySelectable;
        private readonly List<ColumnDefinition> _columns = new();
        private int _columnsVersion;

        public IReadOnlyList<ColumnDefinition> Columns => _columns;

        public int ColumnsVersion => _columnsVersion;

        public ISelectableItemsView ItemsSource => _itemsView;

        public int SelectedIndex
        {
            get => _itemsView.SelectedIndex;
            set
            {
                int next;
                if (_itemsView.Count == 0)
                {
                    next = -1;
                }
                else
                {
                    next = Math.Clamp(value, -1, _itemsView.Count - 1);
                }

                if (_itemsView.SelectedIndex == next)
                {
                    return;
                }

                _itemsView.SelectedIndex = next;
            }
        }

        public object? SelectedItem => _itemsView.SelectedItem;

        public event Action<ItemsChange>? ItemsChanged;
        public event Action<object?>? SelectionChanged;
        public event Action? ColumnsChanged;

        public void SetItems(ISelectableItemsView itemsView)
        {
            ArgumentNullException.ThrowIfNull(itemsView);

            var old = _itemsView;
            int previousSelectedIndex = old.SelectedIndex;
            UnhookItemsView(old);

            _itemsView = itemsView;
            HookItemsView(_itemsView);

            if (previousSelectedIndex != -1)
            {
                _itemsView.SelectedIndex = previousSelectedIndex;
            }

            ItemsChanged?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, _itemsView.Count));
        }

        public void SetColumns(IReadOnlyList<ColumnDefinition> columns)
        {
            ArgumentNullException.ThrowIfNull(columns);

            _columns.Clear();
            for (int i = 0; i < columns.Count; i++)
            {
                _columns.Add(columns[i]);
            }

            _columnsVersion++;
            ColumnsChanged?.Invoke();
        }

        public void AddColumns(IReadOnlyList<ColumnDefinition> columns)
        {
            ArgumentNullException.ThrowIfNull(columns);

            for (int i = 0; i < columns.Count; i++)
            {
                _columns.Add(columns[i]);
            }

            _columnsVersion++;
            ColumnsChanged?.Invoke();
        }

        private void HookItemsView(ISelectableItemsView view)
        {
            view.Changed += OnItemsChanged;
            view.SelectionChanged += OnItemsSelectionChanged;
        }

        private void UnhookItemsView(ISelectableItemsView view)
        {
            view.Changed -= OnItemsChanged;
            view.SelectionChanged -= OnItemsSelectionChanged;
        }

        private void OnItemsChanged(ItemsChange change) => ItemsChanged?.Invoke(change);

        private void OnItemsSelectionChanged(int _) => SelectionChanged?.Invoke(SelectedItem);
    }
}
