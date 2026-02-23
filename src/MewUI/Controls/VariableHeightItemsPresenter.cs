using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Variable-height virtualizing items presenter intended to be hosted by a scroll owner
/// (e.g. <see cref="ScrollViewer"/>) via <see cref="IScrollContent"/>.
/// </summary>
/// <remarks>
/// This presenter maintains a per-index height cache (in DIPs) and uses an estimated height for
/// items that haven't been measured yet. When measurements refine cached heights, it requests
/// scroll offset corrections so the current viewport anchor remains stable (prevents jump).
/// </remarks>
internal sealed class VariableHeightItemsPresenter : Control, IVisualTreeHost, IScrollContent
    , IItemsPresenter
{
    private readonly Dictionary<FrameworkElement, TemplateContext> _contexts = new();

    private readonly Dictionary<int, FrameworkElement> _realized = new();
    private readonly Stack<FrameworkElement> _pool = new();
    private readonly Dictionary<int, FrameworkElement> _recycledByIndex = new();
    private readonly List<int> _recycleScratch = new();

    private UIElement? _deferredFocusedElement;
    private UIElement? _deferredFocusOwner;
    private int? _deferredFocusedIndex;

    private Size _viewport;
    private Point _offset;

    private Size _extent;
    private double _extentWidth = double.NaN;

    private readonly List<double> _heights = new(); // <=0 means unknown (uses estimate)
    private double[]? _prefix; // length = count+1
    private bool _prefixValid;

    private bool _isRequestingOffsetCorrection;
    private bool _stickToBottom;
    private int _pendingScrollIntoViewIndex = -1;

    public event Action<Point>? OffsetCorrectionRequested;

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

            ResetHeights();
            RecycleAll();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }
    private IItemsView _itemsSource = ItemsView.Empty;

    public bool UseHorizontalExtentForLayout { get; set; }

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
            _pool.Clear();
            _contexts.Clear();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }
    private IDataTemplate _itemTemplate = CreateDefaultItemTemplate();

    public double EstimatedItemHeight
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidatePrefix();
                RecomputeExtent();
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

    public Action<IGraphicsContext, int, Rect>? BeforeItemRender { get; set; }

    public Func<int, Rect, Rect>? GetContainerRect { get; set; }

    public bool RebindExisting { get; set; } = false;

    public double ItemRadius
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

    public double ItemHeightHint
    {
        get => EstimatedItemHeight;
        set => EstimatedItemHeight = value;
    }

    public VariableHeightItemsPresenter()
    {
        BorderThickness = 0;
        Padding = new Thickness(0);
        _itemsSource.Changed += OnItemsChanged;
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
        var dpiScale = GetDpi() / 96.0;
        var onePx = dpiScale > 0 ? 1.0 / dpiScale : 1.0;
        double maxY = Math.Max(0, Extent.Height - _viewport.Height);
        _stickToBottom = offset.Y >= maxY - onePx * 1.5;

        var clamped = new Point(
            Math.Clamp(offset.X, 0, Math.Max(0, Extent.Width - _viewport.Width)),
            Math.Clamp(offset.Y, 0, maxY));

        if (_offset == clamped)
        {
            return;
        }

        _offset = clamped;
        InvalidateVisual();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        foreach (var element in _realized.Values)
        {
            if (!visitor(element))
            {
                return false;
            }
        }
        return true;
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
        if (count <= 0)
        {
            RecycleAll();
            return;
        }

        if (_viewport.Height <= 0)
        {
            RecycleAll();
            return;
        }

        EnsureHeightsCapacity(count);

        var dpiScale = GetDpi() / 96.0;
        var viewportBounds = Bounds;
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(viewportBounds, dpiScale);

        // Keep offsets stable at fractional DPI.
        double alignedOffsetY = LayoutRounding.RoundToPixel(_offset.Y, dpiScale);
        double alignedOffsetX = LayoutRounding.RoundToPixel(_offset.X, dpiScale);

        // Handle pending ScrollIntoView requests (estimate-based correction first).
        // If the requested item isn't realized yet, we can't know its actual height immediately,
        // but we can scroll based on cached/estimated heights and then refine once the item gets measured.
        if (_pendingScrollIntoViewIndex >= 0 && !_isRequestingOffsetCorrection)
        {
            EnsurePrefix();
            double top = _prefix![_pendingScrollIntoViewIndex];
            double bottom = top + Math.Max(1, GetEstimatedHeightDip(_pendingScrollIntoViewIndex));
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
            double onePx0 = dpiScale > 0 ? 1.0 / dpiScale : 1.0;
            if (Math.Abs(desiredOffsetY - alignedOffsetY) >= onePx0 * 0.99)
            {
                // Apply the corrected offset immediately for this render pass to avoid a one-frame
                // flash at the old position; the owner will then update _offset via SetOffset.
                alignedOffsetY = desiredOffsetY;
                RequestOffsetCorrection(new Point(_offset.X, desiredOffsetY));
            }
        }

        EnsurePrefix();

        int anchorIndex = FindIndexByY(alignedOffsetY);
        double anchorTop = _prefix![anchorIndex];
        double anchorWithin = alignedOffsetY - anchorTop;

        double onePx = dpiScale > 0 ? 1.0 / dpiScale : 1.0;

        // Visible range (with small overscan).
        int first = Math.Max(0, FindIndexByY(Math.Max(0, alignedOffsetY - EstimatedItemHeight * 2)));
        int lastExclusive = Math.Min(count, FindIndexByY(alignedOffsetY + contentBounds.Height + EstimatedItemHeight * 2) + 1);

        // Recycle out-of-range (no allocations on hot path).
        _recycleScratch.Clear();
        foreach (var key in _realized.Keys)
        {
            if (key < first || key >= lastExclusive)
            {
                _recycleScratch.Add(key);
            }
        }
        for (int i = 0; i < _recycleScratch.Count; i++)
        {
            Recycle(_recycleScratch[i]);
        }

        bool anyHeightChanged = false;

        // Layout realized containers at absolute positions (window coordinates).
        double width = UseHorizontalExtentForLayout
            ? Math.Max(contentBounds.Width, Extent.Width)
            : contentBounds.Width;
        double x = contentBounds.X - alignedOffsetX;

        double yContent = _prefix![first];
        double y = contentBounds.Y + (yContent - alignedOffsetY);

        for (int i = first; i < lastExclusive; i++)
        {
            var element = GetOrCreate(i, RebindExisting);

            // Measure with a width constraint to obtain the real height.
            element.Measure(new Size(Math.Max(0, width), double.PositiveInfinity));

            double desiredH = Math.Max(0, element.DesiredSize.Height);
            double alignedH = LayoutRounding.RoundToPixel(desiredH, dpiScale);
            if (alignedH <= 0 || double.IsNaN(alignedH) || double.IsInfinity(alignedH))
            {
                alignedH = Math.Max(1, LayoutRounding.RoundToPixel(GetEstimatedHeightDip(i), dpiScale));
            }

            if (!HeightsEqual(_heights[i], alignedH))
            {
                _heights[i] = alignedH;
                anyHeightChanged = true;
            }

            var itemRect = new Rect(x, y, width, alignedH);
            var containerRect = GetContainerRect != null ? GetContainerRect(i, itemRect) : itemRect;
            containerRect = LayoutRounding.RoundRectToPixels(containerRect, dpiScale);

            element.Arrange(containerRect);
            BeforeItemRender?.Invoke(context, i, itemRect);
            element.Render(context);

            y += alignedH;
        }

        FlushRecycledByIndexToPool();

        if (anyHeightChanged)
        {
            InvalidatePrefix();
            RecomputeExtent();

            // If the user is pinned to bottom (e.g. chat), always keep the viewport at the end.
            if (_stickToBottom)
            {
                double desiredOffsetY = Math.Max(0, Extent.Height - _viewport.Height);
                if (!_isRequestingOffsetCorrection && Math.Abs(desiredOffsetY - alignedOffsetY) >= onePx * 0.99)
                {
                    RequestOffsetCorrection(new Point(_offset.X, desiredOffsetY));
                }
                return;
            }

            // After refining heights, re-run ScrollIntoView correction for the pending target
            // using the now-accurate prefix/height data.
            if (_pendingScrollIntoViewIndex >= 0)
            {
                EnsurePrefix();
                double top = _prefix![_pendingScrollIntoViewIndex];
                double bottom = top + Math.Max(1, GetEstimatedHeightDip(_pendingScrollIntoViewIndex));
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
                if (!_isRequestingOffsetCorrection && Math.Abs(desiredOffsetY - alignedOffsetY) >= onePx * 0.99)
                {
                    RequestOffsetCorrection(new Point(_offset.X, desiredOffsetY));
                }
                else
                {
                    // Target is in view with the updated measurements.
                    _pendingScrollIntoViewIndex = -1;
                }
                return;
            }

            // Anchor correction: preserve the logical position within the anchor item.
            EnsurePrefix();
            if (anchorIndex >= 0 && anchorIndex < count)
            {
                double newAnchorTop = _prefix![anchorIndex];
                double desiredOffsetY = newAnchorTop + anchorWithin;
                desiredOffsetY = Math.Clamp(desiredOffsetY, 0, Math.Max(0, Extent.Height - _viewport.Height));

                // Only request correction when the difference is at least one device pixel.
                if (!_isRequestingOffsetCorrection && Math.Abs(desiredOffsetY - alignedOffsetY) >= onePx * 0.99)
                {
                    RequestOffsetCorrection(new Point(_offset.X, desiredOffsetY));
                }
            }
        }

        // Clear pending request once the target is actually realized (measured) and within view.
        if (_pendingScrollIntoViewIndex >= 0 && _realized.ContainsKey(_pendingScrollIntoViewIndex))
        {
            EnsurePrefix();
            double top = _prefix![_pendingScrollIntoViewIndex];
            double bottom = top + Math.Max(1, GetEstimatedHeightDip(_pendingScrollIntoViewIndex));
            double viewportH = _viewport.Height;
            if (top >= alignedOffsetY - onePx * 0.99 && bottom <= alignedOffsetY + viewportH + onePx * 0.99)
            {
                _pendingScrollIntoViewIndex = -1;
            }
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

        UIElement? hit = null;
        foreach (var element in _realized.Values)
        {
            if (hit != null)
            {
                break;
            }

            if (element is UIElement ui)
            {
                hit = ui.HitTest(point);
            }
        }

        return hit ?? this;
    }

    public void RecycleAll()
    {
        _recycleScratch.Clear();
        foreach (var index in _realized.Keys)
        {
            _recycleScratch.Add(index);
        }
        for (int i = 0; i < _recycleScratch.Count; i++)
        {
            Recycle(_recycleScratch[i]);
        }
    }

    public void VisitRealized(Action<Element> visitor)
    {
        foreach (var key in _realized.Keys.OrderBy(static k => k))
        {
            visitor(_realized[key]);
        }
    }

    public void VisitRealized(Action<int, FrameworkElement> visitor)
    {
        foreach (var key in _realized.Keys.OrderBy(static k => k))
        {
            visitor(key, _realized[key]);
        }
    }

    public bool TryGetItemIndexAtY(double yContent, out int index)
    {
        int count = ItemsSource.Count;
        if (count <= 0)
        {
            index = -1;
            return false;
        }

        EnsureHeightsCapacity(count);
        EnsurePrefix();

        index = FindIndexByY(yContent);
        return index >= 0 && index < count;
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

        EnsureHeightsCapacity(count);
        EnsurePrefix();

        // If the container is realized, prefer measuring it with the current viewport width so we can
        // return an accurate range (instead of estimated heights). This is especially important for
        // ScrollIntoView in variable-height mode.
        if (_realized.TryGetValue(index, out var realized))
        {
            double w = Math.Max(0, _viewport.Width);
            if (w > 0 && !double.IsNaN(w) && !double.IsInfinity(w))
            {
                realized.Measure(new Size(w, double.PositiveInfinity));

                var dpiScale = GetDpi() / 96.0;
                double desiredH = Math.Max(0, realized.DesiredSize.Height);
                double alignedH = LayoutRounding.RoundToPixel(desiredH, dpiScale);
                if (alignedH <= 0 || double.IsNaN(alignedH) || double.IsInfinity(alignedH))
                {
                    alignedH = Math.Max(1, LayoutRounding.RoundToPixel(GetEstimatedHeightDip(index), dpiScale));
                }

                if (!HeightsEqual(_heights[index], alignedH))
                {
                    _heights[index] = alignedH;
                    InvalidatePrefix();
                    RecomputeExtent();
                }
            }
        }

        double h = _heights[index];
        if (h <= 0 || double.IsNaN(h) || double.IsInfinity(h))
        {
            h = Math.Max(0, GetEstimatedHeightDip(index));
        }

        top = _prefix![index];
        bottom = top + h;
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

    private double GetEstimatedHeightDip(int index)
    {
        var h = _heights[index];
        if (h > 0)
        {
            return h;
        }

        return Math.Max(1, EstimatedItemHeight);
    }

    private int FindIndexByY(double yContent)
    {
        int count = ItemsSource.Count;
        if (count <= 0)
        {
            return 0;
        }

        yContent = Math.Clamp(yContent, 0, Math.Max(0, _prefix![count]));

        // Find largest i such that prefix[i] <= yContent. prefix is non-decreasing.
        // Use Array.BinarySearch over prefix[0..count] and adjust.
        var prefix = _prefix!;
        int lo = 0;
        int hi = count;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (prefix[mid + 1] <= yContent)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private void EnsureHeightsCapacity(int count)
    {
        if (_heights.Count == count)
        {
            return;
        }

        if (_heights.Count < count)
        {
            int add = count - _heights.Count;
            for (int i = 0; i < add; i++)
            {
                _heights.Add(-1);
            }
        }
        else
        {
            _heights.RemoveRange(count, _heights.Count - count);
        }

        InvalidatePrefix();
        RecomputeExtent();
    }

    private void EnsurePrefix()
    {
        int count = ItemsSource.Count;
        if (_prefixValid && _prefix != null && _prefix.Length == count + 1)
        {
            return;
        }

        _prefix ??= new double[count + 1];
        if (_prefix.Length != count + 1)
        {
            _prefix = new double[count + 1];
        }

        _prefix[0] = 0;
        double sum = 0;
        for (int i = 0; i < count; i++)
        {
            double h = GetEstimatedHeightDip(i);
            sum += h;
            _prefix[i + 1] = sum;
        }

        _prefixValid = true;
    }

    private void InvalidatePrefix() => _prefixValid = false;

    private void ResetHeights()
    {
        _heights.Clear();
        _prefix = null;
        _prefixValid = false;
        RecomputeExtent();
    }

    private void RecomputeExtent()
    {
        int count = ItemsSource.Count;
        double width = double.IsNaN(_extentWidth) ? _viewport.Width : _extentWidth;

        double height;
        if (count <= 0)
        {
            height = 0;
        }
        else
        {
            if (_prefixValid && _prefix != null && _prefix.Length == count + 1)
            {
                height = _prefix[count];
            }
            else
            {
                double sum = 0;
                for (int i = 0; i < count && i < _heights.Count; i++)
                {
                    sum += GetEstimatedHeightDip(i);
                }

                int remaining = count - _heights.Count;
                if (remaining > 0)
                {
                    sum += remaining * Math.Max(1, EstimatedItemHeight);
                }

                height = sum;
            }
        }

        _extent = new Size(Math.Max(0, width), Math.Max(0, height));
    }

    private FrameworkElement CreateItemContainer()
    {
        var ctx = new TemplateContext();
        var view = ItemTemplate.Build(ctx);
        _contexts.Add(view, ctx);
        return view;
    }

    private void BindItemContainer(FrameworkElement element, int index)
    {
        var item = ItemsSource.GetItem(index);

        if (!_contexts.TryGetValue(element, out var ctx))
        {
            ctx = new TemplateContext();
            _contexts.Add(element, ctx);
        }

        ctx.Reset();
        ItemTemplate.Bind(element, item, index, ctx);
    }

    private void UnbindItemContainer(FrameworkElement element)
    {
        if (_contexts.TryGetValue(element, out var ctx))
        {
            ctx.Reset();
        }
    }

    private FrameworkElement GetOrCreate(int index, bool rebindExisting)
    {
        if (_realized.TryGetValue(index, out var existing))
        {
            if (rebindExisting)
            {
                BindItemContainer(existing, index);
            }

            return existing;
        }

        FrameworkElement element;
        if (_recycledByIndex.Remove(index, out var recycled))
        {
            element = recycled;
        }
        else
        {
            element = _pool.Count > 0 ? _pool.Pop() : CreateItemContainer();
        }

        element.Parent = this;
        element.IsVisible = true;
        BindItemContainer(element, index);
        _realized[index] = element;
        TryRestoreDeferredFocus(element, index);
        return element;
    }

    private void Recycle(int index)
    {
        if (!_realized.Remove(index, out var element))
        {
            return;
        }

        if (element is UIElement uiElement && FindVisualRoot() is Window window)
        {
            var focused = window.FocusManager.FocusedElement;
            if (focused != null && VisualTree.IsInSubtreeOf(focused, uiElement))
            {
                _deferredFocusedElement = focused;
                _deferredFocusedIndex = index;

                if (Focusable && IsEffectivelyEnabled && IsVisible)
                {
                    _deferredFocusOwner = this;
                    window.FocusManager.SetFocus(this);
                }
                else
                {
                    _deferredFocusOwner = null;
                    window.FocusManager.ClearFocus();
                }
            }
        }

        UnbindItemContainer(element);
        element.Parent = null;

        if (!_recycledByIndex.TryAdd(index, element))
        {
            _pool.Push(element);
        }
    }

    private void FlushRecycledByIndexToPool()
    {
        if (_recycledByIndex.Count == 0)
        {
            return;
        }

        foreach (var element in _recycledByIndex.Values)
        {
            _pool.Push(element);
        }

        _recycledByIndex.Clear();
    }

    private void RemapRealizedIndicesAfterInsert(int insertIndex, int count)
    {
        if (count <= 0)
        {
            return;
        }

        if (_deferredFocusedIndex is int deferredIndex && deferredIndex >= insertIndex)
        {
            _deferredFocusedIndex = deferredIndex + count;
        }

        if (_realized.Count > 0)
        {
            var remapped = new Dictionary<int, FrameworkElement>(_realized.Count);
            foreach (var (index, element) in _realized)
            {
                int newIndex = index >= insertIndex ? index + count : index;
                remapped[newIndex] = element;
            }
            _realized.Clear();
            foreach (var (idx, el) in remapped)
            {
                _realized[idx] = el;
            }
        }

        if (_recycledByIndex.Count > 0)
        {
            var remapped = new Dictionary<int, FrameworkElement>(_recycledByIndex.Count);
            foreach (var (index, element) in _recycledByIndex)
            {
                int newIndex = index >= insertIndex ? index + count : index;
                remapped[newIndex] = element;
            }
            _recycledByIndex.Clear();
            foreach (var (idx, el) in remapped)
            {
                _recycledByIndex[idx] = el;
            }
        }
    }

    private void RemapRealizedIndicesAfterRemove(int removeIndex, int removeCount)
    {
        if (removeCount <= 0)
        {
            return;
        }

        int removeEndExclusive = removeIndex + removeCount;

        if (_deferredFocusedIndex is int deferredIndex)
        {
            if (deferredIndex >= removeEndExclusive)
            {
                _deferredFocusedIndex = deferredIndex - removeCount;
            }
            else if (deferredIndex >= removeIndex)
            {
                _deferredFocusedIndex = null;
                _deferredFocusedElement = null;
                _deferredFocusOwner = null;
            }
        }

        // Recycle realized items that were removed.
        _recycleScratch.Clear();
        foreach (var index in _realized.Keys)
        {
            if (index >= removeIndex && index < removeEndExclusive)
            {
                _recycleScratch.Add(index);
            }
        }
        for (int i = 0; i < _recycleScratch.Count; i++)
        {
            Recycle(_recycleScratch[i]);
        }

        if (_realized.Count > 0)
        {
            var remapped = new Dictionary<int, FrameworkElement>(_realized.Count);
            foreach (var (index, element) in _realized)
            {
                int newIndex = index >= removeEndExclusive ? index - removeCount : index;
                remapped[newIndex] = element;
            }
            _realized.Clear();
            foreach (var (idx, el) in remapped)
            {
                _realized[idx] = el;
            }
        }

        if (_recycledByIndex.Count > 0)
        {
            // Any recycled-by-index in the removed range is no longer meaningful; return to pool.
            _recycleScratch.Clear();
            foreach (var index in _recycledByIndex.Keys)
            {
                if (index >= removeIndex && index < removeEndExclusive)
                {
                    _recycleScratch.Add(index);
                }
            }
            for (int i = 0; i < _recycleScratch.Count; i++)
            {
                int idx = _recycleScratch[i];
                if (_recycledByIndex.Remove(idx, out var element))
                {
                    _pool.Push(element);
                }
            }

            if (_recycledByIndex.Count > 0)
            {
                var remapped = new Dictionary<int, FrameworkElement>(_recycledByIndex.Count);
                foreach (var (index, element) in _recycledByIndex)
                {
                    int newIndex = index >= removeEndExclusive ? index - removeCount : index;
                    remapped[newIndex] = element;
                }
                _recycledByIndex.Clear();
                foreach (var (idx, el) in remapped)
                {
                    _recycledByIndex[idx] = el;
                }
            }
        }
    }

    private void TryRestoreDeferredFocus(FrameworkElement container, int index)
    {
        if (_deferredFocusedIndex != index)
        {
            return;
        }

        var deferred = _deferredFocusedElement;
        if (deferred == null)
        {
            return;
        }

        if (FindVisualRoot() is not Window window)
        {
            return;
        }

        if (_deferredFocusOwner != null)
        {
            if (!ReferenceEquals(window.FocusManager.FocusedElement, _deferredFocusOwner))
            {
                return;
            }
        }
        else
        {
            if (window.FocusManager.FocusedElement != null)
            {
                return;
            }
        }

        if (container is not Element root || !VisualTree.IsInSubtreeOf(deferred, root))
        {
            return;
        }

        if (!deferred.Focusable || !deferred.IsEffectivelyEnabled || !deferred.IsVisible)
        {
            _deferredFocusedElement = null;
            _deferredFocusOwner = null;
            return;
        }

        window.FocusManager.SetFocus(deferred);
        _deferredFocusedElement = null;
        _deferredFocusOwner = null;
        _deferredFocusedIndex = null;
    }

    private void RequestOffsetCorrection(Point correctedOffset)
    {
        _isRequestingOffsetCorrection = true;
        try
        {
            OffsetCorrectionRequested?.Invoke(correctedOffset);
        }
        finally
        {
            _isRequestingOffsetCorrection = false;
        }
    }

    private void OnItemsChanged(ItemsChange change)
    {
        // Keep height cache aligned with indices so prepend/append doesn't destroy scroll anchor.
        // IMPORTANT: ItemsSource.Count is already the NEW count when this callback runs, so we must
        // capture the anchor using the OLD count/_heights BEFORE touching EnsurePrefix().
        int newCount = ItemsSource.Count;
        int heightsCount = _heights.Count;

        int oldCountForAnchor = change.Kind switch
        {
            ItemsChangeKind.Add => Math.Max(0, newCount - Math.Max(0, change.Count)),
            ItemsChangeKind.Remove => newCount + Math.Max(0, change.Count),
            _ => newCount
        };

        // Cap to the cache we actually have. If we're out of sync, fall back to a reset.
        if (oldCountForAnchor > heightsCount)
        {
            ResetHeights();
            RecycleAll();
            EnsureHeightsCapacity(newCount);
            InvalidateMeasure();
            InvalidateVisual();
            return;
        }

        var dpiScale = GetDpi() / 96.0;
        double alignedOffsetY = LayoutRounding.RoundToPixel(_offset.Y, dpiScale);

        // Local prefix for old count (pre-change), to avoid IndexOutOfRange during Add/Remove.
        double[] oldPrefix = new double[oldCountForAnchor + 1];
        double sum = 0;
        oldPrefix[0] = 0;
        for (int i = 0; i < oldCountForAnchor; i++)
        {
            sum += GetEstimatedHeightDip(i);
            oldPrefix[i + 1] = sum;
        }

        int anchorIndex = FindIndexByY(oldPrefix, oldCountForAnchor, alignedOffsetY);
        double anchorWithin = alignedOffsetY - oldPrefix[anchorIndex];

        bool requestAnchorCorrection = false;
        double correctionDelta = 0;

        switch (change.Kind)
        {
            case ItemsChangeKind.Reset:
                ResetHeights();
                RecycleAll();
                break;

            case ItemsChangeKind.Add:
                if (change.Count > 0)
                {
                    int insertIndex = Math.Clamp(change.Index, 0, _heights.Count);
                    _heights.InsertRange(insertIndex, Enumerable.Repeat(-1d, change.Count));
                    RemapRealizedIndicesAfterInsert(insertIndex, change.Count);

                    if (insertIndex <= anchorIndex)
                    {
                        requestAnchorCorrection = true;
                        // Best-effort using estimates (new items are unknown).
                        correctionDelta = change.Count * Math.Max(1, EstimatedItemHeight);
                    }
                }
                break;

            case ItemsChangeKind.Remove:
                if (change.Count > 0 && _heights.Count > 0)
                {
                    int removeIndex = Math.Clamp(change.Index, 0, _heights.Count);
                    int removeCount = Math.Min(change.Count, _heights.Count - removeIndex);
                    if (removeCount > 0)
                    {
                        if (removeIndex < anchorIndex)
                        {
                            requestAnchorCorrection = true;

                            int affected = Math.Min(removeCount, anchorIndex - removeIndex);
                            double removed = 0;
                            for (int i = 0; i < affected; i++)
                            {
                                removed += GetEstimatedHeightDip(removeIndex + i);
                            }

                            correctionDelta = -removed;
                        }

                        _heights.RemoveRange(removeIndex, removeCount);
                        RemapRealizedIndicesAfterRemove(removeIndex, removeCount);
                    }
                }
                break;

            case ItemsChangeKind.Replace:
                if (change.Count > 0)
                {
                    int start = Math.Clamp(change.Index, 0, _heights.Count);
                    int c = Math.Min(change.Count, _heights.Count - start);
                    for (int i = 0; i < c; i++)
                    {
                        _heights[start + i] = -1;
                    }

                    // Rebind realized containers in the replaced range once (no per-frame rebinding).
                    for (int i = 0; i < c; i++)
                    {
                        int index = start + i;
                        if (_realized.TryGetValue(index, out var element))
                        {
                            BindItemContainer(element, index);
                        }
                    }
                }
                break;

            case ItemsChangeKind.Move:
                // Conservative fallback: reset heights and recycle. (Can be optimized later.)
                ResetHeights();
                RecycleAll();
                break;
        }

        // Normalize cache size to the new count.
        EnsureHeightsCapacity(newCount);
        InvalidatePrefix();
        RecomputeExtent();

        if (_stickToBottom && !_isRequestingOffsetCorrection)
        {
            double desiredOffsetY = Math.Max(0, Extent.Height - _viewport.Height);
            if (Math.Abs(desiredOffsetY - alignedOffsetY) > (1.0 / dpiScale) * 0.5)
            {
                RequestOffsetCorrection(new Point(_offset.X, desiredOffsetY));
            }

            InvalidateMeasure();
            InvalidateVisual();
            return;
        }

        if (requestAnchorCorrection && !_isRequestingOffsetCorrection)
        {
            // Best-effort correction using estimates; refined later by measurement-based anchor correction.
            double desiredOffsetY = alignedOffsetY + correctionDelta;
            desiredOffsetY = Math.Clamp(desiredOffsetY, 0, Math.Max(0, Extent.Height - _viewport.Height));
            if (Math.Abs(desiredOffsetY - alignedOffsetY) > (1.0 / dpiScale) * 0.5)
            {
                RequestOffsetCorrection(new Point(_offset.X, desiredOffsetY));
            }
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    private static int FindIndexByY(double[] prefix, int count, double yContent)
    {
        if (count <= 0)
        {
            return 0;
        }

        yContent = Math.Clamp(yContent, 0, Math.Max(0, prefix[count]));

        int lo = 0;
        int hi = count;
        while (lo < hi)
        {
            int mid = lo + ((hi - lo) / 2);
            if (prefix[mid + 1] <= yContent)
            {
                lo = mid + 1;
            }
            else
            {
                hi = mid;
            }
        }

        return lo;
    }

    private static bool HeightsEqual(double a, double b)
    {
        if (a <= 0 && b <= 0)
        {
            return true;
        }

        return a.Equals(b);
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
