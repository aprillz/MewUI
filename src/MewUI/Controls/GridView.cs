using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public sealed class GridView : ContentControl
{
    private object? _core;
    private Type? _itemType;
    private int _selectedIndex = -1;

    private Action<double>? _setRowHeight;
    private Action<double>? _setHeaderHeight;
    private Action<double>? _setMaxAutoViewportHeight;
    private Action<bool>? _setZebraStriping;
    private Action<bool>? _setShowGridLines;
    private Func<int>? _getSelectedIndex;
    private Action<int>? _setSelectedIndex;
    private Func<object?>? _getSelectedItem;

    public GridView()
    {
        // The typed core draws background/border; the wrapper should not add visuals.
        Background = Color.Transparent;
        BorderThickness = 0;
        Padding = Thickness.Zero;
    }

    public event Action<object?>? SelectionChanged;

    public bool ZebraStriping
    {
        get; set
        {
            field = value;
            if (_core != null) ApplyCoreOptions();
        }
    } = true;

    public bool ShowGridLines
    {
        get; set
        {
            field = value;
            if (_core != null) ApplyCoreOptions();
        }
    }

    public double RowHeight
    {
        get; set
        {
            field = value;
            if (_core != null) ApplyCoreOptions();
        }
    } = double.NaN;

    public double HeaderHeight
    {
        get; set
        {
            field = value;
            if (_core != null) ApplyCoreOptions();
        }
    } = double.NaN;

    public double MaxAutoViewportHeight
    {
        get; set
        {
            field = value;
            if (_core != null) ApplyCoreOptions();
        }
    } = 320;

    public int SelectedIndex
    {
        get
        {
            return _getSelectedIndex?.Invoke() ?? _selectedIndex;
        }
        set
        {
            _selectedIndex = value;
            _setSelectedIndex?.Invoke(value);
        }
    }

    public object? SelectedItem => _getSelectedItem?.Invoke();

    public void SetItemsSource<TItem>(IReadOnlyList<TItem> items)
    {
        ArgumentNullException.ThrowIfNull(items);

        var core = EnsureCore<TItem>();
        core.SetItems(ItemsView.Create(items));
    }

    public void SetItemsSource<TItem>(ItemsView<TItem> itemsView)
    {
        ArgumentNullException.ThrowIfNull(itemsView);

        var core = EnsureCore<TItem>();
        core.SetItems(itemsView);
    }

    public void SetColumns<TItem>(IReadOnlyList<GridViewColumn<TItem>> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var core = EnsureCore<TItem>();
        core.SetColumns(columns);
    }

    public void AddColumns<TItem>(params GridViewColumn<TItem>[] columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        var core = EnsureCore<TItem>();
        core.AddColumns(columns);
    }

    private GridViewCore<TItem> EnsureCore<TItem>()
    {
        if (_core is GridViewCore<TItem> typed)
        {
            return typed;
        }

        if (_core != null)
        {
            throw new InvalidOperationException($"GridView is already configured for item type '{_itemType?.Name}'. Create a new GridView for a different TItem.");
        }

        _itemType = typeof(TItem);

        var core = new GridViewCore<TItem>();
        _core = core;
        Content = core;

        _setRowHeight = v => core.RowHeight = v;
        _setHeaderHeight = v => core.HeaderHeight = v;
        _setMaxAutoViewportHeight = v => core.MaxAutoViewportHeight = v;
        _setZebraStriping = v => core.ZebraStriping = v;
        _setShowGridLines = v => core.ShowGridLines = v;
        _getSelectedIndex = () => core.SelectedIndex;
        _setSelectedIndex = v => core.SelectedIndex = v;
        _getSelectedItem = () => core.SelectedItem;

        ApplyCoreOptions();

        // Apply any cached SelectedIndex set before ItemsSource was configured.
        if (_selectedIndex != -1)
        {
            core.SelectedIndex = _selectedIndex;
        }

        core.SelectionChanged += item =>
        {
            _selectedIndex = core.SelectedIndex;
            SelectionChanged?.Invoke(item);
        };

        return core;
    }

    private void ApplyCoreOptions()
    {
        if (_core == null)
        {
            return;
        }

        _setRowHeight?.Invoke(RowHeight);
        _setHeaderHeight?.Invoke(HeaderHeight);
        _setMaxAutoViewportHeight?.Invoke(MaxAutoViewportHeight);
        _setZebraStriping?.Invoke(ZebraStriping);
        _setShowGridLines?.Invoke(ShowGridLines);
    }
}

internal sealed class GridViewCore<TItem> : Control, IVisualTreeHost
{
    private readonly List<GridViewColumn<TItem>> _columns = new();

    private readonly ScrollBar _vBar;
    private readonly ScrollController _scroll = new();
    private readonly HeaderRow _header;
    private readonly TemplatedItemsHost _itemsHost;
    private bool _rebindVisibleOnNextRender = true;
    private IItemsView _itemsView = ItemsView.Empty;
    private double _rowsExtentHeight;
    private double _viewportHeight;
    private int _columnsVersion;

    public GridViewCore()
    {
        BorderThickness = 1;
        Padding = new Thickness(1);

        _header = new HeaderRow { Parent = this };
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
            getItem: index => index >= 0 && index < _itemsView.Count ? _itemsView.GetItem(index) : null,
            invalidateMeasureAndVisual: () => { InvalidateMeasure(); InvalidateArrange(); InvalidateVisual(); },
            template: rowTemplate,
            recycle: e => ((Row)e).Recycle());
    }

    public event Action<TItem?>? SelectionChanged;

    public IItemsView ItemsSource => _itemsView;

    public IReadOnlyList<GridViewColumn<TItem>> Columns => _columns;

    public override bool Focusable => true;

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
    } = false;

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

    /// <summary>
    /// When the parent measures with infinite height (common inside ScrollViewer/StackPanel),
    /// GridView keeps a finite viewport and uses its internal scrollbar so it can virtualize rows.
    /// </summary>
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
        get => _itemsView.SelectedIndex;
        set => _itemsView.SelectedIndex = value;
    }

    public TItem? SelectedItem =>
        _itemsView.SelectedItem is TItem t ? t : default;

    protected override Color DefaultBackground => Theme.Palette.ControlBackground;

    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _rebindVisibleOnNextRender = true;
        InvalidateVisual();
    }

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

        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    public void SetColumns(IReadOnlyList<GridViewColumn<TItem>> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        _columns.Clear();
        for (int i = 0; i < columns.Count; i++)
        {
            _columns.Add(columns[i]);
        }
        _header.SetColumns(_columns);
        _columnsVersion++;
        _itemsHost.RecycleAll();

        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    public void AddColumns(IReadOnlyList<GridViewColumn<TItem>> columns)
    {
        ArgumentNullException.ThrowIfNull(columns);

        for (int i = 0; i < columns.Count; i++)
        {
            _columns.Add(columns[i]);
        }

        _header.SetColumns(_columns);
        _columnsVersion++;
        _itemsHost.RecycleAll();
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
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

        // Clip to the inner content area (exclude border/padding) to avoid overdrawing the outer border.
        var contentBounds = GetSnappedBorderBounds(Bounds)
            .Deflate(new Thickness(borderInset))
            .Deflate(Padding);

        // Rows must render only inside the scroll viewport (below header).
        double headerH = ResolveHeaderHeight();
        var rowsViewport = new Rect(
            contentBounds.X,
            contentBounds.Y + headerH,
            Math.Max(0, contentBounds.Width),
            Math.Max(0, contentBounds.Height - headerH));

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

                _itemsHost.Render(context);
            }
        }
        finally
        {
            context.Restore();
        }

        context.Save();
        context.SetClip(contentBounds);
        try
        {
            _header.Render(context);
        }
        finally
        {
            context.Restore();
        }

        if (_vBar.IsVisible)
        {
            _vBar.Render(context);
        }
    }

    public void ScrollBy(double delta)
    {
        int notches = Math.Sign(delta);
        if (notches == 0)
        {
            return;
        }

        _scroll.DpiScale = GetDpi() / 96.0;
        _scroll.SetMetricsDip(1, _rowsExtentHeight, _viewportHeight);
        if (_scroll.ScrollByNotches(1, notches, Theme.Metrics.ScrollWheelStep))
        {
            if (_vBar.IsVisible)
            {
                _vBar.Value = _scroll.GetOffsetDip(1);
            }

            InvalidateArrange();
            InvalidateVisual();
        }
    }

    void IVisualTreeHost.VisitChildren(Action<Element> visitor)
    {
        visitor(_header);
        _itemsHost.VisitRealized(visitor);

        visitor(_vBar);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        Focus();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var inner = new Rect(0, 0, availableSize.Width, availableSize.Height).Deflate(new Thickness(borderInset));

        double headerH = ResolveHeaderHeight();
        double rowH = ResolveRowHeight();

        _rowsExtentHeight = _itemsView.Count * rowH;
        if (double.IsPositiveInfinity(inner.Height))
        {
            var maxViewport = MaxAutoViewportHeight;
            if (!(maxViewport > 0) || double.IsNaN(maxViewport) || double.IsInfinity(maxViewport))
            {
                maxViewport = 320;
            }

            _viewportHeight = Math.Min(_rowsExtentHeight, maxViewport);
        }
        else
        {
            _viewportHeight = Math.Max(0, inner.Height - Padding.VerticalThickness - headerH);
        }

        double width = 0;
        for (int i = 0; i < _columns.Count; i++)
        {
            width += Math.Max(0, _columns[i].Width);
        }

        double height = headerH + _viewportHeight;

        // Header measures itself based on column widths; rows are virtualized.
        _header.SetColumns(_columns);
        _header.Measure(new Size(Math.Max(0, inner.Width - Padding.HorizontalThickness), headerH));

        return new Size(width, height).Inflate(Padding).Inflate(new Thickness(borderInset));
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        var theme = Theme;
        var snapped = GetSnappedBorderBounds(bounds);
        var dpiScale = GetDpi() / 96.0;
        var borderInset = GetBorderVisualInset();
        var inner = snapped.Deflate(new Thickness(borderInset));

        double headerH = ResolveHeaderHeight();
        double rowH = ResolveRowHeight();

        _rowsExtentHeight = _itemsView.Count * rowH;

        var content = inner.Deflate(Padding);
        var headerRect = new Rect(content.X, content.Y, Math.Max(0, content.Width), headerH);
        var rowsRect = new Rect(content.X, content.Y + headerH, Math.Max(0, content.Width), Math.Max(0, content.Height - headerH));

        _viewportHeight = rowsRect.Height;
        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(1, _rowsExtentHeight, _viewportHeight);
        _scroll.SetOffsetPx(1, _scroll.GetOffsetPx(1));

        _header.SetColumns(_columns);
        _header.Arrange(headerRect);

        bool needV = _rowsExtentHeight > _viewportHeight + 0.5;
        _vBar.IsVisible = needV;
        ArrangeVerticalBar(theme, inner, headerH);

        if (TryComputeVisibleRows(rowsRect, rowH, out int first, out int lastExclusive, out double yStart))
        {
            _itemsHost.Layout = new TemplatedItemsHost.ItemsRangeLayout
            {
                ContentBounds = rowsRect,
                First = first,
                LastExclusive = lastExclusive,
                ItemHeight = rowH,
                YStart = yStart,
                RebindExisting = false,
            };

            _itemsHost.Arrange();
        }
        else
        {
            _itemsHost.RecycleAll();
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

        if (_vBar.IsVisible && _vBar.Bounds.Contains(point))
        {
            return _vBar;
        }

        UIElement? rowHit = null;
        _itemsHost.VisitRealized(e =>
        {
            if (rowHit != null)
            {
                return;
            }

            if (e is UIElement ui)
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

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || !_vBar.IsVisible)
        {
            return;
        }

        int notches = Math.Sign(e.Delta);
        if (notches == 0)
        {
            return;
        }

        _scroll.DpiScale = GetDpi() / 96.0;
        _scroll.SetMetricsDip(1, _rowsExtentHeight, _viewportHeight);
        _scroll.ScrollByNotches(1, -notches, Theme.Metrics.ScrollWheelStep);
        _vBar.Value = _scroll.GetOffsetDip(1);
        InvalidateArrange();
        InvalidateVisual();
        e.Handled = true;
    }

    private void ArrangeVerticalBar(Theme theme, Rect innerBounds, double headerH)
    {
        if (!_vBar.IsVisible)
        {
            _vBar.Value = 0;
            _vBar.Arrange(Rect.Empty);
            return;
        }

        double t = theme.Metrics.ScrollBarHitThickness;
        const double inset = 0;

        _vBar.Minimum = 0;
        _vBar.Maximum = _scroll.GetMaxDip(1);
        _vBar.ViewportSize = _viewportHeight;
        _vBar.SmallChange = theme.Metrics.ScrollBarSmallChange;
        _vBar.LargeChange = theme.Metrics.ScrollBarLargeChange;
        _vBar.Value = _scroll.GetOffsetDip(1);

        _vBar.Arrange(new Rect(
            innerBounds.Right - t - inset,
            innerBounds.Y + inset + headerH,
            t,
            Math.Max(0, innerBounds.Height - headerH - inset * 2)));
    }

    private bool TryComputeVisibleRows(Rect rowsRect, double rowH, out int first, out int lastExclusive, out double yStart)
    {
        first = 0;
        lastExclusive = 0;
        yStart = rowsRect.Y;

        int itemCount = _itemsView.Count;
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

    private void BindRowTemplate(FrameworkElement element, object? item, int index, TemplateContext _)
    {
        var row = (Row)element;
        row.EnsureDpi(GetDpi());
        row.EnsureColumns(_columns, _columnsVersion);
        row.Bind(item is TItem typed ? typed : default!, index);
    }

    private void SetSelectedIndex(int value)
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

    private void OnItemsChanged(ItemsChange change)
    {
        _itemsHost.RecycleAll();
        _rebindVisibleOnNextRender = true;
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    private void OnItemsSelectionChanged(int _)
    {
        SelectionChanged?.Invoke(SelectedItem);
        _rebindVisibleOnNextRender = true;
        InvalidateVisual();
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
        private readonly List<Label> _cells = new();

        public void SetColumns(IReadOnlyList<GridViewColumn<TItem>> columns)
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
            var gv = (GridViewCore<TItem>)Parent!;
            double x = bounds.X;
            for (int i = 0; i < _cells.Count; i++)
            {
                double w = Math.Max(0, gv._columns[i].Width);
                _cells[i].Arrange(new Rect(x, bounds.Y, w, bounds.Height));
                x += w;
            }
        }

        protected override void OnRender(IGraphicsContext context)
        {
            var theme = Theme;
            var gv = (GridViewCore<TItem>)Parent!;
            var snapped = GetSnappedBorderBounds(Bounds);
            var bg = theme.Palette.ButtonFace;

            if (theme.Metrics.ControlCornerRadius - 2 is double r && r > 0)
            {
                context.FillRoundedRectangle(snapped, r, r, bg);
            }
            else
            {
                context.FillRectangle(snapped, bg);
            }

            var stroke = theme.Palette.ControlBorder;

            // Header bottom separator line (button border).
            context.DrawLine(new Point(snapped.X, snapped.Bottom - 1), new Point(snapped.Right, snapped.Bottom - 1), stroke, 1);

            double x = snapped.X;
            double inset = Math.Min(6, Math.Max(0, (snapped.Height - 2) / 2));
            for (int i = 0; i < gv._columns.Count; i++)
            {
                x += Math.Max(0, gv._columns[i].Width);
                if (x >= snapped.Right - 0.5)
                {
                    break;
                }

                // Header separator line (right edge of each column).
                context.DrawLine(new Point(x, snapped.Y + inset), new Point(x, snapped.Bottom - inset), stroke, 1);
            }
        }
    }

    private sealed class Row : Panel
    {
        private readonly GridViewCore<TItem> _owner;
        private readonly List<Cell> _cells = new();
        private int _rowIndex;
        private uint _lastDpi;
        private int _lastColumnsVersion = -1;

        public Row(GridViewCore<TItem> owner)
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

            if (!_owner.IsEnabled)
            {
                return;
            }

            // Clicking the row background (e.g. to the right of the last column) should select the row.
            // Note: clicks on cell content are handled via Cell.HookSelection.
            _owner.Focus();
            _owner.SetSelectedIndex(_rowIndex);
        }

        public void EnsureDpi(uint dpi)
        {
            if (_lastDpi == dpi)
            {
                return;
            }

            var old = _lastDpi;
            _lastDpi = dpi;

            // Recycled rows can be detached during a DPI change, so they won't receive the Window broadcast.
            // Force a DPI refresh on all descendant controls (fonts, measurement caches, etc.) upon re-attach.
            Aprillz.MewUI.VisualTree.Visit(this, e =>
            {
                if (e is Control c)
                {
                    c.NotifyDpiChanged(old, dpi);
                }
            });

            InvalidateMeasure();
        }

        public void EnsureColumns(IReadOnlyList<GridViewColumn<TItem>> columns, int columnsVersion)
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
                _cells[i].Template = columns[i].CellTemplate
                    ?? throw new InvalidOperationException("GridViewColumn.CellTemplate is required.");
                _cells[i].EnsureViewBuilt(this);
            }

            InvalidateMeasure();
        }

        public void Bind(TItem item, int index)
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
                _cells[i].View.Measure(new Size(Math.Max(0, _owner._columns[i].Width), availableSize.Height));
            }

            return new Size(availableSize.Width, availableSize.Height);
        }

        protected override void ArrangeContent(Rect bounds)
        {
            double x = bounds.X;
            for (int i = 0; i < _cells.Count; i++)
            {
                double w = Math.Max(0, _owner._columns[i].Width);
                _cells[i].View.Arrange(new Rect(x, bounds.Y, w, bounds.Height));
                x += w;
            }
        }

        protected override void OnRender(IGraphicsContext context)
        {
            var snapped = GetSnappedBorderBounds(Bounds);

            var baseBg = Theme.Palette.ControlBackground;
            var rowBg = baseBg;

            if (_owner.ZebraStriping && (_rowIndex & 1) == 1)
            {
                rowBg = rowBg.Lerp(Theme.Palette.WindowText, Theme.IsDark ? 0.025 : 0.05);
            }

            context.FillRectangle(snapped, rowBg);


            var r = Math.Max(0, Theme.Metrics.ControlCornerRadius - 2);

            if (_owner.SelectedIndex == _rowIndex)
            {
                context.FillRoundedRectangle(snapped, r, r, Theme.Palette.SelectionBackground);
            }
            else if (IsMouseOver)
            {
                context.FillRoundedRectangle(snapped, r, r, rowBg.Lerp(Theme.Palette.Accent, 0.15));
            }


            if (!_owner.ShowGridLines)
            {
                return;
            }

            var stroke = Theme.Palette.ControlBorder;
            context.DrawLine(new Point(snapped.X, snapped.Bottom - 1), new Point(snapped.Right, snapped.Bottom - 1), stroke, 1);

            double x = snapped.X;
            for (int i = 0; i < _owner._columns.Count; i++)
            {
                x += Math.Max(0, _owner._columns[i].Width);
                if (x >= snapped.Right - 0.5)
                {
                    break;
                }

                context.DrawLine(new Point(x, snapped.Y), new Point(x, snapped.Bottom), stroke, 1);
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
                View = new Label(); // placeholder until Build
            }

            public TemplateContext Context { get; }

            public IDataTemplate<TItem>? Template { get; set; }

            public FrameworkElement View { get; private set; }

            public void EnsureViewBuilt(Row row)
            {
                if (_built || Template == null)
                {
                    return;
                }

                var built = Template.Build(Context);
                built.Parent = row;

                // Replace placeholder child.
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

                if (!_row._owner.IsEnabled)
                {
                    return;
                }

                _row._owner.Focus();
                _row._owner.SetSelectedIndex(_row._rowIndex);
            }
        }
    }
}
