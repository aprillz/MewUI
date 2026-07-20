using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class GridView : ScrollableItemsBase, IFocusIntoViewHost, IVirtualizedTabNavigationHost, ISelector, IIndexedSelector, IMultiSelector
{
    public static readonly MewProperty<bool> ZebraStripingProperty =
        MewProperty<bool>.Register<GridView>(nameof(ZebraStriping), true, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<bool> ShowGridLinesProperty =
        MewProperty<bool>.Register<GridView>(nameof(ShowGridLines), false, MewPropertyOptions.AffectsRender);

    public static readonly MewProperty<double> RowHeightProperty =
        MewProperty<double>.Register<GridView>(nameof(RowHeight), double.NaN, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<double> HeaderHeightProperty =
        MewProperty<double>.Register<GridView>(nameof(HeaderHeight), double.NaN, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<Thickness> CellPaddingProperty =
        MewProperty<Thickness>.Register<GridView>(nameof(CellPadding), default, MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnCellPaddingChanged());

    public static readonly MewProperty<double> MaxAutoViewportHeightProperty =
        MewProperty<double>.Register<GridView>(nameof(MaxAutoViewportHeight), 320.0, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<int> SelectedIndexProperty =
        MewProperty<int>.Register<GridView>(nameof(SelectedIndex), -1,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnSelectedIndexPropertyChanged(newVal));

    public static readonly MewProperty<ItemsSelectionMode> SelectionModeProperty =
        MewProperty<ItemsSelectionMode>.Register<GridView>(nameof(SelectionMode), ItemsSelectionMode.Single,
            MewPropertyOptions.None,
            static (self, _, newVal) => self.OnSelectionModePropertyChanged(newVal));

    public static readonly MewProperty<object?> SelectedItemProperty =
        MewProperty<object?>.Register<GridView>(nameof(SelectedItem), null,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnSelectedItemPropertyChanged(newVal));

    private static readonly MewPropertyKey<IReadOnlyList<object?>> SelectedItemsPropertyKey =
        MewProperty<IReadOnlyList<object?>>.RegisterReadOnly<GridView>(nameof(SelectedItems), Array.Empty<object?>());
    public static readonly MewProperty<IReadOnlyList<object?>> SelectedItemsProperty = SelectedItemsPropertyKey.Property;

    private object? _itemTypeToken;
    private readonly GridViewCore _core = new();
    private readonly SelectionSync _selection;

    private readonly HeaderRow _header;
    private IItemsPresenter _presenter;
    private readonly IDataTemplate _rowTemplate;

    private double _rowsExtentHeight;
    private double _columnsExtentWidth;
    private double _rowsViewportHeight;
    private double _rowsViewportWidth;

    public GridView()
    {
        _selection = new SelectionSync(() => _core.ItemsSource,
            value => SetValue(SelectedIndexProperty, value),
            value => SetValue(SelectedItemProperty, value),
            value => SetValue(SelectedItemsPropertyKey, value));

        CellPadding = Theme.Metrics.ItemPadding;

        _scrollViewer.Padding = new Thickness(0);
        _scrollViewer.CornerRadius = 0;

        _header = new HeaderRow(this) { Parent = this };

        _rowTemplate = new DelegateTemplate<object?>(
            build: _ => new Row(this),
            bind: BindRowTemplate);

        _presenter = CreateDefaultPresenter();
        InitializePresenter(_presenter);

        _scrollViewer.Content = (UIElement)_presenter;
        _scrollViewer.ScrollChanged += () =>
        {
            _header.HorizontalOffset = _scrollViewer.HorizontalOffset;
        };

        _core.ItemsChanged += OnItemsChanged;
        _core.SelectionChanged += _ => OnItemsSelectionChanged();
        _core.SelectedIndicesChanged += () =>
        {
            _selection.SyncFromModel();
            SelectedIndicesChanged?.Invoke();
            InvalidateItemBindings();
            InvalidateVisual();
        };
        _core.SortChanged += change =>
        {
            _header.InvalidateMeasure();
            _header.InvalidateVisual();
            SortChanged?.Invoke(change);
        };
        _core.ColumnsChanged += () =>
        {
            _header.SetColumns(_core.Columns);
            _presenter.RecycleAll();
            InvalidateItemBindings();
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

    /// <summary>Occurs when the active single-column sort changes.</summary>
    public event Action<GridViewSortChange>? SortChanged;

    public bool ZebraStriping
    {
        get => GetValue(ZebraStripingProperty);
        set => SetValue(ZebraStripingProperty, value);
    }

    public bool ShowGridLines
    {
        get => GetValue(ShowGridLinesProperty);
        set => SetValue(ShowGridLinesProperty, value);
    }

    public double RowHeight
    {
        get => GetValue(RowHeightProperty);
        set => SetValue(RowHeightProperty, value);
    }

    public double HeaderHeight
    {
        get => GetValue(HeaderHeightProperty);
        set => SetValue(HeaderHeightProperty, value);
    }

    public Thickness CellPadding
    {
        get => GetValue(CellPaddingProperty);
        set => SetValue(CellPaddingProperty, value);
    }

    public double MaxAutoViewportHeight
    {
        get => GetValue(MaxAutoViewportHeightProperty);
        set => SetValue(MaxAutoViewportHeightProperty, value);
    }

    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>
    /// Gets or sets the selection mode. Requires a multi-selection-capable items source
    /// (e.g. from <see cref="ItemsView.Create{T}(IReadOnlyList{T}, System.Func{T, string}, System.Func{T, object})"/>);
    /// otherwise stays <see cref="ItemsSelectionMode.Single"/>.
    /// </summary>
    public ItemsSelectionMode SelectionMode
    {
        get => GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    private void OnSelectionModePropertyChanged(ItemsSelectionMode mode)
        => _core.SelectionMode = mode;

    /// <summary>Gets the selected row indices in ascending order.</summary>
    public IReadOnlyList<int> SelectedIndices => _selection.SelectedIndices;

    /// <summary>Gets the selected items in ascending index order (read-only, bindable).</summary>
    public IReadOnlyList<object?> SelectedItems => GetValue(SelectedItemsProperty);

    /// <summary>Returns whether the row at <paramref name="index"/> is selected.</summary>
    public bool IsSelected(int index) => _selection.IsSelected(index);

    /// <summary>Selects every row (multi-selection modes only; no-op otherwise).</summary>
    public void SelectAll() => _selection.SelectAll();

    /// <summary>Clears the entire selection.</summary>
    public void ClearSelection() => _selection.ClearSelection();

    /// <summary>Selects the inclusive range [start, end], replacing the current selection (multi only).</summary>
    public void SelectRange(int start, int end) => _selection.SelectRange(start, end);

    /// <summary>Occurs when the set of selected rows changes (multi-select).</summary>
    public event Action? SelectedIndicesChanged;

    // The pointer-down event that already applied selection. Row and cell handlers both funnel
    // here with the same args instance; comparing it deduplicates without marking the event
    // Handled, which would suppress interactive cell content (e.g. a ComboBox opening its popup).
    private MouseEventArgs? _lastSelectionAppliedEvent;

    // Shared selection entry for row/cell pointer-down. Selection observes the click, it does not consume it.
    internal void HandleRowPointerDown(int rowIndex, MouseEventArgs e)
    {
        if (!IsEffectivelyEnabled)
        {
            return;
        }

        if (ReferenceEquals(_lastSelectionAppliedEvent, e))
        {
            return;
        }
        _lastSelectionAppliedEvent = e;

        var multi = _core.MultiView;
        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
        {
            ItemsSelectionInput.HandleClick(multi, rowIndex, e.Modifiers);
        }
        else
        {
            SelectedIndex = rowIndex;
        }
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _core.ResetAutoDesiredWidths();
        InvalidateItemBindings();
        InvalidateMeasure();
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
        var multi = _core.MultiView;

        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single && IsSelectAllShortcut(e))
        {
            multi.SelectRange(0, count - 1, clearExisting: true);
            e.Handled = true;
            Focus();
            InvalidateVisual();
            return;
        }

        int target = current;
        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
            case Key.Home:
            case Key.End:
            case Key.PageUp:
            case Key.PageDown:
                e.Handled = TryGetListNavigationTarget(e.Key, current, count, ResolvePageStep(count), supportsPaging: true, out target);
                break;
        }

        if (e.Handled)
        {
            if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
            {
                ItemsSelectionInput.HandleKeyboardMove(multi, target, (e.Modifiers & ModifierKeys.Shift) != 0);
            }
            else
            {
                SelectedIndex = target;
            }

            Focus();
            InvalidateVisual();
        }
    }

    private void OnCellPaddingChanged()
    {
        _core.ResetAutoDesiredWidths();
        InvalidateItemBindings();
    }

    private void InvalidateGridItemBindings() => InvalidateItemBindings();

    private int ResolvePageStep(int count)
    {
        double rowH = GetPixelAlignedRowHeight();
        if (rowH <= 0)
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

        if (!TryFindRealizedIndex(_presenter, focusedElement, out int found, out _) || found >= _core.ItemsSource.Count)
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

        if (!TryFindRealizedIndex(_presenter, focusedElement, out int found, out var foundContainer))
        {
            return false;
        }

        // Check whether there are more focusable elements in this container.
        var edge = moveForward
            ? FocusManager.FindLastFocusable(foundContainer)
            : FocusManager.FindFirstFocusable(foundContainer);
        bool hasMoreFocusable = edge != null && !ReferenceEquals(edge, focusedElement);

        if (hasMoreFocusable)
        {
            if (IsItemInViewport(found))
            {
                // Item is on-screen - let normal Tab navigation handle intra-item movement.
                return false;
            }

            // Item is off-screen (focus-pinned). We can't return false here because
            // FocusManager's flat-list Tab would move focus within the same container,
            // then ScrollViewer.OnDescendantFocused fires first (before GridView) and
            // uses the element's stale Bounds - resulting in no vertical scroll.
            // Instead, scroll this item into view and move focus ourselves.
            ScrollIntoView(found);
            var next = FindNextFocusableInContainer(foundContainer, focusedElement, moveForward);
            if (next != null && FindVisualRoot() is Window window)
            {
                window.FocusManager.SetFocus(next);
                return true;
            }

            return false;
        }

        int targetIndex = moveForward ? found + 1 : found - 1;
        if (targetIndex < 0 || targetIndex >= _core.ItemsSource.Count)
        {
            return false;
        }

        SelectedIndex = targetIndex;
        ScrollIntoView(targetIndex);
        _tabFocusHelper.Schedule(targetIndex, moveForward);
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
        bool handled = false;

        if (canVScroll && e.Delta.Y != 0)
        {
            _scrollViewer.ScrollBy(-e.Delta.Y);
            handled = true;
        }
        if (canHScroll && e.Delta.X != 0)
        {
            _scrollViewer.ScrollByHorizontal(-e.Delta.X);
            handled = true;
        }
        if (handled)
        {
            e.Handled = true;
        }
    }

    /// <summary>
    /// Gets or sets the data source. Assign an <see cref="ItemsView{T}"/> (e.g. from
    /// <see cref="ItemsView.Create{T}(IReadOnlyList{T}, System.Func{T, string}, System.Func{T, object})"/>)
    /// to display typed rows alongside typed columns.
    /// </summary>
    public ISelectableItemsView ItemsSource
    {
        get => _core.SourceItemsView;
        set => _core.SetItems(value ?? ItemsView.EmptySelectable);
    }

    /// <summary>Gets the active sorted column index, or -1 when source order is displayed.</summary>
    public int SortColumnIndex => _core.SortColumnIndex;

    /// <summary>Gets the active single-column sort direction.</summary>
    public GridViewSortDirection SortDirection => _core.SortDirection;

    /// <summary>Applies a local sort using the comparer registered on the specified column.</summary>
    public void SortByColumn(int columnIndex, GridViewSortDirection direction)
        => _core.SortByColumn(columnIndex, direction);

    /// <summary>Clears the active sort and restores current source order.</summary>
    public void ClearSort() => _core.ClearSort();

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

        double rowH = GetPixelAlignedRowHeight();
        if (rowH <= 0)
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
            double w = Math.Max(0, _core.Columns[i].ActualWidth);
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

            ValidateColumn(c);
            var sortComparer = c.SortComparer;
            list.Add(new GridViewCore.ColumnDefinition(
                c.Header,
                c.Width,
                c.MinWidth,
                c.MaxWidth,
                c.IsResizable,
                c.CellTemplate,
                sortComparer == null
                    ? null
                    : (left, right) => sortComparer.Compare((TItem)left!, (TItem)right!)));
        }

        return list;
    }

    private static void ValidateColumn<TItem>(GridViewColumn<TItem> column)
    {
        var width = column.Width;
        switch (width.GridUnitType)
        {
            case GridUnitType.Auto:
                break;
            case GridUnitType.Pixel:
                if (!double.IsFinite(width.Value) || width.Value < 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(column.Width), "Pixel width must be finite and non-negative.");
                }
                break;
            case GridUnitType.Star:
                if (!double.IsFinite(width.Value) || width.Value <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(column.Width), "Star weight must be finite and greater than zero.");
                }
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(column.Width), "Unknown GridUnitType.");
        }

        if (!double.IsFinite(column.MinWidth) || column.MinWidth < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(column.MinWidth), "MinWidth must be finite and non-negative.");
        }

        if (double.IsNaN(column.MaxWidth) || column.MaxWidth < 0 || double.IsNegativeInfinity(column.MaxWidth))
        {
            throw new ArgumentOutOfRangeException(nameof(column.MaxWidth), "MaxWidth must be non-negative or positive infinity.");
        }

        if (column.MaxWidth < column.MinWidth)
        {
            throw new ArgumentOutOfRangeException(nameof(column.MaxWidth), "MaxWidth must be greater than or equal to MinWidth.");
        }
    }

    // Topmost-first: the scroll viewer renders after (on top of) the header row.
    protected override bool VisitScrollChildren(Func<Element, bool> visitor)
        => visitor(_scrollViewer) && visitor(_header);

    protected override Size MeasureContent(Size availableSize)
    {
        var dpiScale = GetDpi() / 96.0;
        var borderInset = GetBorderVisualInset();

        double widthLimit = double.IsPositiveInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width - Padding.HorizontalThickness - borderInset * 2);

        double headerH = ResolveHeaderHeight();
        _header.MeasureAutoColumns(headerH);
        _columnsExtentWidth = _core.ResolveColumnWidths(widthLimit, out bool columnsChanged);
        if (columnsChanged)
        {
            // Measure can resolve new Auto widths after rows were realized in the previous
            // arrange round. The outer GridView bounds may be unchanged, but its header and
            // rows still need another arrange with the new column geometry.
            InvalidateArrangeForCurrentLayoutPass();
        }

        double contentWidth = double.IsPositiveInfinity(widthLimit)
            ? _columnsExtentWidth
            : Math.Min(_columnsExtentWidth, widthLimit);

        double rowH = ResolveRowHeight();
        double alignedRowH = GetPixelAlignedRowHeight();

        int count = _core.ItemsSource.Count;
        _rowsExtentHeight = count > 0 && alignedRowH > 0 ? count * alignedRowH : 0;

        double desiredRowsHeight;
        if (double.IsPositiveInfinity(availableSize.Height))
        {
            desiredRowsHeight = _rowsExtentHeight <= 0 ? 0 : Math.Min(_rowsExtentHeight, MaxAutoViewportHeight);
        }
        else
        {
            desiredRowsHeight = Math.Max(0, availableSize.Height - headerH - Padding.VerticalThickness - borderInset * 2);
        }

        _presenter.ItemHeightHint = rowH;
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

        // Measure owns the presenter's scroll extent. Arrange may legitimately resolve Star
        // columns to a different final width: for example, a WrapPanel measures its children
        // with infinite width to obtain their natural size and then arranges them into a finite
        // slot. Feeding that final width back into measure makes the columns (and presenter
        // extent) alternate between their natural minimum and the arranged viewport forever.
        //
        // Rows are measured by the presenter against the final layout width below, so updating
        // ActualWidth here is sufficient; it must not invalidate measure or rewrite the measured
        // scroll extent.
        _columnsExtentWidth = _core.ResolveColumnWidths(Math.Max(0, contentBounds.Width), out _);

        _rowsViewportWidth = LayoutRounding.RoundToPixel(Math.Max(0, contentBounds.Width), dpiScale);
        _rowsViewportHeight = LayoutRounding.RoundToPixel(Math.Max(0, contentBounds.Height - headerH), dpiScale);

        var rowsViewport = new Rect(
            contentBounds.X,
            contentBounds.Y + headerH,
            Math.Max(0, contentBounds.Width),
            Math.Max(0, contentBounds.Height - headerH));

        // Apply the rebind hint BEFORE the scroll viewer arranges the presenter so this
        // frame's arrange picks it up. Setting it after _scrollViewer.Arrange would defer
        // the rebind to the next frame, producing a one-frame flicker where the row's
        // stale _rowIndex disagrees with the just-shifted SelectedIndex (visible as the
        // selection appearing to release then reattach right after a prepend/remove).
        _presenter.ItemBindingGeneration = ItemBindingGeneration;

        _scrollViewer.Arrange(rowsViewport);

        // ScrollViewer clamps its offsets while arranging against the latest extent. Auto-fit
        // can shrink that extent, so arrange the header only after the clamp; otherwise its text
        // keeps the old offset for this frame while separators render with the new offset.
        _header.HorizontalOffset = _scrollViewer.HorizontalOffset;
        _header.Arrange(new Rect(contentBounds.X, contentBounds.Y, Math.Max(0, contentBounds.Width), headerH));

        if (TryConsumeScrollIntoViewRequest(out var request))
        {
            if (request.Kind == ScrollIntoViewRequestKind.Selected)
            {
                ScrollSelectedIntoView();
            }
            else if (request.Kind == ScrollIntoViewRequestKind.Index)
            {
                ScrollIntoView(request.Index);
            }
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        var bg = GetValue(BackgroundProperty);
        var borderColor = GetValue(BorderBrushProperty);
        DrawBackgroundAndBorder(context, bounds, bg, borderColor, BorderThickness, CornerRadius);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        var dpiScale = GetDpi() / 96.0;
        var borderInset = GetBorderVisualInset();

        var contentBounds = bounds
            .Deflate(new Thickness(borderInset))
            .Deflate(Padding);

        var clipRect = LayoutRounding.MakeClipRect(contentBounds, dpiScale);
        var clipRadius = Math.Max(0, LayoutRounding.RoundToPixel(CornerRadius, dpiScale) - borderInset);
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
            total += Math.Max(0, _core.Columns[i].ActualWidth);
        }
        return total;
    }

    private void ReportAutoDesiredWidth(int columnIndex, double desiredWidth)
    {
        if (!_core.ReportAutoDesiredWidth(columnIndex, desiredWidth))
        {
            return;
        }

        if (_presenter is VariableHeightItemsPresenter variableHeightPresenter)
        {
            variableHeightPresenter.InvalidateHeights();
        }
        InvalidateMeasure();
    }

    private void MeasureRealizedAutoColumn(int columnIndex)
    {
        _presenter.VisitRealized((_, element) =>
        {
            if (element is Row row)
            {
                row.MeasureAutoColumn(columnIndex);
            }
        });
    }

    private void InvalidateColumnSizing()
    {
        InvalidateGridItemBindings();
        if (_presenter is VariableHeightItemsPresenter variableHeightPresenter)
        {
            variableHeightPresenter.InvalidateHeights();
        }
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void BindRowTemplate(FrameworkElement element, object? item, int index, TemplateContext _)
    {
        var row = (Row)element;
        row.EnsureDpi(GetDpi());
        row.EnsureColumns(_core.Columns, _core.ColumnsVersion);
        row.EnsureTheme(ThemeInternal);
        row.Bind(item, index);
    }

    private void OnItemsChanged(ItemsChange change)
    {
        _presenter.ItemsSource = _core.ItemsSource;
        // Presenters handle Add/Remove/Replace internally (remapping realized indices,
        // updating height caches and offsets). Force a full recycle only for Reset, which
        // signals a wholesale collection change.
        if (change.Kind == ItemsChangeKind.Reset)
        {
            _core.ResetAutoDesiredWidths();
            _presenter.RecycleAll();
            if (_presenter is VariableHeightItemsPresenter variableHeightPresenter)
            {
                variableHeightPresenter.InvalidateHeights();
            }
            // A wholesale swap resets the underlying view's mode; re-apply the control-level setting.
            _core.SelectionMode = SelectionMode;
        }

        // Force rebind of visible rows only when the change can shift their indices or
        // their backing data. Pure append-at-end (Insert with Index == previous count)
        // doesn't affect any existing realized row, so triggering a rebind there causes
        // unnecessary cell-context resets - visually that flashes selection/hover off+on
        // for every appended item.
        int newCount = _core.ItemsSource.Count;
        int oldCount = newCount - (change.Kind == ItemsChangeKind.Add ? change.Count
                                  : change.Kind == ItemsChangeKind.Remove ? -change.Count
                                  : 0);
        bool needsRebind = change.Kind switch
        {
            ItemsChangeKind.Reset => true,
            ItemsChangeKind.Move => true,
            ItemsChangeKind.Replace => true,
            ItemsChangeKind.Remove => true,
            ItemsChangeKind.Add => change.Index < oldCount,  // false when appending at end
            _ => true
        };
        if (needsRebind)
        {
            InvalidateItemBindings();
        }
        // A collection change can clear/shift the selection without firing SelectionChanged
        // (e.g. SetItems resetting to -1 on a view already at -1); re-sync so the bindable
        // property never diverges from the core.
        _selection.SyncFromModel();
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    private IItemsPresenter CreateDefaultPresenter()
        => new FixedHeightItemsPresenter
        {
            BorderThickness = 0,
            Padding = new Thickness(0),
            UseHorizontalExtentForLayout = true,
        };

    private void InitializePresenter(IItemsPresenter presenter)
    {
        presenter.ItemTemplate = _rowTemplate;
        presenter.ItemsSource = _core.ItemsSource;
        presenter.BeforeItemRender = BeforeRowRender;
        presenter.UseHorizontalExtentForLayout = true;
        // Variable-height virtualization requests scroll offset corrections during INCC bursts
        // (insert/remove above the anchor) and after re-measurement refines heights. Without
        // this subscription the events are dropped and the ScrollViewer's stale offset gets
        // pushed back into the presenter on the next arrange, causing visible jumps.
        presenter.OffsetCorrectionRequested += OnPresenterOffsetCorrectionRequested;
    }

    private void OnPresenterOffsetCorrectionRequested(Point offset)
        => _scrollViewer.SetScrollOffsets(_scrollViewer.HorizontalOffset, offset.Y);

    /// <summary>
    /// Replaces the row presenter (e.g. swap fixed-height virtualization for variable-height).
    /// Default is <see cref="FixedHeightItemsPresenter"/>; use
    /// <c>VariableHeightPresenter()</c> extension for per-row measured heights.
    /// </summary>
    internal void SetPresenter(IItemsPresenter presenter)
    {
        if (presenter == null) throw new ArgumentNullException(nameof(presenter));
        if (ReferenceEquals(presenter, _presenter)) return;

        _presenter.OffsetCorrectionRequested -= OnPresenterOffsetCorrectionRequested;
        if (_presenter is IDisposable d)
        {
            d.Dispose();
        }

        InitializePresenter(presenter);
        _presenter = presenter;
        _scrollViewer.Content = (UIElement)_presenter;
        InvalidateItemBindings();
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    private void OnItemsSelectionChanged()
    {
        _selection.SyncFromModel();
        SelectionChanged?.Invoke(_core.SelectedItem);
        InvalidateItemBindings();
        ScrollSelectedIntoView();
        InvalidateVisual();
    }

    private void OnSelectedIndexPropertyChanged(int newIndex) => _selection.PushIndex(newIndex);

    private void OnSelectedItemPropertyChanged(object? item) => _selection.PushItem(item);

    private void ScrollSelectedIntoView()
        => ScrollIntoView(SelectedIndex);

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
            RequestScrollIntoView(ScrollIntoViewRequest.IndexRequest(index));
            return;
        }

        // Prefer the presenter's own y-range when it knows real (measured) item bounds ??        // e.g. VariableHeightItemsPresenter's prefix sum. Falls back to fixed-height math
        // only when the presenter can't supply a range (e.g. unmeasured items in variable
        // mode that the presenter hasn't realized yet).
        double oldOffset = _scrollViewer.VerticalOffset;
        if (_presenter.TryGetItemYRange(index, out double itemTop, out double itemBottom))
        {
            double itemH = Math.Max(1, itemBottom - itemTop);
            double newOffset = ItemsViewportMath.ComputeScrollOffsetToBringItemRangeIntoView(itemTop, itemH, viewport, oldOffset);
            if (!newOffset.Equals(oldOffset))
            {
                _scrollViewer.SetScrollOffsets(_scrollViewer.HorizontalOffset, newOffset);
            }
            return;
        }

        // Unmeasured item - defer to the presenter so it can estimate, scroll, then refine
        // after the item realizes. (Same path used when viewport isn't laid out yet.)
        _presenter.RequestScrollIntoView(index);
    }

    private static UIElement? FindNextFocusableInContainer(FrameworkElement container, UIElement current, bool forward)
    {
        var focusable = new List<UIElement>();
        CollectFocusableIn(container, focusable);
        int idx = focusable.IndexOf(current);
        if (idx < 0)
        {
            return null;
        }

        int next = forward ? idx + 1 : idx - 1;
        return next >= 0 && next < focusable.Count ? focusable[next] : null;
    }

    private static void CollectFocusableIn(Element? element, List<UIElement> result)
    {
        if (element is UIElement ui && ui.Focusable && ui.IsEffectivelyEnabled && ui.IsVisible)
        {
            result.Add(ui);
        }

        if (element is IVisualTreeHost host)
        {
            host.VisitChildren(child =>
            {
                CollectFocusableIn(child, result);
                return true;
            });
        }
    }

    private bool IsItemInViewport(int index)
    {
        double rowH = GetPixelAlignedRowHeight();
        if (rowH <= 0 || _rowsViewportHeight <= 0)
        {
            return false;
        }

        double itemTop = index * rowH;
        double itemBottom = itemTop + rowH;
        double offset = _scrollViewer.VerticalOffset;
        return itemBottom > offset && itemTop < offset + _rowsViewportHeight;
    }

    private double ResolveRowHeight()
    {
        if (!double.IsNaN(RowHeight) && RowHeight > 0)
        {
            return RowHeight;
        }

        return Math.Max(Theme.Metrics.BaseControlHeight, 24);
    }

    // Pixel-aligned row height matching the FixedHeightItemsPresenter's visual layout.
    // Needed so hit-test, extent, and viewport queries agree with the rendered rows
    // (esp. at 125% DPI where a 24 DIP row rounds to 24.8 DIP per row).
    private double GetPixelAlignedRowHeight()
    {
        double rowH = ResolveRowHeight();
        if (rowH <= 0 || double.IsNaN(rowH) || double.IsInfinity(rowH))
        {
            return 0;
        }

        return LayoutRounding.RoundToPixel(rowH, GetDpi() / 96.0);
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
        private const double SeparatorHitWidth = 6;
        private const double SortIndicatorSlotWidth = 18;

        private readonly GridView _owner;
        private readonly List<TextBlock> _cells = new();
        private double _horizontalOffset;

        // Column resize drag state
        private int _resizeColumnIndex = -1;

        private double _resizeDragStartX;
        private double _resizeDragStartWidth;
        private GridViewCore.ColumnResizeSession? _resizeSession;
        private int _pressedSortColumnIndex = -1;

        public HeaderRow(GridView owner)
        {
            _owner = owner;
            IsHitTestVisible = true;
        }

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
                var text = new TextBlock();
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
                _cells[i].Margin = columns[i].IsSortable
                    ? new Thickness(6, 0, SortIndicatorSlotWidth, 0)
                    : new Thickness(6, 0, 6, 0);
            }
        }

        protected override Size MeasureContent(Size availableSize)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                // Use actual column width as constraint so TextTrimming/Ellipsis works
                double colWidth = i < _owner._core.Columns.Count
                    ? Math.Max(0, _owner._core.Columns[i].ActualWidth)
                    : double.PositiveInfinity;
                _cells[i].Measure(new Size(colWidth, availableSize.Height));
            }

            return new Size(availableSize.Width, availableSize.Height);
        }

        public void MeasureAutoColumns(double availableHeight)
        {
            for (int i = 0; i < _cells.Count && i < _owner._core.Columns.Count; i++)
            {
                if (!_owner._core.Columns[i].Width.IsAuto)
                {
                    continue;
                }

                _cells[i].Measure(new Size(double.PositiveInfinity, Math.Max(0, availableHeight)));
                _owner._core.ReportAutoDesiredWidth(i, _cells[i].DesiredSize.Width);
            }
        }

        protected override void ArrangeContent(Rect bounds)
        {
            double x = bounds.X - HorizontalOffset;
            for (int i = 0; i < _cells.Count; i++)
            {
                double w = Math.Max(0, _owner._core.Columns[i].ActualWidth);
                _cells[i].Arrange(new Rect(x, bounds.Y, w, bounds.Height));
                x += w;
            }

            // A resize or auto-fit can move a separator under a stationary pointer. No native
            // mouse-move is generated in that case, so refresh the separator cursor from the
            // window's last pointer position after the new column bounds have been applied.
            if (_resizeColumnIndex < 0 && _pressedSortColumnIndex < 0 && IsMouseOver &&
                FindVisualRoot() is Window window)
            {
                var pointer = window.TranslatePoint(window.LastMousePositionDip, this);
                UpdateSeparatorCursor(pointer);
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
                double width = Math.Max(0, _owner._core.Columns[i].ActualWidth);
                double right = x + width;
                if (_owner._core.SortColumnIndex == i && width >= SortIndicatorSlotWidth)
                {
                    double centerX = right - SortIndicatorSlotWidth / 2;
                    if (centerX >= bounds.Left && centerX <= bounds.Right)
                    {
                        Glyph.Draw(
                            context,
                            new Point(centerX, bounds.Y + bounds.Height / 2),
                            3,
                            theme.Palette.WindowText,
                            _owner._core.SortDirection == GridViewSortDirection.Ascending
                                ? GlyphKind.ChevronUp
                                : GlyphKind.ChevronDown,
                            1);
                    }
                }

                x = right;
                if (x >= bounds.Right - 0.5)
                {
                    break;
                }

                context.DrawLine(new Point(x, bounds.Y + inset), new Point(x, bounds.Bottom - inset), stroke, 1, pixelSnap: true);
            }
        }

        private int HitTestColumn(double localX)
        {
            double x = -HorizontalOffset;
            for (int i = 0; i < _owner._core.Columns.Count; i++)
            {
                double right = x + Math.Max(0, _owner._core.Columns[i].ActualWidth);
                if (localX >= x && localX < right)
                {
                    return i;
                }
                x = right;
            }
            return -1;
        }

        /// <summary>
        /// Returns the column index whose right-edge separator is near the given X position,
        /// or -1 if no resizable separator is hit.
        /// </summary>
        private int HitTestSeparator(double localX)
        {
            var columns = _owner._core.Columns;
            double x = -HorizontalOffset;
            double nearestDistance = double.PositiveInfinity;
            double nearestBoundary = double.NaN;
            int nearestColumn = -1;
            int collapsedColumn = -1;
            for (int i = 0; i < columns.Count; i++)
            {
                double width = Math.Max(0, columns[i].ActualWidth);
                x += width;
                if (!_owner._core.CanResizeColumn(i))
                {
                    continue;
                }

                double distance = Math.Abs(localX - x);
                if (distance > SeparatorHitWidth / 2)
                {
                    continue;
                }

                if (distance < nearestDistance - 0.01)
                {
                    nearestDistance = distance;
                    nearestBoundary = x;
                    nearestColumn = i;
                    collapsedColumn = width <= 0.01 ? i : -1;
                }
                else if (Math.Abs(x - nearestBoundary) <= 0.01 &&
                    collapsedColumn < 0 && width <= 0.01)
                {
                    // A zero-width column shares its right boundary with the preceding column.
                    // Prefer the collapsed column so pointer resizing can make it visible again.
                    collapsedColumn = i;
                }
            }
            return collapsedColumn >= 0 ? collapsedColumn : nearestColumn;
        }

        private void UpdateSeparatorCursor(Point position)
        {
            bool inside = position.X >= 0 && position.X < Bounds.Width &&
                position.Y >= 0 && position.Y < Bounds.Height;
            Cursor = inside && HitTestSeparator(position.X) >= 0
                ? CursorType.SizeWE
                : null;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Handled || e.Button != MouseButton.Left) return;

            var pos = e.GetPosition(this);
            bool autoSize = e.ClickCount >= 2;
            int col = HitTestSeparator(pos.X);
            UpdateSeparatorCursor(pos);
            if (col >= 0 && autoSize)
            {
                if (_owner._core.ResetColumnToAuto(col))
                {
                    // Auto-fit is an explicit snapshot of the content currently on screen.
                    // Presenter measure does not walk realized rows, so collect their intrinsic
                    // cell widths before resolving the new column width.
                    _owner.MeasureRealizedAutoColumn(col);
                    _owner.InvalidateColumnSizing();
                    InvalidateArrange();
                    InvalidateVisual();
                }
                e.Handled = true;
                return;
            }

            if (col >= 0)
            {
                _resizeColumnIndex = col;
                _resizeDragStartX = pos.X;
                _resizeDragStartWidth = _owner._core.Columns[col].ActualWidth;
                _resizeSession = _owner._core.BeginColumnResize(col);
                if (_resizeSession == null)
                {
                    _resizeColumnIndex = -1;
                    return;
                }
                Cursor = CursorType.SizeWE;

                if (_owner.FindVisualRoot() is Window resizeWindow)
                    resizeWindow.CaptureMouse(this);

                e.Handled = true;
                return;
            }

            int sortColumn = HitTestColumn(pos.X);
            if (sortColumn < 0 || !_owner._core.Columns[sortColumn].IsSortable)
            {
                return;
            }

            _pressedSortColumnIndex = sortColumn;
            if (_owner.FindVisualRoot() is Window sortWindow)
                sortWindow.CaptureMouse(this);

            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            var pos = e.GetPosition(this);

            if (_resizeColumnIndex >= 0)
            {
                double delta = pos.X - _resizeDragStartX;
                double newWidth = _resizeDragStartWidth + delta;

                if (!_owner._core.ResizeColumn(_resizeSession!, newWidth))
                {
                    e.Handled = true;
                    return;
                }
                _owner.InvalidateColumnSizing();
                InvalidateArrange();
                InvalidateVisual();
                e.Handled = true;
                return;
            }

            if (_pressedSortColumnIndex >= 0)
            {
                if (!e.LeftButton || !IsMouseCaptured)
                {
                    _pressedSortColumnIndex = -1;
                }
                e.Handled = true;
                return;
            }

            UpdateSeparatorCursor(pos);
        }

        protected override void OnMouseLeave()
        {
            base.OnMouseLeave();
            if (_resizeColumnIndex < 0)
            {
                Cursor = null;
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);

            if (_resizeColumnIndex >= 0)
            {
                _resizeColumnIndex = -1;
                _resizeSession = null;
                Cursor = null;

                if (_owner.FindVisualRoot() is Window resizeWindow)
                    resizeWindow.ReleaseMouseCapture();

                e.Handled = true;
                return;
            }

            if (_pressedSortColumnIndex >= 0)
            {
                int pressed = _pressedSortColumnIndex;
                _pressedSortColumnIndex = -1;
                if (_owner.FindVisualRoot() is Window sortWindow)
                    sortWindow.ReleaseMouseCapture();

                var pos = e.GetPosition(this);
                if (HitTestColumn(pos.X) == pressed && HitTestSeparator(pos.X) < 0)
                {
                    _owner._core.CycleSort(pressed);
                }
                e.Handled = true;
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

        // OnRender reads IsMouseOver directly (no style trigger), so the framework's
        // visual-state path doesn't invalidate for us. Schedule a render explicitly.
        protected override void OnMouseEnter() => InvalidateVisual();

        protected override void OnMouseLeave() => InvalidateVisual();

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

            _owner.HandleRowPointerDown(_rowIndex, e);
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
                if (e is FrameworkElement fe)
                {
                    fe.NotifyDpiChanged(old, dpi);
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
                _cells[idx].Unbind();
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
                _cells[i].Bind(item, index);
            }

            InvalidateMeasure();
        }

        public void Recycle()
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                _cells[i].Unbind();
            }

            InvalidateMeasure();
        }

        protected override Size MeasureContent(Size availableSize)
        {
            var pad = _owner.CellPadding;
            double padH = pad.HorizontalThickness;
            double padV = pad.VerticalThickness;
            double maxCellH = 0;
            for (int i = 0; i < _cells.Count; i++)
            {
                double h = double.IsPositiveInfinity(availableSize.Height)
                    ? double.PositiveInfinity
                    : Math.Max(0, availableSize.Height - padV);

                var column = _owner._core.Columns[i];
                if (column.Width.IsAuto)
                {
                    _cells[i].View.Measure(new Size(double.PositiveInfinity, h));
                    _owner.ReportAutoDesiredWidth(i, _cells[i].View.DesiredSize.Width + padH);
                }

                double w = Math.Max(0, column.ActualWidth - padH);
                _cells[i].View.Measure(new Size(w, h));
                if (_cells[i].View.DesiredSize.Height > maxCellH)
                {
                    maxCellH = _cells[i].View.DesiredSize.Height;
                }
            }

            // Report measured max cell height + padding. FixedHeightItemsPresenter ignores
            // this and uses its own ItemHeight; VariableHeightItemsPresenter uses it as the
            // actual row height for prefix-sum bookkeeping and viewport layout.
            double rowH = double.IsPositiveInfinity(availableSize.Height)
                ? maxCellH + padV
                : availableSize.Height;
            return new Size(availableSize.Width, rowH);
        }

        public void MeasureAutoColumn(int columnIndex)
        {
            if ((uint)columnIndex >= (uint)_cells.Count)
            {
                return;
            }

            var pad = _owner.CellPadding;
            double rowHeight = Bounds.Height > 0 ? Bounds.Height : _owner.ResolveRowHeight();
            double availableHeight = Math.Max(0, rowHeight - pad.VerticalThickness);
            var view = _cells[columnIndex].View;
            view.Measure(new Size(double.PositiveInfinity, availableHeight));
            _owner._core.ReportAutoDesiredWidth(
                columnIndex,
                view.DesiredSize.Width + pad.HorizontalThickness);
        }

        protected override void ArrangeContent(Rect bounds)
        {
            double x = bounds.X;
            var pad = _owner.CellPadding;
            for (int i = 0; i < _cells.Count; i++)
            {
                double w = Math.Max(0, _owner._core.Columns[i].ActualWidth);
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
            var isSelected = _owner._core.IsItemSelected(_rowIndex);

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
                context.DrawLine(new Point(snapped.X, snapped.Bottom - 1), new Point(snapped.Right, snapped.Bottom - 1), stroke, 1, pixelSnap: true);

                double x = snapped.X;
                for (int i = 0; i < _owner._core.Columns.Count; i++)
                {
                    x += Math.Max(0, _owner._core.Columns[i].ActualWidth);
                    if (x >= snapped.Right - 0.5)
                    {
                        break;
                    }

                    context.DrawLine(new Point(x, snapped.Y), new Point(x, snapped.Bottom), stroke, 1, pixelSnap: true);
                }
            }
        }

        protected override void RenderSubtree(IGraphicsContext context)
        {
            for (int i = 0; i < _cells.Count; i++)
            {
                // Keep collapsed cells realized and bound so their column can be restored,
                // but do not render controls into a zero-width slot. Bordered controls would
                // otherwise collapse both edges into a visible vertical line.
                if (_owner._core.Columns[i].ActualWidth <= 0.01)
                {
                    continue;
                }

                _cells[i].View.Render(context);
            }
        }

        private sealed class Cell
        {
            private readonly Row _row;
            private bool _built;

            public Cell(Row row, TemplateContext context)
            {
                _row = row;
                Context = context;
                View = new TextBlock();
            }

            public TemplateContext Context { get; }

            public IDataTemplate? Template { get; set; }

            public FrameworkElement View { get; private set; }

            public void Bind(object? item, int index)
            {
                Context.BindTemplate(View, Template!, item, index);
            }

            public void Unbind()
            {
                Context.UnbindTemplate(View);
            }

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

                // MouseDown bubbles up the visual tree, so a single handler
                // on the root view catches clicks on all child elements.
                View.MouseDown += OnCellMouseDown;
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

                _row._owner.HandleRowPointerDown(_row._rowIndex, e);
            }
        }
    }

    internal sealed class GridViewCore
    {
        internal sealed class ColumnDefinition
        {
            public ColumnDefinition(
                string header,
                GridLength width,
                double minWidth,
                double maxWidth,
                bool isResizable,
                IDataTemplate cellTemplate,
                Comparison<object?>? sortComparison = null)
            {
                Header = header;
                Width = width;
                MinWidth = minWidth;
                MaxWidth = maxWidth;
                IsResizable = isResizable;
                CellTemplate = cellTemplate;
                SortComparison = sortComparison;
            }

            public string Header { get; }

            public GridLength Width { get; set; }

            public double MinWidth { get; }

            public double MaxWidth { get; }

            public bool IsResizable { get; }

            public IDataTemplate CellTemplate { get; }

            public Comparison<object?>? SortComparison { get; }

            public bool IsSortable => SortComparison != null;

            public double AutoDesiredWidth { get; set; }

            public double ActualWidth { get; set; }
        }

        internal sealed class ColumnResizeSession
        {
            public required int TargetIndex { get; init; }

            public required int ColumnCount { get; init; }

            public required double StartWidth { get; init; }

            public required bool IsStar { get; init; }

            public required bool IsAuto { get; init; }

            public required double[] InitialWidths { get; init; }

            public required double[] InitialWeights { get; init; }

            public required int[] PeerIndices { get; init; }

            public double[] WorkingWidths { get; init; } = [];
        }

        private ISelectableItemsView _sourceItemsView = ItemsView.EmptySelectable;
        private GridViewSortedItemsView _itemsView = GridViewSortedItemsView.Create(
            ItemsView.EmptySelectable,
            null,
            GridViewSortDirection.None);
        private readonly List<ColumnDefinition> _columns = new();
        private readonly List<GridViewColumnWidthRequest> _widthRequests = new();
        private double[] _resolvedWidths = [];
        private int _columnsVersion;

        public IReadOnlyList<ColumnDefinition> Columns => _columns;

        public int ColumnsVersion => _columnsVersion;

        public ISelectableItemsView ItemsSource => _itemsView;

        public ISelectableItemsView SourceItemsView => _sourceItemsView;

        public int SortColumnIndex { get; private set; } = -1;

        public GridViewSortDirection SortDirection { get; private set; }

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

        /// <summary>The current items view as a multi-selection view, or null when it does not support multi-select.</summary>
        public IMultiSelectableItemsView? MultiView => _itemsView.AsMultiSelectable();

        public ItemsSelectionMode SelectionMode
        {
            get => _itemsView.GetSelectionMode();
            set => _itemsView.SetSelectionMode(value);
        }

        public IReadOnlyList<int> SelectedIndices => _itemsView.GetSelectedIndices();

        public bool IsItemSelected(int index) => _itemsView.IsItemSelected(index);

        public event Action? SelectedIndicesChanged;

        public event Action<ItemsChange>? ItemsChanged;

        public event Action<object?>? SelectionChanged;

        public event Action? ColumnsChanged;

        public event Action<GridViewSortChange>? SortChanged;

        public void SetItems(ISelectableItemsView itemsView)
        {
            ArgumentNullException.ThrowIfNull(itemsView);

            var old = _itemsView;
            int previousSelectedIndex = old.SelectedIndex;
            object? previousSelectedItem = previousSelectedIndex >= 0 && previousSelectedIndex < old.Count
                ? old.GetItem(previousSelectedIndex)
                : null;

            Comparison<object?>? comparison = SortColumnIndex >= 0
                ? _columns[SortColumnIndex].SortComparison
                : null;
            var next = GridViewSortedItemsView.Create(itemsView, comparison, SortDirection);
            if (itemsView is IMultiSelectableItemsView newMulti)
            {
                newMulti.ClearSelection();
            }
            if (previousSelectedItem != null)
            {
                next.SelectedItem = previousSelectedItem;
            }

            UnhookItemsView(old);
            old.Dispose();
            _sourceItemsView = itemsView;
            _itemsView = next;
            HookItemsView(_itemsView);

            ItemsChanged?.Invoke(new ItemsChange(ItemsChangeKind.Reset, 0, _itemsView.Count));
        }

        public void SetColumns(IReadOnlyList<ColumnDefinition> columns)
        {
            ArgumentNullException.ThrowIfNull(columns);

            ClearSort();

            _columns.Clear();
            for (int i = 0; i < columns.Count; i++)
            {
                _columns.Add(columns[i]);
            }

            _columnsVersion++;
            ColumnsChanged?.Invoke();
        }

        public void SortByColumn(int columnIndex, GridViewSortDirection direction)
        {
            if (direction is < GridViewSortDirection.None or > GridViewSortDirection.Descending)
            {
                throw new ArgumentOutOfRangeException(nameof(direction));
            }
            if (direction == GridViewSortDirection.None)
            {
                ClearSort();
                return;
            }
            if ((uint)columnIndex >= (uint)_columns.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(columnIndex));
            }

            var comparison = _columns[columnIndex].SortComparison
                ?? throw new InvalidOperationException($"GridView column {columnIndex} does not define a sort comparer.");
            if (SortColumnIndex == columnIndex && SortDirection == direction)
            {
                return;
            }

            int previousColumn = SortColumnIndex;
            var previousDirection = SortDirection;
            SortColumnIndex = columnIndex;
            SortDirection = direction;
            try
            {
                _itemsView.SetSort(comparison, direction);
            }
            catch
            {
                SortColumnIndex = previousColumn;
                SortDirection = previousDirection;
                throw;
            }

            SortChanged?.Invoke(new GridViewSortChange(columnIndex, direction));
        }

        public void ClearSort()
        {
            if (SortDirection == GridViewSortDirection.None)
            {
                return;
            }

            SortColumnIndex = -1;
            SortDirection = GridViewSortDirection.None;
            _itemsView.SetSort(null, GridViewSortDirection.None);
            SortChanged?.Invoke(new GridViewSortChange(-1, GridViewSortDirection.None));
        }

        public void CycleSort(int columnIndex)
        {
            if ((uint)columnIndex >= (uint)_columns.Count || !_columns[columnIndex].IsSortable)
            {
                return;
            }

            var next = SortColumnIndex != columnIndex
                ? GridViewSortDirection.Ascending
                : SortDirection switch
                {
                    GridViewSortDirection.Ascending => GridViewSortDirection.Descending,
                    GridViewSortDirection.Descending => GridViewSortDirection.None,
                    _ => GridViewSortDirection.Ascending,
                };
            SortByColumn(columnIndex, next);
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

        public double ResolveColumnWidths(double availableWidth, out bool changed)
        {
            _widthRequests.Clear();
            for (int i = 0; i < _columns.Count; i++)
            {
                var column = _columns[i];
                _widthRequests.Add(new GridViewColumnWidthRequest(
                    column.Width,
                    column.AutoDesiredWidth,
                    column.MinWidth,
                    column.MaxWidth));
            }

            if (_resolvedWidths.Length < _columns.Count)
            {
                _resolvedWidths = new double[_columns.Count];
            }

            double extent = GridViewColumnWidthResolver.Resolve(_widthRequests, availableWidth, _resolvedWidths);
            changed = false;
            for (int i = 0; i < _columns.Count; i++)
            {
                double actual = _resolvedWidths[i];
                if (!_columns[i].ActualWidth.Equals(actual))
                {
                    _columns[i].ActualWidth = actual;
                    changed = true;
                }
            }

            return extent;
        }

        public bool ReportAutoDesiredWidth(int index, double desiredWidth)
        {
            if ((uint)index >= (uint)_columns.Count || double.IsNaN(desiredWidth) || desiredWidth <= 0)
            {
                return false;
            }

            var column = _columns[index];
            if (!column.Width.IsAuto)
            {
                return false;
            }

            desiredWidth = Math.Min(Math.Max(desiredWidth, column.MinWidth), column.MaxWidth);
            if (desiredWidth <= column.AutoDesiredWidth + 0.01)
            {
                return false;
            }

            column.AutoDesiredWidth = desiredWidth;
            return true;
        }

        public void ResetAutoDesiredWidths()
        {
            for (int i = 0; i < _columns.Count; i++)
            {
                _columns[i].AutoDesiredWidth = 0;
            }
        }

        public bool CanResizeColumn(int index)
            => (uint)index < (uint)_columns.Count && _columns[index].IsResizable;

        public bool CanAutoSizeColumn(int index)
            => (uint)index < (uint)_columns.Count && _columns[index].IsResizable;

        public bool ResetColumnToAuto(int index)
        {
            if (!CanAutoSizeColumn(index))
            {
                return false;
            }

            var column = _columns[index];
            column.Width = GridLength.Auto;
            // Auto observations are monotonic during normal virtualization. An explicit
            // auto-fit gesture is also the escape hatch that allows a previously wider
            // observation to shrink to the currently realized header/cell content.
            column.AutoDesiredWidth = 0;
            return true;
        }

        public ColumnResizeSession? BeginColumnResize(int index)
        {
            if (!CanResizeColumn(index))
            {
                return null;
            }

            var target = _columns[index];

            var initialWidths = new double[_columns.Count];
            var initialWeights = new double[_columns.Count];
            for (int i = 0; i < _columns.Count; i++)
            {
                initialWidths[i] = _columns[i].ActualWidth;
                initialWeights[i] = _columns[i].Width.IsStar ? _columns[i].Width.Value : 0;
            }

            var peers = new List<int>();
            if (target.Width.IsStar)
            {
                // Prefer columns to the right, matching the separator being dragged. If there
                // are none, compensate with Star columns on the left.
                for (int i = index + 1; i < _columns.Count; i++)
                {
                    if (_columns[i].IsResizable && _columns[i].Width.IsStar)
                    {
                        peers.Add(i);
                    }
                }
                if (peers.Count == 0)
                {
                    for (int i = index - 1; i >= 0; i--)
                    {
                        if (_columns[i].IsResizable && _columns[i].Width.IsStar)
                        {
                            peers.Add(i);
                        }
                    }
                }
            }

            return new ColumnResizeSession
            {
                TargetIndex = index,
                ColumnCount = _columns.Count,
                StartWidth = target.ActualWidth,
                IsStar = target.Width.IsStar,
                IsAuto = target.Width.IsAuto,
                InitialWidths = initialWidths,
                InitialWeights = initialWeights,
                PeerIndices = peers.ToArray(),
                WorkingWidths = new double[_columns.Count],
            };
        }

        public bool ResizeColumn(ColumnResizeSession session, double requestedWidth)
        {
            ArgumentNullException.ThrowIfNull(session);
            if (session.ColumnCount != _columns.Count || (uint)session.TargetIndex >= (uint)_columns.Count)
            {
                return false;
            }

            var target = _columns[session.TargetIndex];
            requestedWidth = Math.Min(Math.Max(requestedWidth, target.MinWidth), target.MaxWidth);

            if (!session.IsStar)
            {
                if (target.ActualWidth.Equals(requestedWidth) && target.Width.IsAbsolute && target.Width.Value.Equals(requestedWidth))
                {
                    return false;
                }

                target.Width = GridLength.Pixels(requestedWidth);
                if (session.IsAuto)
                {
                    target.AutoDesiredWidth = 0;
                }
                target.ActualWidth = requestedWidth;
                return true;
            }

            Array.Copy(session.InitialWidths, session.WorkingWidths, session.ColumnCount);
            double requestedDelta = requestedWidth - session.StartWidth;
            if (Math.Abs(requestedDelta) <= 0.001)
            {
                return false;
            }

            if (session.PeerIndices.Length == 0)
            {
                // A lone Star has no relative weight that can express a manual width. Once the
                // user actually drags it, materialize the requested width as Pixel so the last
                // column remains resizable and may intentionally create or remove overflow.
                target.Width = GridLength.Pixels(requestedWidth);
                target.ActualWidth = requestedWidth;
                return true;
            }

            bool targetExpands = requestedDelta > 0;
            double capacity = 0;
            for (int i = 0; i < session.PeerIndices.Length; i++)
            {
                int peerIndex = session.PeerIndices[i];
                var peer = _columns[peerIndex];
                double peerCapacity = targetExpands
                    ? Math.Max(0, session.InitialWidths[peerIndex] - peer.MinWidth)
                    : Math.Max(0, peer.MaxWidth - session.InitialWidths[peerIndex]);
                capacity += peerCapacity;
            }

            double appliedMagnitude = Math.Min(Math.Abs(requestedDelta), capacity);
            if (appliedMagnitude <= 0.001)
            {
                if (targetExpands && session.StartWidth <= 0.001)
                {
                    // No Star peer can surrender space when every flexible column is already
                    // collapsed. Materialize the dragged column as Pixel so the gesture can
                    // intentionally create horizontal overflow and recover the column.
                    target.Width = GridLength.Pixels(requestedWidth);
                    target.ActualWidth = requestedWidth;
                    return true;
                }
                return false;
            }

            double appliedDelta = targetExpands ? appliedMagnitude : -appliedMagnitude;
            session.WorkingWidths[session.TargetIndex] = session.StartWidth + appliedDelta;
            DistributeResizeCompensation(session, appliedMagnitude, targetExpands);

            // Re-express every Star width in a common scale. Using only the changed pair would
            // mix pixel-like weights with the untouched original weights and distort the next
            // viewport resolve.
            const double minimumWeight = 0.000001;
            for (int i = 0; i < _columns.Count; i++)
            {
                if (!_columns[i].Width.IsStar)
                {
                    continue;
                }

                double actual = session.WorkingWidths[i];
                _columns[i].ActualWidth = actual;
                _columns[i].Width = GridLength.Stars(Math.Max(minimumWeight, actual));
            }

            return true;
        }

        private void DistributeResizeCompensation(ColumnResizeSession session, double amount, bool targetExpands)
        {
            var unresolved = new List<int>(session.PeerIndices);
            double remaining = amount;

            while (unresolved.Count > 0 && remaining > 0.001)
            {
                double totalWeight = 0;
                for (int i = 0; i < unresolved.Count; i++)
                {
                    totalWeight += Math.Max(0.000001, session.InitialWeights[unresolved[i]]);
                }

                bool constrained = false;
                for (int i = unresolved.Count - 1; i >= 0; i--)
                {
                    int peerIndex = unresolved[i];
                    var peer = _columns[peerIndex];
                    double capacity = targetExpands
                        ? Math.Max(0, session.InitialWidths[peerIndex] - peer.MinWidth)
                        : Math.Max(0, peer.MaxWidth - session.InitialWidths[peerIndex]);
                    double share = remaining * Math.Max(0.000001, session.InitialWeights[peerIndex]) / totalWeight;
                    if (share >= capacity - 0.001)
                    {
                        session.WorkingWidths[peerIndex] = targetExpands
                            ? session.InitialWidths[peerIndex] - capacity
                            : session.InitialWidths[peerIndex] + capacity;
                        remaining = Math.Max(0, remaining - capacity);
                        unresolved.RemoveAt(i);
                        constrained = true;
                    }
                }

                if (constrained)
                {
                    continue;
                }

                for (int i = 0; i < unresolved.Count; i++)
                {
                    int peerIndex = unresolved[i];
                    double share = remaining * Math.Max(0.000001, session.InitialWeights[peerIndex]) / totalWeight;
                    session.WorkingWidths[peerIndex] = targetExpands
                        ? session.InitialWidths[peerIndex] - share
                        : session.InitialWidths[peerIndex] + share;
                }
                remaining = 0;
            }
        }

        private void HookItemsView(ISelectableItemsView view)
        {
            view.Changed += OnItemsChanged;
            view.SelectionChanged += OnItemsSelectionChanged;
            if (view is IMultiSelectableItemsView multi)
            {
                multi.SelectedIndicesChanged += OnSelectedIndicesChanged;
            }
        }

        private void UnhookItemsView(ISelectableItemsView view)
        {
            view.Changed -= OnItemsChanged;
            view.SelectionChanged -= OnItemsSelectionChanged;
            if (view is IMultiSelectableItemsView multi)
            {
                multi.SelectedIndicesChanged -= OnSelectedIndicesChanged;
            }
        }

        private void OnItemsChanged(ItemsChange change) => ItemsChanged?.Invoke(change);

        private void OnItemsSelectionChanged(int _) => SelectionChanged?.Invoke(SelectedItem);

        private void OnSelectedIndicesChanged() => SelectedIndicesChanged?.Invoke();
    }
}
