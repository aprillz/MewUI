using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class GridView : Control, IVisualTreeHost, Aprillz.MewUI.Input.IFocusIntoViewHost, Aprillz.MewUI.Input.IVirtualizedTabNavigationHost
{
    private object? _itemTypeToken;
    private readonly GridViewCore _core = new();

    private readonly ScrollBar _vBar;
    private readonly ScrollController _scroll = new();
    private readonly HeaderRow _header;
    private readonly TemplatedItemsHost _itemsHost;

    private bool _rebindVisibleOnNextRender = true;
    private double _rowsExtentHeight;
    private double _viewportHeight;
    private ScrollIntoViewRequest _scrollIntoViewRequest;
    private int _pendingTabFocusIndex = -1;
    private int _pendingTabFocusAttempts;

    protected override double DefaultBorderThickness => Theme.Metrics.ControlBorderThickness;

    public GridView()
    {
        Padding = new Thickness(1);

        _header = new HeaderRow(this) { Parent = this };
        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false, Parent = this };
        _vBar.ValueChanged += v =>
        {
            var dpiScale = GetDpi() / 96.0;
            _scroll.DpiScale = dpiScale;
            _scroll.SetMetricsDip(1, _rowsExtentHeight, _viewportHeight);
            if (_scroll.SetOffsetDip(1, v))
            {
                InvalidateArrange();
                InvalidateVisual();
            }
        };

        var rowTemplate = new DelegateTemplate<object?>(
            build: _ => new Row(this),
            bind: BindRowTemplate);

        _itemsHost = new TemplatedItemsHost(
            owner: this,
            getItem: index => index >= 0 && index < _core.ItemsSource.Count ? _core.ItemsSource.GetItem(index) : null,
            invalidateMeasureAndVisual: () => { InvalidateMeasure(); InvalidateArrange(); InvalidateVisual(); },
            template: rowTemplate,
            recycle: e => ((Row)e).Recycle());

        _core.ItemsChanged += OnItemsChanged;
        _core.SelectionChanged += _ => OnItemsSelectionChanged();
        _core.ColumnsChanged += () =>
        {
            _header.SetColumns(_core.Columns);
            _itemsHost.RecycleAll();
            _rebindVisibleOnNextRender = true;
            InvalidateMeasure();
            InvalidateArrange();
            InvalidateVisual();
        };
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

    public override bool Focusable => true;

    protected override Color DefaultBackground => Theme.Palette.ControlBackground;

    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

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

        double viewport = _viewportHeight;
        if (viewport <= 0 || double.IsNaN(viewport) || double.IsInfinity(viewport))
        {
            return 1;
        }

        int step = (int)Math.Floor(viewport / rowH);
        return Math.Clamp(step, 1, Math.Max(1, count));
    }

    bool Aprillz.MewUI.Input.IFocusIntoViewHost.OnDescendantFocused(UIElement focusedElement)
    {
        if (focusedElement == this)
        {
            return false;
        }

        int found = -1;
        _itemsHost.VisitRealized((i, element) =>
        {
            if (found != -1)
            {
                return;
            }

            if (IsInSubtreeOf(focusedElement, element))
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

    bool Aprillz.MewUI.Input.IVirtualizedTabNavigationHost.TryMoveFocusFromDescendant(UIElement focusedElement, bool moveForward)
    {
        if (!IsEffectivelyEnabled || _core.ItemsSource.Count == 0)
        {
            return false;
        }

        int found = -1;
        _itemsHost.VisitRealized((i, element) =>
        {
            if (found != -1)
            {
                return;
            }

            if (IsInSubtreeOf(focusedElement, element))
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
        _pendingTabFocusIndex = targetIndex;
        _pendingTabFocusAttempts = 0;
        SchedulePendingTabFocus();
        return true;
    }

    private void SchedulePendingTabFocus()
    {
        if (_pendingTabFocusIndex < 0)
        {
            return;
        }

        if (FindVisualRoot() is not Window window)
        {
            return;
        }

        window.ApplicationDispatcher?.Post(ApplyPendingTabFocus, UiDispatcherPriority.Render);
    }

    private void ApplyPendingTabFocus()
    {
        if (_pendingTabFocusIndex < 0)
        {
            return;
        }

        if (FindVisualRoot() is not Window window)
        {
            _pendingTabFocusIndex = -1;
            return;
        }

        FrameworkElement? container = null;
        _itemsHost.VisitRealized((i, element) =>
        {
            if (i == _pendingTabFocusIndex)
            {
                container = element;
            }
        });

        if (container == null)
        {
            if (_pendingTabFocusAttempts++ < 4)
            {
                window.ApplicationDispatcher?.Post(ApplyPendingTabFocus, UiDispatcherPriority.Render);
            }
            else
            {
                _pendingTabFocusIndex = -1;
            }
            return;
        }

        var target = Input.FocusManager.FindFirstFocusable(container);
        if (target != null)
        {
            window.FocusManager.SetFocus(target);
        }

        _pendingTabFocusIndex = -1;
    }

    private static bool IsInSubtreeOf(UIElement element, UIElement root)
    {
        for (Element? current = element; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled || !_vBar.IsVisible || e.IsHorizontal)
        {
            return;
        }

        int notches = Math.Sign(e.Delta);
        if (notches == 0)
        {
            return;
        }

        double viewport = _viewportHeight;
        if (viewport <= 0 || double.IsNaN(viewport) || double.IsInfinity(viewport))
        {
            return;
        }

        int count = _core.ItemsSource.Count;
        double rowH = ResolveRowHeight();
        _rowsExtentHeight = count > 0 && rowH > 0 ? count * rowH : 0;

        _scroll.DpiScale = GetDpi() / 96.0;
        _scroll.SetMetricsDip(1, _rowsExtentHeight, viewport);
        _scroll.ScrollByNotches(1, -notches, Theme.Metrics.ScrollWheelStep);
        _vBar.Value = _scroll.GetOffsetDip(1);

        InvalidateArrange();
        InvalidateVisual();
        e.Handled = true;
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
        if (_vBar.IsVisible && GetLocalBounds(_vBar).Contains(position))
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
            _scroll.GetOffsetDip(1),
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
        if (_vBar.IsVisible && GetLocalBounds(_vBar).Contains(position))
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
        if (_vBar.IsVisible && GetLocalBounds(_vBar).Contains(position))
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

    private Rect GetLocalBounds(UIElement element)
        => new(
            element.Bounds.X - Bounds.X,
            element.Bounds.Y - Bounds.Y,
            element.Bounds.Width,
            element.Bounds.Height);

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

    void IVisualTreeHost.VisitChildren(Action<Element> visitor)
    {
        visitor(_header);
        visitor(_vBar);
        _itemsHost.VisitRealized(visitor);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        double widthLimit = double.IsPositiveInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width - Padding.HorizontalThickness - borderInset * 2);

        double totalColumnsWidth = 0;
        for (int i = 0; i < _core.Columns.Count; i++)
        {
            totalColumnsWidth += Math.Max(0, _core.Columns[i].Width);
            if (totalColumnsWidth >= widthLimit)
            {
                totalColumnsWidth = widthLimit;
                break;
            }
        }

        double headerH = ResolveHeaderHeight();
        double rowH = ResolveRowHeight();

        int count = _core.ItemsSource.Count;
        double desiredRowsHeight;
        if (double.IsPositiveInfinity(availableSize.Height))
        {
            desiredRowsHeight = count == 0 || rowH <= 0 ? 0 : Math.Min(count * rowH, MaxAutoViewportHeight);
        }
        else
        {
            desiredRowsHeight = Math.Max(0, availableSize.Height - headerH - Padding.VerticalThickness - borderInset * 2);
        }

        double desiredHeight = headerH + desiredRowsHeight;

        _header.Measure(new Size(Math.Max(0, totalColumnsWidth), headerH));

        double width = totalColumnsWidth + Padding.HorizontalThickness + borderInset * 2;
        double height = desiredHeight + Padding.VerticalThickness + borderInset * 2;

        return new Size(width, height);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var theme = Theme;
        var dpiScale = GetDpi() / 96.0;
        var borderInset = GetBorderVisualInset();

        var snapped = GetSnappedBorderBounds(bounds);
        var innerBounds = snapped.Deflate(new Thickness(borderInset));
        var contentBounds = innerBounds.Deflate(Padding);

        double headerH = ResolveHeaderHeight();

        _header.Arrange(new Rect(contentBounds.X, contentBounds.Y, Math.Max(0, contentBounds.Width), headerH));

        _viewportHeight = LayoutRounding.RoundToPixel(Math.Max(0, contentBounds.Height - headerH), dpiScale);

        int count = _core.ItemsSource.Count;
        double rowH = ResolveRowHeight();
        _rowsExtentHeight = count > 0 && rowH > 0 ? count * rowH : 0;

        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(1, _rowsExtentHeight, _viewportHeight);
        _scroll.SetOffsetPx(1, _scroll.GetOffsetPx(1));

        bool needV = _rowsExtentHeight > _viewportHeight + 0.5;
        _vBar.IsVisible = needV;

        if (_vBar.IsVisible)
        {
            double t = theme.Metrics.ScrollBarHitThickness;
            const double inset = 0;

            _vBar.Minimum = 0;
            _vBar.Maximum = Math.Max(0, _rowsExtentHeight - _viewportHeight);
            _vBar.ViewportSize = _viewportHeight;
            _vBar.SmallChange = theme.Metrics.ScrollBarSmallChange;
            _vBar.LargeChange = theme.Metrics.ScrollBarLargeChange;
            _vBar.Value = _scroll.GetOffsetDip(1);

            _vBar.Arrange(new Rect(
                innerBounds.Right - t - inset,
                innerBounds.Y + inset + Padding.Top + headerH,
                t,
                Math.Max(0, innerBounds.Height - Padding.VerticalThickness - headerH - inset * 2)));
        }

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
        var clipRadius = Math.Max(0, Theme.Metrics.ControlCornerRadius - borderInset);
        clipRadius = LayoutRounding.RoundToPixel(clipRadius, dpiScale);
        clipRadius = Math.Min(clipRadius, Math.Min(clipRect.Width, clipRect.Height) / 2);

        double headerH = ResolveHeaderHeight();
        var rowsViewport = new Rect(
            contentBounds.X,
            contentBounds.Y + headerH,
            Math.Max(0, contentBounds.Width),
            Math.Max(0, contentBounds.Height - headerH));

        context.Save();
        if (clipRadius > 0)
        {
            context.SetClipRoundedRect(clipRect, clipRadius, clipRadius);
        }
        else
        {
            context.SetClip(clipRect);
        }

        _header.Render(context);

        context.Save();
        context.SetClip(LayoutRounding.ExpandClipByDevicePixels(rowsViewport, dpiScale));
        try
        {
            var rowH = ResolveRowHeight();
            if (TryComputeVisibleRows(rowsViewport, rowH, out int first, out int lastExclusive, out double yStart))
            {
                bool rebind = _rebindVisibleOnNextRender;
                _rebindVisibleOnNextRender = false;

                _itemsHost.Layout = new TemplatedItemsHost.ItemsRangeLayout
                {
                    ContentBounds = rowsViewport,
                    First = first,
                    LastExclusive = lastExclusive,
                    ItemHeight = rowH,
                    YStart = yStart,
                    RebindExisting = rebind,
                };

                _itemsHost.Options = new TemplatedItemsHost.ItemsRangeOptions
                {
                    BeforeItemRender = BeforeRowRender,
                    GetContainerRect = GetRowContainerRect,
                };

                _itemsHost.Render(context);
            }
            else
            {
                _itemsHost.RecycleAll();
            }
        }
        finally
        {
            context.Restore();
            context.Restore();
        }

        if (_vBar.IsVisible)
        {
            _vBar.Render(context);
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

        var vbarHit = _vBar.HitTest(point);
        if (vbarHit != null)
        {
            return vbarHit;
        }

        UIElement? rowHit = null;
        _itemsHost.VisitRealized(element =>
        {
            if (rowHit != null)
            {
                return;
            }

            if (element is UIElement ui)
            {
                rowHit = ui.HitTest(point);
            }
        });

        if (rowHit != null)
        {
            return rowHit;
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
            context.FillRectangle(snapped, theme.Palette.ControlBackground.Lerp(theme.Palette.Accent, 0.06));
        }
    }

    private Rect GetRowContainerRect(int index, Rect itemRect)
    {
        var snapped = LayoutRounding.SnapViewportRectToPixels(itemRect, GetDpi() / 96.0);
        return snapped;
    }

    private void BindRowTemplate(FrameworkElement element, object? item, int index, TemplateContext _)
    {
        var row = (Row)element;
        row.EnsureDpi(GetDpi());
        row.EnsureColumns(_core.Columns, _core.ColumnsVersion);
        row.Bind(item, index);
    }

    private void OnItemsChanged(ItemsChange _)
    {
        _itemsHost.RecycleAll();
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

        double viewport = _viewportHeight;
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

        double oldOffset = _scroll.GetOffsetDip(1);
        double newOffset = ItemsViewportMath.ComputeScrollOffsetToBringItemIntoView(index, rowH, viewport, oldOffset);

        _scroll.DpiScale = GetDpi() / 96.0;
        _scroll.SetMetricsDip(1, _rowsExtentHeight, viewport);
        _scroll.SetOffsetDip(1, newOffset);

        double applied = _scroll.GetOffsetDip(1);
        if (applied.Equals(oldOffset))
        {
            return;
        }

        if (_vBar.IsVisible)
        {
            _vBar.Value = applied;
        }

        InvalidateArrange();
        InvalidateVisual();
    }

    public void ScrollIntoView(int index)
    {
        int count = _core.ItemsSource.Count;
        if (index < 0 || index >= count)
        {
            return;
        }

        double viewport = _viewportHeight;
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

        double oldOffset = _scroll.GetOffsetDip(1);
        double newOffset = ItemsViewportMath.ComputeScrollOffsetToBringItemIntoView(index, rowH, viewport, oldOffset);

        _scroll.DpiScale = GetDpi() / 96.0;
        _scroll.SetMetricsDip(1, _rowsExtentHeight, viewport);
        _scroll.SetOffsetDip(1, newOffset);

        double applied = _scroll.GetOffsetDip(1);
        if (applied.Equals(oldOffset))
        {
            return;
        }

        if (_vBar.IsVisible)
        {
            _vBar.Value = applied;
        }

        InvalidateArrange();
        InvalidateVisual();
    }

    private bool TryComputeVisibleRows(Rect rowsRect, double rowH, out int first, out int lastExclusive, out double yStart)
    {
        first = 0;
        lastExclusive = 0;
        yStart = rowsRect.Y;

        int itemCount = _core.ItemsSource.Count;
        if (itemCount == 0 || rowH <= 0 || rowsRect.Height <= 0)
        {
            return false;
        }

        double verticalOffset = _scroll.GetOffsetDip(1);
        first = (int)Math.Floor(verticalOffset / rowH);
        first = Math.Clamp(first, 0, Math.Max(0, itemCount - 1));

        int visible = (int)Math.Ceiling(rowsRect.Height / rowH) + 1;
        int last = Math.Min(itemCount - 1, first + visible);
        lastExclusive = last + 1;

        yStart = rowsRect.Y + (first * rowH - verticalOffset);
        return lastExclusive > first;
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

        public HeaderRow(GridView owner) => _owner = owner;

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
            double x = bounds.X;
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
            var snapped = GetSnappedBorderBounds(Bounds);
            var bg = theme.Palette.ButtonFace;

            context.FillRectangle(snapped, bg);

            var stroke = theme.Palette.ControlBorder;
            context.DrawLine(new Point(snapped.X, snapped.Bottom - 1), new Point(snapped.Right, snapped.Bottom - 1), stroke, 1);

            double x = snapped.X;
            double inset = Math.Min(6, Math.Max(0, (snapped.Height - 2) / 2));
            for (int i = 0; i < _owner._core.Columns.Count; i++)
            {
                x += Math.Max(0, _owner._core.Columns[i].Width);
                if (x >= snapped.Right - 0.5)
                {
                    break;
                }

                context.DrawLine(new Point(x, snapped.Y + inset), new Point(x, snapped.Bottom - inset), stroke, 1);
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

            _owner.Focus();
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
            for (int i = 0; i < _cells.Count; i++)
            {
                _cells[i].View.Measure(new Size(Math.Max(0, _owner._core.Columns[i].Width), availableSize.Height));
            }

            return new Size(availableSize.Width, availableSize.Height);
        }

        protected override void ArrangeContent(Rect bounds)
        {
            double x = bounds.X;
            for (int i = 0; i < _cells.Count; i++)
            {
                double w = Math.Max(0, _owner._core.Columns[i].Width);
                _cells[i].View.Arrange(new Rect(x, bounds.Y, w, bounds.Height));
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

                if (!_row._owner.IsEffectivelyEnabled)
                {
                    return;
                }

                _row._owner.Focus();
                _row._owner.SelectedIndex = _row._rowIndex;
            }
        }
    }

    internal sealed class GridViewCore
    {
        internal readonly record struct ColumnDefinition(string Header, double Width, IDataTemplate CellTemplate);

        private IItemsView _itemsView = ItemsView.Empty;
        private readonly List<ColumnDefinition> _columns = new();
        private int _columnsVersion;

        public IReadOnlyList<ColumnDefinition> Columns => _columns;

        public int ColumnsVersion => _columnsVersion;

        public IItemsView ItemsSource => _itemsView;

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

        public void SetItems(IItemsView itemsView)
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

        private void HookItemsView(IItemsView view)
        {
            view.Changed += OnItemsChanged;
            view.SelectionChanged += OnItemsSelectionChanged;
        }

        private void UnhookItemsView(IItemsView view)
        {
            view.Changed -= OnItemsChanged;
            view.SelectionChanged -= OnItemsSelectionChanged;
        }

        private void OnItemsChanged(ItemsChange change) => ItemsChanged?.Invoke(change);

        private void OnItemsSelectionChanged(int _) => SelectionChanged?.Invoke(SelectedItem);
    }
}
