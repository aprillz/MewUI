using System.Runtime.CompilerServices;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Non-virtualizing items presenter that realizes all items and stacks them vertically.
/// Each item is measured individually to determine its actual height.
/// </summary>
internal sealed class StackItemsPresenter : Control, IItemsPresenter
{
    private readonly List<FrameworkElement> _containers = new();
    private readonly Stack<FrameworkElement> _pool = new();
    private readonly ConditionalWeakTable<FrameworkElement, TemplateContext> _contexts = new();
    private readonly List<double> _measuredHeights = new();

    private IItemsView _itemsSource = ItemsView.Empty;
    private IDataTemplate _itemTemplate;
    private double _totalHeight;
    private double _extentWidth = double.NaN;
    private double _itemRadius;
    private Size _viewport;
    private Size _extent;
    private Point _offset;
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

            SyncContainers();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public IDataTemplate ItemTemplate
    {
        get => _itemTemplate;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ReferenceEquals(_itemTemplate, value))
            {
                return;
            }

            _itemTemplate = value;
            RecycleAll();
            SyncContainers();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    public Action<IGraphicsContext, int, Rect>? BeforeItemRender { get; set; }

    public Func<int, Rect, Rect>? GetContainerRect { get; set; }

    public double ExtentWidth
    {
        get => _extentWidth;
        set
        {
            if (Set(ref _extentWidth, value))
            {
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

    public Thickness ItemPadding { get; set; }

    public bool RebindExisting { get; set; } = true;

    public double ItemHeightHint { get; set; } = 28;

    public bool UseHorizontalExtentForLayout { get; set; }

    public event Action<Point>? OffsetCorrectionRequested;

    public double DesiredContentHeight => _totalHeight;

    public bool FillsAvailableWidth => true;
    public bool IsNonVirtualized => true;

    /// <summary>
    /// Gets the total measured height of all items.
    /// </summary>
    public double TotalExtentHeight => _totalHeight;

    public StackItemsPresenter()
    {
        _itemTemplate = CreateDefaultItemTemplate();
    }

    // IScrollContent

    public Size Extent => _extent;

    public void SetViewport(Size viewport)
    {
        if (_viewport == viewport)
        {
            return;
        }

        _viewport = viewport;
        RecomputeExtent();
        InvalidateVisual();
    }

    public void SetOffset(Point offset)
    {
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

    // IItemsPresenter

    public void RecycleAll()
    {
        foreach (var container in _containers)
        {
            container.Parent = null;
            _pool.Push(container);
        }

        _containers.Clear();
        _measuredHeights.Clear();
        _totalHeight = 0;
    }

    public void VisitRealized(Action<Element> visitor)
    {
        for (int i = 0; i < _containers.Count; i++)
        {
            visitor(_containers[i]);
        }
    }

    public void VisitRealized(Action<int, FrameworkElement> visitor)
    {
        for (int i = 0; i < _containers.Count; i++)
        {
            visitor(i, _containers[i]);
        }
    }

    public bool TryGetItemIndexAtY(double yContent, out int index)
    {
        index = -1;
        if (_containers.Count == 0 || yContent < 0)
        {
            return false;
        }

        double y = 0;
        for (int i = 0; i < _measuredHeights.Count; i++)
        {
            double h = _measuredHeights[i];
            if (yContent < y + h)
            {
                index = i;
                return true;
            }

            y += h;
        }

        return false;
    }

    public bool TryGetItemYRange(int index, out double top, out double bottom)
    {
        top = 0;
        bottom = 0;

        if (index < 0 || index >= _measuredHeights.Count)
        {
            return false;
        }

        double y = 0;
        for (int i = 0; i < index; i++)
        {
            y += _measuredHeights[i];
        }

        top = y;
        bottom = y + _measuredHeights[index];
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

    // IVisualTreeHost

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        for (int i = 0; i < _containers.Count; i++)
        {
            if (!visitor(_containers[i]))
            {
                return false;
            }
        }

        return true;
    }

    // Layout & Render

    protected override Size MeasureContent(Size availableSize)
    {
        SyncContainers();
        MeasureAllItems(availableSize.Width);
        RecomputeExtent();

        return new Size(
            Math.Max(0, availableSize.Width),
            Math.Max(0, availableSize.Height));
    }

    protected override void OnRender(IGraphicsContext context)
    {
        int count = _containers.Count;
        if (count == 0)
        {
            return;
        }

        var dpiScale = GetDpi() / 96.0;
        var viewportBounds = Bounds;
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(viewportBounds, dpiScale);
        double alignedOffsetY = LayoutRounding.RoundToPixel(_offset.Y, dpiScale);
        double alignedOffsetX = LayoutRounding.RoundToPixel(_offset.X, dpiScale);

        if (_pendingScrollIntoViewIndex >= 0 && _pendingScrollIntoViewIndex < _measuredHeights.Count)
        {
            double top = 0;
            for (int i = 0; i < _pendingScrollIntoViewIndex; i++)
            {
                top += _measuredHeights[i];
            }

            double height = _measuredHeights[_pendingScrollIntoViewIndex];
            double bottom = top + height;
            double viewportH = _viewport.Height;

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
            }
            else
            {
                _pendingScrollIntoViewIndex = -1;
            }
        }

        double layoutWidth = UseHorizontalExtentForLayout
            ? Math.Max(contentBounds.Width, Extent.Width)
            : contentBounds.Width;

        var pad = ItemPadding;
        var userGetContainerRect = GetContainerRect;
        var beforeItemRender = BeforeItemRender;

        double y = contentBounds.Y - alignedOffsetY;
        for (int i = 0; i < count; i++)
        {
            double h = i < _measuredHeights.Count ? _measuredHeights[i] : ItemHeightHint;
            double snappedH = LayoutRounding.RoundToPixel(h, dpiScale);
            double snappedY = LayoutRounding.RoundToPixel(y, dpiScale);

            var itemRect = new Rect(contentBounds.X - alignedOffsetX, snappedY, layoutWidth, snappedH);
            beforeItemRender?.Invoke(context, i, itemRect);

            var containerRect = itemRect;
            if (userGetContainerRect != null)
            {
                containerRect = userGetContainerRect(i, containerRect);
            }

            if (pad != default)
            {
                containerRect = containerRect.Deflate(pad);
            }

            containerRect = LayoutRounding.RoundRectToPixels(containerRect, dpiScale);

            var container = _containers[i];
            container.Measure(new Size(Math.Max(0, containerRect.Width), Math.Max(0, containerRect.Height)));
            container.Arrange(containerRect);
            container.Render(context);

            y += snappedH;
        }
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        if (!Bounds.Contains(point))
        {
            return null;
        }

        for (int i = _containers.Count - 1; i >= 0; i--)
        {
            if (_containers[i] is UIElement ui)
            {
                var hit = ui.HitTest(point);
                if (hit != null)
                {
                    return hit;
                }
            }
        }

        return this;
    }

    // Helpers

    private void SyncContainers()
    {
        int count = _itemsSource.Count;

        // Remove excess
        while (_containers.Count > count)
        {
            int last = _containers.Count - 1;
            var container = _containers[last];
            container.Parent = null;
            _pool.Push(container);
            _containers.RemoveAt(last);
        }

        // Add missing
        while (_containers.Count < count)
        {
            var container = _pool.Count > 0 ? _pool.Pop() : CreateContainer();
            container.Parent = this;
            container.IsVisible = true;
            _containers.Add(container);
        }

        // Bind all
        for (int i = 0; i < count; i++)
        {
            BindContainer(_containers[i], i);
        }
    }

    private void MeasureAllItems(double availableWidth)
    {
        _measuredHeights.Clear();
        _totalHeight = 0;

        double w = Math.Max(0, availableWidth);
        var measureSize = new Size(w, double.PositiveInfinity);

        for (int i = 0; i < _containers.Count; i++)
        {
            var container = _containers[i];
            container.Measure(measureSize);
            double h = container.DesiredSize.Height;
            _measuredHeights.Add(h);
            _totalHeight += h;
        }
    }

    private void RecomputeExtent()
    {
        double width = double.IsNaN(_extentWidth) ? _viewport.Width : _extentWidth;
        _extent = new Size(Math.Max(0, width), Math.Max(0, _totalHeight));
    }

    private void OnItemsChanged(ItemsChange _)
    {
        SyncContainers();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private FrameworkElement CreateContainer()
    {
        var ctx = new TemplateContext();
        var view = _itemTemplate.Build(ctx);
        _contexts.Add(view, ctx);
        return view;
    }

    private void BindContainer(FrameworkElement element, int index)
    {
        var item = _itemsSource.GetItem(index);

        if (!_contexts.TryGetValue(element, out var ctx))
        {
            ctx = new TemplateContext();
            _contexts.Add(element, ctx);
        }

        ctx.Reset();
        _itemTemplate.Bind(element, item, index, ctx);
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
