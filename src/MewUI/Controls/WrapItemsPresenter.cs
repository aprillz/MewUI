namespace Aprillz.MewUI.Controls;

using Aprillz.MewUI.Rendering;

/// <summary>
/// Virtualizing wrap-grid items presenter with fixed item width and height.
/// Virtualizes by row — only visible rows are realized.
/// </summary>
internal sealed class WrapItemsPresenter : Control, IVisualTreeHost, IScrollContent, IItemsPresenter
{
    private readonly TemplatedItemsHost _itemsHost;

    private Size _viewport;
    private Point _offset;
    private Size _extent;
    private double _itemRadius;
    private int _pendingScrollIntoViewIndex = -1;

    // Cached render-time layout values (avoid closure allocation per OnRender)
    private double _renderOffsetY;
    private double _renderItemW;
    private double _renderItemH;
    private int _renderCols;
    private Rect _renderBounds;
    private Thickness _renderPad;
    private Action<IGraphicsContext, int, Rect>? _cachedBeforeItemRender;
    private Func<int, Rect, Rect>? _cachedGetContainerRect;

    private IItemsView _itemsSource = ItemsView.Empty;

    public double ItemWidth { get; set; } = 100;
    public double ItemHeight { get; set; } = 100;

    public IItemsView ItemsSource
    {
        get => _itemsSource;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_itemsSource, value)) return;

            if (_itemsSource != null) _itemsSource.Changed -= OnItemsChanged;
            _itemsSource = value;
            _itemsSource.Changed += OnItemsChanged;

            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public IDataTemplate ItemTemplate
    {
        get => _itemsHost.ItemTemplate;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _itemsHost.ItemTemplate = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public double ExtentWidth { get; set; }
    public double ItemRadius { get => _itemRadius; set { if (Set(ref _itemRadius, value)) InvalidateVisual(); } }
    public Action<IGraphicsContext, int, Rect>? BeforeItemRender { get; set; }
    public Func<int, Rect, Rect>? GetContainerRect { get; set; }
    public Thickness ItemPadding { get; set; }
    public bool RebindExisting { get; set; } = true;
    public double ItemHeightHint { get => ItemHeight; set { /* Wrap uses its own ItemHeight; ignore hint */ } }
    public bool UseHorizontalExtentForLayout { get; set; }
    public bool FillsAvailableWidth => true;
    public bool IsNonVirtualized => false;

    public double DesiredContentHeight
    {
        get
        {
            int count = ItemsSource.Count;
            if (count == 0 || ItemHeight <= 0 || ItemWidth <= 0) return 0;
            int cols = ComputeColumns(_viewport.Width);
            int rows = (count + cols - 1) / cols;
            double alignedH = GetPixelAlignedItemHeight();
            return Math.Min(rows * alignedH, alignedH * 12);
        }
    }

    public event Action<Point>? OffsetCorrectionRequested;

    public void RecycleAll() => _itemsHost.RecycleAll();
    public void VisitRealized(Action<Element> visitor) => _itemsHost.VisitRealized(visitor);
    public bool VisitRealized(Func<Element, bool> visitor) => _itemsHost.VisitRealized(visitor);
    public void VisitRealized(Action<int, FrameworkElement> visitor) => _itemsHost.VisitRealized(visitor);

    public WrapItemsPresenter()
    {
        _itemsHost = new TemplatedItemsHost(
            owner: this,
            getItem: i => ItemsSource.GetItem(i),
            invalidateMeasureAndVisual: () =>
            {
                InvalidateMeasure();
                InvalidateVisual();
            },
            template: CreateDefaultItemTemplate());
    }

    public Size Extent => _extent;

    public void SetViewport(Size viewport)
    {
        if (_viewport == viewport) return;
        _viewport = viewport;
        RecomputeExtent();
        InvalidateVisual();
    }

    public void SetOffset(Point offset)
    {
        var clamped = new Point(
            Math.Clamp(offset.X, 0, Math.Max(0, Extent.Width - _viewport.Width)),
            Math.Clamp(offset.Y, 0, Math.Max(0, Extent.Height - _viewport.Height)));

        if (_offset == clamped) return;
        _offset = clamped;
        InvalidateVisual();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        return _itemsHost.VisitRealized(visitor);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        RecomputeExtent();
        return new Size(
            Math.Max(0, availableSize.Width),
            Math.Max(0, availableSize.Height));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        int count = ItemsSource.Count;
        if (count == 0) return;

        double itemW = Math.Max(0, ItemWidth);
        double itemH = Math.Max(0, ItemHeight);
        if (itemW <= 0 || itemH <= 0 || _viewport.Height <= 0 || _viewport.Width <= 0)
        {
            _itemsHost.RecycleAll();
            return;
        }

        var dpiScale = GetDpi() / 96.0;
        var viewportBounds = Bounds;
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(viewportBounds, dpiScale);

        double alignedItemW = LayoutRounding.RoundToPixel(itemW, dpiScale);
        double alignedItemH = LayoutRounding.RoundToPixel(itemH, dpiScale);
        double alignedOffsetY = LayoutRounding.RoundToPixel(_offset.Y, dpiScale);

        int cols = ComputeColumns(_viewport.Width);
        int totalRows = (count + cols - 1) / cols;

        if (_pendingScrollIntoViewIndex >= 0)
        {
            int targetRow = _pendingScrollIntoViewIndex / cols;
            double top = targetRow * alignedItemH;
            double bottom = top + alignedItemH;
            double viewportH = _viewport.Height;

            double desiredOffsetY = alignedOffsetY;
            if (top < alignedOffsetY)
                desiredOffsetY = top;
            else if (bottom > alignedOffsetY + viewportH)
                desiredOffsetY = bottom - viewportH;

            desiredOffsetY = Math.Clamp(desiredOffsetY, 0, Math.Max(0, Extent.Height - viewportH));
            double onePx = dpiScale > 0 ? 1.0 / dpiScale : 1.0;
            if (Math.Abs(desiredOffsetY - alignedOffsetY) >= onePx * 0.99)
            {
                alignedOffsetY = desiredOffsetY;
                OffsetCorrectionRequested?.Invoke(new Point(_offset.X, desiredOffsetY));
            }
            else
            {
                _pendingScrollIntoViewIndex = -1;
            }
        }

        // Compute visible row range
        int firstRow = Math.Max(0, (int)Math.Floor(alignedOffsetY / alignedItemH));
        int lastRowExcl = Math.Min(totalRows, (int)Math.Ceiling((alignedOffsetY + contentBounds.Height) / alignedItemH));

        int firstIndex = firstRow * cols;
        int lastIndexExcl = Math.Min(count, lastRowExcl * cols);

        // Store layout values in fields (avoid closure allocation per OnRender)
        _renderOffsetY = alignedOffsetY;
        _renderItemW = alignedItemW;
        _renderItemH = alignedItemH;
        _renderCols = cols;
        _renderBounds = contentBounds;
        _renderPad = ItemPadding;

        _itemsHost.Layout = new TemplatedItemsHost.ItemsRangeLayout
        {
            ContentBounds = contentBounds,
            First = firstIndex,
            LastExclusive = lastIndexExcl,
            ItemHeight = alignedItemH,
            YStart = 0, // not used — GetContainerRect provides absolute coords
            ItemRadius = ItemRadius,
            RebindExisting = RebindExisting,
        };

        var userBeforeItemRender = BeforeItemRender;
        _itemsHost.Options = new TemplatedItemsHost.ItemsRangeOptions
        {
            BeforeItemRender = userBeforeItemRender != null
                ? (_cachedBeforeItemRender ??= (ctx, index, _) => BeforeItemRender?.Invoke(ctx, index, RenderCellRect(index)))
                : null,
            GetContainerRect = _cachedGetContainerRect ??= (index, _) =>
            {
                var cell = RenderCellRect(index);
                return _renderPad != default ? cell.Deflate(_renderPad) : cell;
            },
        };

        _itemsHost.Render(context);
    }

    private Rect RenderCellRect(int index)
    {
        int row = index / _renderCols;
        int col = index % _renderCols;
        double x = _renderBounds.X + col * _renderItemW;
        double y = _renderBounds.Y + row * _renderItemH - _renderOffsetY;
        return new Rect(x, y, _renderItemW, _renderItemH);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled) return null;
        if (!Bounds.Contains(point)) return null;

        UIElement? hit = null;
        _itemsHost.VisitRealized(element =>
        {
            if (hit != null) return;
            if (element is UIElement ui) hit = ui.HitTest(point);
        });

        return hit ?? this;
    }

    public bool TryGetItemIndexAtY(double yContent, out int index)
    {
        index = -1;
        int count = ItemsSource.Count;
        double alignedH = GetPixelAlignedItemHeight();
        if (count <= 0 || alignedH <= 0 || ItemWidth <= 0) return false;

        int cols = ComputeColumns(_viewport.Width);
        int row = (int)Math.Floor(yContent / alignedH);
        if (row < 0) return false;

        int i = row * cols;
        if (i >= count) return false;

        index = i;
        return true;
    }

    public bool TryGetItemIndexAt(double xContent, double yContent, out int index)
    {
        index = -1;
        int count = ItemsSource.Count;
        double alignedH = GetPixelAlignedItemHeight();
        double alignedW = GetPixelAlignedItemWidth();
        if (count <= 0 || alignedH <= 0 || alignedW <= 0) return false;

        int cols = ComputeColumns(_viewport.Width);
        int row = (int)Math.Floor(yContent / alignedH);
        int col = (int)Math.Floor(xContent / alignedW);

        if (row < 0 || col < 0 || col >= cols) return false;

        int i = row * cols + col;
        if (i < 0 || i >= count) return false;

        index = i;
        return true;
    }

    public bool TryGetItemYRange(int index, out double top, out double bottom)
    {
        top = 0; bottom = 0;
        int count = ItemsSource.Count;
        double alignedH = GetPixelAlignedItemHeight();
        if (count <= 0 || index < 0 || index >= count || alignedH <= 0 || ItemWidth <= 0) return false;

        int cols = ComputeColumns(_viewport.Width);
        int row = index / cols;
        top = row * alignedH;
        bottom = top + alignedH;
        return true;
    }

    // Must match the pixel-aligned height/width used in OnRender so hit-test, extent,
    // and ScrollIntoView stay consistent with the visual layout (esp. at 125% DPI).
    private double GetPixelAlignedItemHeight()
    {
        double h = Math.Max(0, ItemHeight);
        if (h <= 0 || double.IsNaN(h) || double.IsInfinity(h)) return 0;
        return LayoutRounding.RoundToPixel(h, GetDpi() / 96.0);
    }

    private double GetPixelAlignedItemWidth()
    {
        double w = Math.Max(0, ItemWidth);
        if (w <= 0 || double.IsNaN(w) || double.IsInfinity(w)) return 0;
        return LayoutRounding.RoundToPixel(w, GetDpi() / 96.0);
    }

    public void RequestScrollIntoView(int index)
    {
        int count = ItemsSource.Count;
        if (count <= 0 || index < 0 || index >= count) return;
        _pendingScrollIntoViewIndex = index;
        InvalidateVisual();
    }

    /// <summary>
    /// Gets the number of columns for the current viewport width.
    /// </summary>
    internal int ColumnCount => ComputeColumns(_viewport.Width);

    private int ComputeColumns(double viewportWidth)
    {
        if (ItemWidth <= 0 || viewportWidth <= 0) return 1;
        // Use rounding tolerance to avoid DPI-dependent column count differences.
        // E.g. 400 DIP / 80 = 5.0 exactly, but pixel-snapped borders may reduce
        // viewportWidth to 399.33, yielding 4.99 — which should still be 5 columns.
        double raw = viewportWidth / ItemWidth;
        int cols = (int)Math.Floor(raw);
        if (raw - cols > 0.95) cols++;
        return Math.Max(1, cols);
    }

    private void RecomputeExtent()
    {
        int count = ItemsSource.Count;
        double alignedH = GetPixelAlignedItemHeight();
        if (count == 0 || alignedH <= 0 || ItemWidth <= 0)
        {
            _extent = new Size(_viewport.Width, 0);
            return;
        }

        int cols = ComputeColumns(_viewport.Width);
        int rows = (count + cols - 1) / cols;
        _extent = new Size(_viewport.Width, rows * alignedH);
    }

    private void OnItemsChanged(ItemsChange _)
    {
        RecomputeExtent();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private static IDataTemplate CreateDefaultItemTemplate()
        => new DelegateTemplate<object?>(
            build: _ => new TextBlock(),
            bind: (view, _, index, _) =>
            {
                if (view is TextBlock label) label.Text = index.ToString();
            });
}
