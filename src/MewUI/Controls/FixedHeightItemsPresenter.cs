namespace Aprillz.MewUI.Controls;

using Aprillz.MewUI.Rendering;

/// <summary>
/// Fixed-height virtualizing items presenter intended to be hosted by a scroll owner
/// (e.g. <see cref="ScrollViewer"/>) via <see cref="IScrollContent"/>.
/// </summary>
internal sealed class FixedHeightItemsPresenter : Control, IVisualTreeHost, IScrollContent
    , IItemsPresenter
{
    private readonly TemplatedItemsHost _itemsHost;

    private Size _viewport;
    private Point _offset;
    private Size _extent;
    private double _extentWidth = double.NaN;
    private double _itemRadius;
    private int _pendingScrollIntoViewIndex = -1;

    public IItemsView ItemsSource
    {
        get => _itemsSource;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_itemsSource, value))
            {
                return;
            }

            if (_itemsSource != null)
            {
                _itemsSource.Changed -= OnItemsChanged;
            }

            _itemsSource = value;
            _itemsSource.Changed += OnItemsChanged;

            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private IItemsView _itemsSource = ItemsView.Empty;

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

    public double ItemHeight
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    } = 28;

    public double ExtentWidth
    {
        get => _extentWidth;
        set
        {
            if (Set(ref _extentWidth, value))
            {
                RecomputeExtent();
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    public double ItemRadius
    {
        get => _itemRadius;
        set
        {
            if (Set(ref _itemRadius, value))
            {
                InvalidateVisual();
            }
        }
    }

    public Action<IGraphicsContext, int, Rect>? BeforeItemRender { get; set; }

    public Func<int, Rect, Rect>? GetContainerRect { get; set; }

    public Thickness ItemPadding { get; set; }

    public bool RebindExisting { get; set; } = true;

    public double ItemHeightHint
    {
        get => ItemHeight;
        set => ItemHeight = value;
    }

    public bool UseHorizontalExtentForLayout { get; set; }

    public double DesiredContentHeight
    {
        get
        {
            int count = ItemsSource.Count;
            double h = GetPixelAlignedItemHeight();
            return count == 0 || h <= 0 ? 0 : Math.Min(count * h, h * 12);
        }
    }

    public bool FillsAvailableWidth => false;
    public bool IsNonVirtualized => false;

    public event Action<Point>? OffsetCorrectionRequested;

    public void RecycleAll() => _itemsHost.RecycleAll();

    public void VisitRealized(Action<Element> visitor) => _itemsHost.VisitRealized(visitor);

    public bool VisitRealized(Func<Element, bool> visitor) => _itemsHost.VisitRealized(visitor);

    public void VisitRealized(Action<int, FrameworkElement> visitor) => _itemsHost.VisitRealized(visitor);

    public FixedHeightItemsPresenter()
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
        if (_viewport == viewport)
        {
            return;
        }

        _viewport = viewport;
        RecomputeExtent();
        InvalidateArrange();
    }

    public void SetOffset(Point offset)
    {
        // The scroll owner clamps against its own metrics; be defensive anyway.
        var clamped = new Point(
            Math.Clamp(offset.X, 0, Math.Max(0, Extent.Width - _viewport.Width)),
            Math.Clamp(offset.Y, 0, Math.Max(0, Extent.Height - _viewport.Height)));

        if (_offset == clamped)
        {
            return;
        }

        _offset = clamped;
        InvalidateArrange();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        return _itemsHost.VisitRealized(visitor);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        // We are scroll-driven; desired size is the viewport slot.
        // Extent is reported via IScrollContent.Extent.
        RecomputeExtent();
        return new Size(
            Math.Max(0, availableSize.Width),
            Math.Max(0, availableSize.Height));
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (ItemsSource.Count == 0)
        {
            return;
        }

        double itemHeight = Math.Max(0, ItemHeight);
        if (itemHeight <= 0 || _viewport.Height <= 0)
        {
            _itemsHost.RecycleAll();
            return;
        }

        var dpiScale = GetDpi() / 96.0;
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(bounds, dpiScale);

        double alignedItemHeight = LayoutRounding.RoundToPixel(itemHeight, dpiScale);
        double alignedOffsetY = LayoutRounding.RoundToPixel(_offset.Y, dpiScale);
        double alignedOffsetX = LayoutRounding.RoundToPixel(_offset.X, dpiScale);

        if (_pendingScrollIntoViewIndex >= 0)
        {
            double viewportH = _viewport.Height;
            double top = _pendingScrollIntoViewIndex * alignedItemHeight;
            double bottom = top + alignedItemHeight;

            double desiredOffsetY = alignedOffsetY;
            if (top < alignedOffsetY)
            {
                desiredOffsetY = top;
            }
            else if (bottom > alignedOffsetY + viewportH)
            {
                desiredOffsetY = bottom - viewportH;
            }

            desiredOffsetY = Math.Clamp(desiredOffsetY, 0, Math.Max(0, Extent.Height - viewportH));
            double onePx = dpiScale > 0 ? 1.0 / dpiScale : 1.0;
            if (Math.Abs(desiredOffsetY - alignedOffsetY) >= onePx * 0.99)
            {
                alignedOffsetY = desiredOffsetY;
                OffsetCorrectionRequested?.Invoke(new Point(_offset.X, desiredOffsetY));
                InvalidateMeasure();
            }
            else
            {
                _pendingScrollIntoViewIndex = -1;
            }
        }

        ItemsViewportMath.ComputeVisibleRange(
            ItemsSource.Count,
            alignedItemHeight,
            contentBounds.Height,
            contentBounds.Y,
            alignedOffsetY,
            out int first,
            out int lastExclusive,
            out double yStart,
            out _);

        double layoutWidth = UseHorizontalExtentForLayout
            ? Math.Max(contentBounds.Width, Extent.Width)
            : contentBounds.Width;

        var scrollContentBounds = new Rect(
            contentBounds.X - alignedOffsetX,
            contentBounds.Y,
            layoutWidth,
            contentBounds.Height);

        _itemsHost.Layout = new TemplatedItemsHost.ItemsRangeLayout
        {
            ContentBounds = scrollContentBounds,
            First = first,
            LastExclusive = lastExclusive,
            ItemHeight = alignedItemHeight,
            YStart = LayoutRounding.RoundToPixel(yStart, dpiScale),
            ItemRadius = ItemRadius,
            RebindExisting = RebindExisting,
        };

        var userGetContainerRect = GetContainerRect;
        var pad = ItemPadding;
        Func<int, Rect, Rect>? effectiveGetContainerRect;
        if (pad == default)
        {
            effectiveGetContainerRect = userGetContainerRect;
        }
        else if (userGetContainerRect != null)
        {
            effectiveGetContainerRect = (i, r) => userGetContainerRect(i, r).Deflate(pad);
        }
        else
        {
            effectiveGetContainerRect = (_, r) => r.Deflate(pad);
        }

        _itemsHost.Options = new TemplatedItemsHost.ItemsRangeOptions
        {
            BeforeItemRender = BeforeItemRender,
            GetContainerRect = effectiveGetContainerRect,
        };

        _itemsHost.Arrange();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        _itemsHost.RenderArranged(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        // We are arranged in window coordinates by the scroll owner.
        // Prevent "ghost" hits outside the visible (clipped) viewport.
        if (!Bounds.Contains(point))
        {
            return null;
        }

        UIElement? hit = null;
        _itemsHost.VisitRealized(element =>
        {
            if (hit != null)
            {
                return;
            }

            if (element is UIElement ui)
            {
                hit = ui.HitTest(point);
            }
        });

        return hit ?? this;
    }

    private void RecomputeExtent()
    {
        double itemHeight = GetPixelAlignedItemHeight();
        double height = ItemsSource.Count == 0 || itemHeight <= 0 ? 0 : ItemsSource.Count * itemHeight;

        double width = double.IsNaN(_extentWidth) ? _viewport.Width : _extentWidth;
        _extent = new Size(Math.Max(0, width), Math.Max(0, height));
    }

    private void OnItemsChanged(ItemsChange _)
    {
        RecomputeExtent();
        InvalidateMeasure();
        InvalidateVisual();
    }

    public bool TryGetItemIndexAtY(double yContent, out int index)
    {
        index = -1;

        int count = ItemsSource.Count;
        if (count <= 0)
        {
            return false;
        }

        double itemHeight = GetPixelAlignedItemHeight();
        if (itemHeight <= 0)
        {
            return false;
        }

        int i = (int)Math.Floor(yContent / itemHeight);
        if (i < 0 || i >= count)
        {
            return false;
        }

        index = i;
        return true;
    }

    public bool TryGetItemYRange(int index, out double top, out double bottom)
    {
        top = 0;
        bottom = 0;

        int count = ItemsSource.Count;
        if (count <= 0 || index < 0 || index >= count)
        {
            return false;
        }

        double itemHeight = GetPixelAlignedItemHeight();
        if (itemHeight <= 0)
        {
            return false;
        }

        top = index * itemHeight;
        bottom = top + itemHeight;
        return true;
    }

    // Must match the pixel-aligned height used in ArrangeContent so hit-test
    // and ScrollIntoView stay consistent with the visual layout (esp. at 125% DPI,
    // where 26 DIP rounds to 26.4 DIP per item).
    private double GetPixelAlignedItemHeight()
    {
        double itemHeight = Math.Max(0, ItemHeight);
        if (itemHeight <= 0 || double.IsNaN(itemHeight) || double.IsInfinity(itemHeight))
        {
            return 0;
        }

        var dpiScale = GetDpi() / 96.0;
        return LayoutRounding.RoundToPixel(itemHeight, dpiScale);
    }

    public void RequestScrollIntoView(int index)
    {
        int count = ItemsSource.Count;
        if (count <= 0 || index < 0 || index >= count)
        {
            return;
        }

        _pendingScrollIntoViewIndex = index;
        InvalidateArrange();
    }

    private static IDataTemplate CreateDefaultItemTemplate()
        => new DelegateTemplate<object?>(
            build: _ => new TextBlock(),
            bind: (view, _, index, _) =>
            {
                if (view is TextBlock label)
                {
                    label.Text = index.ToString();
                }
            });
}
