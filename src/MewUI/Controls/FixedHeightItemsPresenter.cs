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

    public event Action<Point>? OffsetCorrectionRequested;

    public void RecycleAll() => _itemsHost.RecycleAll();

    public void VisitRealized(Action<Element> visitor) => _itemsHost.VisitRealized(visitor);

    public void VisitRealized(Action<int, FrameworkElement> visitor) => _itemsHost.VisitRealized(visitor);

    public FixedHeightItemsPresenter()
    {
        BorderThickness = 0;
        Padding = new Thickness(0);

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
        // Scroll-driven content: viewport changes should not trigger re-measurement loops.
        // The ScrollViewer already owns measuring and will read the updated Extent in the same pass.
        InvalidateVisual();
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
        InvalidateVisual();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        bool stopped = false;
        _itemsHost.VisitRealized(e =>
        {
            if (!stopped && !visitor(e))
                stopped = true;
        });
        return !stopped;
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

    protected override void OnRender(IGraphicsContext context)
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
        // Use our arranged bounds (window coordinates). ScrollViewer doesn't translate scroll-driven content;
        // we render/arrange at absolute positions and rely on the owner to clip.
        var viewportBounds = Bounds;
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(viewportBounds, dpiScale);

        // Keep the item stepping stable at fractional DPI by aligning the item height and offsets to pixels.
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
                // Apply the corrected offset immediately for this render pass to avoid a one-frame
                // "flash" at the old position; the owner will then update _offset via SetOffset.
                alignedOffsetY = desiredOffsetY;
                OffsetCorrectionRequested?.Invoke(new Point(_offset.X, desiredOffsetY));
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

        // Shift the realized containers by horizontal scroll. Keep Y in viewport space.
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

        _itemsHost.Render(context);
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
        double itemHeight = Math.Max(0, ItemHeight);
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

        double itemHeight = Math.Max(0, ItemHeight);
        if (itemHeight <= 0 || double.IsNaN(itemHeight) || double.IsInfinity(itemHeight))
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

        double itemHeight = Math.Max(0, ItemHeight);
        if (itemHeight <= 0 || double.IsNaN(itemHeight) || double.IsInfinity(itemHeight))
        {
            return false;
        }

        top = index * itemHeight;
        bottom = top + itemHeight;
        return true;
    }

    public void RequestScrollIntoView(int index)
    {
        int count = ItemsSource.Count;
        if (count <= 0 || index < 0 || index >= count)
        {
            return;
        }

        _pendingScrollIntoViewIndex = index;
        InvalidateVisual();
    }

    private static IDataTemplate CreateDefaultItemTemplate()
        => new DelegateTemplate<object?>(
            build: _ => new Label(),
            bind: (view, _, index, _) =>
            {
                if (view is Label label)
                {
                    label.Text = index.ToString();
                    label.VerticalTextAlignment = TextAlignment.Center;
                }
            });
}
