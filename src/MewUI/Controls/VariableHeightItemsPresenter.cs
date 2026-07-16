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
internal sealed class VariableHeightItemsPresenter : Control, IItemsPresenter
{
    private readonly Dictionary<FrameworkElement, TemplateContext> _contexts = new();

    private readonly Dictionary<int, FrameworkElement> _realized = new();
    private readonly Dictionary<FrameworkElement, uint> _itemBindingGenerations = new();
    private readonly Stack<FrameworkElement> _pool = new();
    private readonly Dictionary<int, FrameworkElement> _recycledByIndex = new();
    private readonly List<int> _recycleScratch = new();
    private readonly List<(int Index, Rect ItemRect)> _arrangedItems = new();
    private readonly List<(int OldIndex, FrameworkElement Element)> _remapScratch = new();
    private readonly List<double> _insertHeightScratch = new(); // reused -1 fill buffer for InsertRange, avoids Enumerable.Repeat allocation
    private double[] _oldPrefixScratch = Array.Empty<double>(); // reused prefix-sum buffer for OnItemsChanged anchor calc, grow-only
    private HashSet<int>? _pendingRebind;

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

    // Running statistics over measured heights - used to estimate unmeasured items.
    // Replaces the fixed EstimatedItemHeight default for items that haven't been arranged yet.
    private double _measuredHeightSum;
    private int _measuredHeightCount;

    private bool _isRequestingOffsetCorrection;
    private int _pendingScrollIntoViewIndex = -1;

    // Tracks the DPI scale from the last layout pass so we can detect DPI changes
    // and invalidate prefix sums (which are DPI-dependent due to per-item pixel rounding).
    private double _lastDpiScale = 1.0;

    // Set to true on a DPI change so that the very first anchor correction after the change
    // is suppressed. ScrollViewer.OnDpiChanged already preserves the logical DIP offset;
    // we must not let the anchor correction override it with a partially-updated prefix.
    private bool _suppressAnchorCorrectionForDpiChange;

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

            RecycleAll();
            _pool.Clear();
            _recycledByIndex.Clear();
            _contexts.Clear();
            _itemTemplate = value;
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

    public Thickness ItemPadding { get; set; }

    public uint ItemBindingGeneration { get; set; }

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

    public double PreferredViewportHeight
    {
        get
        {
            int count = ItemsSource.Count;
            double h = EstimatedItemHeight;
            return count == 0 || h <= 0 ? 0 : Math.Min(count * h, h * 12);
        }
    }

    public bool FillsAvailableWidth => true;

    public VariableHeightItemsPresenter()
    {
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
        InvalidateArrange();
    }

    public void SetOffset(Point offset)
    {
        double maxY = Math.Max(0, Extent.Height - _viewport.Height);
        var clamped = new Point(
            Math.Clamp(offset.X, 0, Math.Max(0, Extent.Width - _viewport.Width)),
            Math.Clamp(offset.Y, 0, maxY));

        if (_offset == clamped)
        {
            return;
        }

        _offset = clamped;
        InvalidateArrange();
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

    protected override void ArrangeContent(Rect bounds)
    {
        _arrangedItems.Clear();

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

        // If DPI changed, prefix sums built from per-item pixel-rounded DIPs are stale.
        // Invalidate so EnsurePrefix rebuilds them at the new scale. Also suppress the first
        // anchor correction: ScrollViewer already preserved the logical DIP offset, and the
        // prefix will be partially stale (only visible items re-measured) so the correction
        // would produce a wrong result.
        if (dpiScale != _lastDpiScale)
        {
            InvalidatePrefix();
            _lastDpiScale = dpiScale;
            _suppressAnchorCorrectionForDpiChange = true;
        }

        var contentBounds = LayoutRounding.SnapViewportRectToPixels(bounds, dpiScale);

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
                // Apply the corrected offset immediately for this layout pass; the owner
                // will then update _offset via SetOffset.
                alignedOffsetY = desiredOffsetY;
                RequestOffsetCorrection(new Point(_offset.X, desiredOffsetY));
                InvalidateMeasure();
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
                if (!IsFocusedSubtree(key))
                {
                    _recycleScratch.Add(key);
                }
                else
                {
                    // Don't rebind focus-pinned items immediately - it can reset
                    // user-interaction state (e.g. ToggleSwitch.IsChecked).
                    // Defer rebind + style snap until the item re-enters the visible range.
                    (_pendingRebind ??= new()).Add(key);
                }
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
            var element = GetOrCreate(i, ItemBindingGeneration);

            // Measure with ItemPadding-deflated width so the child measures within
            // the actual space it will receive after Arrange Deflate.
            var padding = ItemPadding;
            double measureW = padding != default ? Math.Max(0, width - padding.HorizontalThickness) : width;
            element.Measure(new Size(Math.Max(0, measureW), double.PositiveInfinity));

            // Include ItemPadding in the slot height so Arrange's Deflate
            // doesn't shrink below the child's DesiredSize.
            double desiredH = Math.Max(0, element.DesiredSize.Height);
            if (padding != default)
                desiredH += padding.VerticalThickness;
            double alignedH = LayoutRounding.RoundToPixel(desiredH, dpiScale);
            if (alignedH <= 0 || double.IsNaN(alignedH) || double.IsInfinity(alignedH))
            {
                alignedH = Math.Max(1, LayoutRounding.RoundToPixel(GetEstimatedHeightDip(i), dpiScale));
            }

            if (!HeightsEqual(_heights[i], alignedH))
            {
                RemoveMeasuredHeight(_heights[i]);
                _heights[i] = alignedH;
                AddMeasuredHeight(alignedH);
                anyHeightChanged = true;
            }

            var itemRect = new Rect(x, y, width, alignedH);
            var containerRect = GetContainerRect != null ? GetContainerRect(i, itemRect) : itemRect;
            if (padding != default)
            {
                containerRect = containerRect.Deflate(padding);
            }
            containerRect = LayoutRounding.RoundRectToPixels(containerRect, dpiScale);

            element.Arrange(containerRect);
            _arrangedItems.Add((i, itemRect));

            y += alignedH;
        }

        FlushRecycledByIndexToPool();

        if (anyHeightChanged)
        {
            InvalidatePrefix();
            RecomputeExtent();

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
                    InvalidateMeasure();
                }
                else
                {
                    // Target is in view with the updated measurements.
                    _pendingScrollIntoViewIndex = -1;
                }
                return;
            }

            // Anchor correction: preserve the logical position within the anchor item.
            // Skip on the first render after a DPI change: ScrollViewer already preserved the
            // logical DIP offset, and only visible items have been re-measured so the prefix is
            // partially stale - computing a correction from it would produce a wrong result.
            if (_suppressAnchorCorrectionForDpiChange)
            {
                _suppressAnchorCorrectionForDpiChange = false;
            }
            else
            {
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
                        InvalidateMeasure();
                    }
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

    protected override void OnRender(IGraphicsContext context)
    {
        var beforeItemRender = BeforeItemRender;
        for (int i = 0; i < _arrangedItems.Count; i++)
        {
            var (index, itemRect) = _arrangedItems[i];
            if (!_realized.TryGetValue(index, out var element))
            {
                continue;
            }

            beforeItemRender?.Invoke(context, index, itemRect);
            element.Render(context);
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
        foreach (var key in GetSortedKeys())
        {
            visitor(_realized[key]);
        }
    }

    public bool VisitRealized(Func<Element, bool> visitor)
    {
        if (_realized.Count == 0)
        {
            return true;
        }

        foreach (var key in GetSortedKeys())
        {
            if (!visitor(_realized[key]))
            {
                return false;
            }
        }

        return true;
    }

    public void VisitRealized(Action<int, FrameworkElement> visitor)
    {
        foreach (var key in GetSortedKeys())
        {
            visitor(key, _realized[key]);
        }
    }

    [ThreadStatic] private static List<int>? _sortedKeysBuffer;

    private ReadOnlySpan<int> GetSortedKeys()
    {
        _sortedKeysBuffer ??= new List<int>();
        _sortedKeysBuffer.Clear();

        foreach (var key in _realized.Keys)
        {
            _sortedKeysBuffer.Add(key);
        }

        _sortedKeysBuffer.Sort();
#if NET8_0_OR_GREATER
        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_sortedKeysBuffer);
#else
        return _sortedKeysBuffer.ToArray();
#endif
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
                var padding = ItemPadding;
                double measureW = padding != default ? Math.Max(0, w - padding.HorizontalThickness) : w;
                realized.Measure(new Size(measureW, double.PositiveInfinity));

                var dpiScale = GetDpi() / 96.0;
                double desiredH = Math.Max(0, realized.DesiredSize.Height);
                if (padding != default)
                    desiredH += padding.VerticalThickness;
                double alignedH = LayoutRounding.RoundToPixel(desiredH, dpiScale);
                if (alignedH <= 0 || double.IsNaN(alignedH) || double.IsInfinity(alignedH))
                {
                    alignedH = Math.Max(1, LayoutRounding.RoundToPixel(GetEstimatedHeightDip(index), dpiScale));
                }

                if (!HeightsEqual(_heights[index], alignedH))
                {
                    RemoveMeasuredHeight(_heights[index]);
                    _heights[index] = alignedH;
                    AddMeasuredHeight(alignedH);
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
        InvalidateArrange();
    }

    private double GetEstimatedHeightDip(int index)
    {
        var h = _heights[index];
        if (h > 0)
        {
            return h;
        }

        // Pixel-align the estimate so the prefix sum advances in the same whole-pixel
        // increments that anchor correction applies to the offset. Measured heights are
        // already pixel-rounded at measurement; matching the estimate keeps the prefix
        // grid consistent for both measured and unmeasured items, eliminating sub-pixel
        // drift across rapid INCC bursts where many unmeasured items are inserted.
        return Math.Max(1, GetRunningEstimateOrDefault());
    }

    private double GetRunningEstimateOrDefault()
    {
        double raw = _measuredHeightCount > 0
            ? _measuredHeightSum / _measuredHeightCount
            : EstimatedItemHeight;
        double dpiScale = GetDpi() / 96.0;
        if (dpiScale <= 0) dpiScale = 1.0;
        return LayoutRounding.RoundToPixel(raw, dpiScale);
    }

    private void AddMeasuredHeight(double height)
    {
        if (height > 0)
        {
            _measuredHeightSum += height;
            _measuredHeightCount++;
        }
    }

    private void RemoveMeasuredHeight(double height)
    {
        if (height > 0 && _measuredHeightCount > 0)
        {
            _measuredHeightSum = Math.Max(0, _measuredHeightSum - height);
            _measuredHeightCount--;
            if (_measuredHeightCount == 0)
            {
                _measuredHeightSum = 0;
            }
        }
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
            for (int i = count; i < _heights.Count; i++)
            {
                RemoveMeasuredHeight(_heights[i]);
            }
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

    /// <summary>
    /// Discards all cached item heights without recycling the realized containers. Call when
    /// an external factor (e.g. column-width change in a tabular host) invalidates previously
    /// measured row heights but the item set and templates are unchanged. The next arrange
    /// re-measures visible items; off-screen items refresh on demand when scrolled in.
    /// </summary>
    public void InvalidateHeights()
    {
        int count = _heights.Count;
        if (count == 0) return;

        for (int i = 0; i < count; i++)
        {
            _heights[i] = -1;
        }
        _measuredHeightSum = 0;
        _measuredHeightCount = 0;
        InvalidatePrefix();
        RecomputeExtent();
        InvalidateMeasure();
        InvalidateArrange();
    }

    private void ResetHeights()
    {
        _heights.Clear();
        _measuredHeightSum = 0;
        _measuredHeightCount = 0;
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

        ctx.BindTemplate(element, ItemTemplate, item, index);
    }

    private void UnbindItemContainer(FrameworkElement element)
    {
        if (_contexts.TryGetValue(element, out var ctx))
        {
            ctx.UnbindTemplate(element);
        }
    }

    private FrameworkElement GetOrCreate(int index, uint itemBindingGeneration)
    {
        if (_realized.TryGetValue(index, out var existing))
        {
            // Also rebind if the item was focus-pinned and missed a prior rebind pass.
            bool pending = _pendingRebind != null && _pendingRebind.Remove(index);
            bool generationMismatch = !_itemBindingGenerations.TryGetValue(existing, out var boundGeneration)
                || boundGeneration != itemBindingGeneration;
            if (generationMismatch || pending)
            {
                BindItemContainer(existing, index);
                _itemBindingGenerations[existing] = itemBindingGeneration;
            }

            // When a focus-pinned item re-enters the visible range after being off-screen,
            // its cached VisualState may be stale (e.g. still has Focused/Active flags).
            // Force snap so the next Render applies the correct style immediately.
            if (pending)
            {
                ForceStyleSnapSubtree(existing);
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
        _itemBindingGenerations[element] = itemBindingGeneration;
        _realized[index] = element;
        TryRestoreDeferredFocus(element, index);
        return element;
    }

    private static void ForceStyleSnapSubtree(FrameworkElement container)
    {
        VisualTree.Visit(container, static element =>
        {
            if (element is Control control)
            {
                control.ForceStyleSnap();
            }
        });
    }

    private bool IsFocusedSubtree(int index)
    {
        if (!_realized.TryGetValue(index, out var element) || element is not UIElement uiElement)
        {
            return false;
        }

        if (FindVisualRoot() is not Window window)
        {
            return false;
        }

        var focused = window.FocusManager.FocusedElement;
        return focused != null && VisualTree.IsInSubtreeOf(focused, uiElement);
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
        _itemBindingGenerations.Remove(element);
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
            RemapDictionaryAfterInsert(_realized, insertIndex, count);
        }

        if (_recycledByIndex.Count > 0)
        {
            RemapDictionaryAfterInsert(_recycledByIndex, insertIndex, count);
        }
    }

    // Remaps dictionary keys in place using the reused _remapScratch pair list, avoiding a
    // replacement dictionary allocation per mutation. Keys can't be shifted while enumerating
    // the same dictionary, so entries are copied out to the scratch list first.
    private void RemapDictionaryAfterInsert(Dictionary<int, FrameworkElement> dictionary, int insertIndex, int count)
    {
        _remapScratch.Clear();
        foreach (var (index, element) in dictionary)
        {
            _remapScratch.Add((index, element));
        }

        dictionary.Clear();
        for (int i = 0; i < _remapScratch.Count; i++)
        {
            var (oldIndex, element) = _remapScratch[i];
            int newIndex = oldIndex >= insertIndex ? oldIndex + count : oldIndex;
            dictionary[newIndex] = element;
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
            RemapDictionaryAfterRemove(_realized, removeEndExclusive, removeCount);
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
                RemapDictionaryAfterRemove(_recycledByIndex, removeEndExclusive, removeCount);
            }
        }
    }

    private void RemapDictionaryAfterRemove(Dictionary<int, FrameworkElement> dictionary, int removeEndExclusive, int removeCount)
    {
        _remapScratch.Clear();
        foreach (var (index, element) in dictionary)
        {
            _remapScratch.Add((index, element));
        }

        dictionary.Clear();
        for (int i = 0; i < _remapScratch.Count; i++)
        {
            var (oldIndex, element) = _remapScratch[i];
            int newIndex = oldIndex >= removeEndExclusive ? oldIndex - removeCount : oldIndex;
            dictionary[newIndex] = element;
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

            // Keep the presenter's local offset in sync with the value we just pushed
            // to the host. Subsequent operations within the same synchronous dispatch
            // (e.g. a burst of INCC events from collection mutation) compute anchors
            // against this offset; without this update they all see the pre-correction
            // value and produce the same correction delta every iteration, so 20 inserts
            // collapse to a single 70px shift instead of accumulating 20 × 70px.
            // The next external SetOffset (driven by ScrollViewer arrange) will reconcile
            // any host-side clamping.
            _offset = correctedOffset;
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
        // Reuses a grow-only scratch buffer instead of allocating a new array every mutation.
        if (_oldPrefixScratch.Length < oldCountForAnchor + 1)
        {
            _oldPrefixScratch = new double[oldCountForAnchor + 1];
        }

        double[] oldPrefix = _oldPrefixScratch;
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
                    _insertHeightScratch.Clear();
                    for (int i = 0; i < change.Count; i++)
                    {
                        _insertHeightScratch.Add(-1d);
                    }
                    _heights.InsertRange(insertIndex, _insertHeightScratch);
                    RemapRealizedIndicesAfterInsert(insertIndex, change.Count);

                    if (insertIndex <= anchorIndex)
                    {
                        requestAnchorCorrection = true;
                        // New items haven't been measured; use the running mean over already-measured
                        // items rather than a fixed default. Refined later by measurement-based anchor correction.
                        correctionDelta = change.Count * Math.Max(1, GetRunningEstimateOrDefault());
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

                        // Drop measured-height stats for all removed items (independent of anchor position).
                        for (int i = 0; i < removeCount; i++)
                        {
                            RemoveMeasuredHeight(_heights[removeIndex + i]);
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
                        RemoveMeasuredHeight(_heights[start + i]);
                        _heights[start + i] = -1;
                    }

                    // Rebind realized containers in the replaced range once (no per-frame rebinding).
                    for (int i = 0; i < c; i++)
                    {
                        int index = start + i;
                        if (_realized.TryGetValue(index, out var element))
                        {
                            BindItemContainer(element, index);
                            _itemBindingGenerations[element] = ItemBindingGeneration;
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

        if (requestAnchorCorrection && !_isRequestingOffsetCorrection)
        {
            // Best-effort correction using estimates; refined later by measurement-based anchor correction.
            double quantizedDelta = LayoutRounding.RoundToPixel(correctionDelta, dpiScale);
            double desiredOffsetY = _offset.Y + quantizedDelta;
            double clamped = Math.Clamp(desiredOffsetY, 0, Math.Max(0, Extent.Height - _viewport.Height));
            if (Math.Abs(clamped - _offset.Y) > (1.0 / dpiScale) * 0.5)
            {
                RequestOffsetCorrection(new Point(_offset.X, clamped));
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
            build: _ => new TextBlock(),
            bind: (view, _, index, _) =>
            {
                if (view is TextBlock label)
                {
                    label.Text = index.ToString();
                }
            });
}
