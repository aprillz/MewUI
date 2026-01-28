using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public class ListBox : Control
    , IVisualTreeHost
{
    private readonly List<string> _items = new();
    private readonly TextWidthCache _textWidthCache = new(512);
    private ValueBinding<int>? _selectedIndexBinding;
    private bool _updatingFromSource;
    private readonly ScrollBar _vBar;
    private readonly ScrollController _scroll = new();
    private double _extentHeight;
    private double _viewportHeight;
    private int? _pendingScrollIntoViewIndex;

    public IList<string> Items => _items;

    public int SelectedIndex
    {
        get;
        set
        {
            int clamped = value;
            if (_items.Count == 0)
            {
                clamped = -1;
            }
            else
            {
                clamped = Math.Clamp(value, -1, _items.Count - 1);
            }

            if (field == clamped)
            {
                return;
            }

            field = clamped;
            SelectionChanged?.Invoke(field);
            ScrollIntoView(field);
            InvalidateVisual();
        }
    } = -1;

    public string? SelectedItem => SelectedIndex >= 0 && SelectedIndex < _items.Count ? _items[SelectedIndex] : null;

    public double ItemHeight
    {
        get;
        set { field = value; InvalidateMeasure(); }
    } = double.NaN;

    public Thickness ItemPadding
    {
        get;
        set { field = value; InvalidateMeasure(); InvalidateVisual(); }
    }

    public event Action<int>? SelectionChanged;

    /// <summary>
    /// Fired when the user activates an item (e.g. click or Enter), even if the selection didn't change.
    /// </summary>
    public event Action<int>? ItemActivated;

    private bool TryGetItemIndexAt(Point position, out int index)
    {
        index = -1;

        // Don't treat scrollbar interaction as item hit/activation.
        if (_vBar.IsVisible && _vBar.Bounds.Contains(position))
        {
            return false;
        }

        var bounds = GetSnappedBorderBounds(Bounds);
        var dpiScale = GetDpi() / 96.0;
        var innerBounds = bounds.Deflate(new Thickness(GetBorderVisualInset()));
        var viewportBounds = innerBounds;
        // Viewport/clip rect should not shrink due to edge rounding; snap outward.
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(viewportBounds.Deflate(Padding), dpiScale);

        double itemHeight = ResolveItemHeight();
        if (itemHeight <= 0)
        {
            return false;
        }

        index = (int)((position.Y - contentBounds.Y + _scroll.GetOffsetDip(1)) / itemHeight);
        return index >= 0 && index < _items.Count;
    }

    public override bool Focusable => true;

    protected override Color DefaultBackground => Theme.Palette.ControlBackground;

    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    public ListBox()
    {
        BorderThickness = 1;
        Padding = new Thickness(1);
        ItemPadding = Theme.Metrics.ItemPadding;

        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _vBar.Parent = this;
        _vBar.ValueChanged += v =>
        {
            _scroll.DpiScale = GetDpi() / 96.0;
            _scroll.SetMetricsDip(1, _extentHeight, GetViewportHeightDip());
            _scroll.SetOffsetDip(1, v);
            InvalidateVisual();
        };
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        if (ItemPadding == oldTheme.Metrics.ItemPadding)
        {
            ItemPadding = newTheme.Metrics.ItemPadding;
        }
    }

    void IVisualTreeHost.VisitChildren(Action<Element> visitor)
    {
        visitor(_vBar);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var dpi = GetDpi();
        double widthLimit = double.IsPositiveInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width - Padding.HorizontalThickness - borderInset * 2);

        double maxWidth;

        // Fast path: when stretching horizontally, the parent is going to size us by slot width anyway.
        // Avoid scanning huge item lists just to compute a content-based desired width.
        if (HorizontalAlignment == HorizontalAlignment.Stretch && !double.IsPositiveInfinity(widthLimit))
        {
            maxWidth = widthLimit;
        }
        else
        {
            using var measure = BeginTextMeasurement();

            maxWidth = 0;

            // If the list is huge, prefer a cheap width estimate.
            // This keeps layout responsive; users can still explicitly set Width for deterministic sizing.
            if (_items.Count > 4096)
            {
                double itemHeightEstimate = ResolveItemHeight();
                double viewportEstimate = double.IsPositiveInfinity(availableSize.Height)
                    ? Math.Min(_items.Count * itemHeightEstimate, itemHeightEstimate * 12)
                    : Math.Max(0, availableSize.Height - Padding.VerticalThickness - borderInset * 2);

                int visibleEstimate = itemHeightEstimate <= 0 ? _items.Count : (int)Math.Ceiling(viewportEstimate / itemHeightEstimate) + 1;
                int sampleCount = Math.Clamp(visibleEstimate, 32, 256);
                sampleCount = Math.Min(sampleCount, _items.Count);
                _textWidthCache.SetCapacity(Math.Clamp(visibleEstimate * 4, 256, 4096));
                double itemPadW = ItemPadding.HorizontalThickness;

                for (int i = 0; i < sampleCount; i++)
                {
                    var item = _items[i];
                    if (string.IsNullOrEmpty(item))
                    {
                        continue;
                    }

                    maxWidth = Math.Max(maxWidth, _textWidthCache.GetOrMeasure(measure.Context, measure.Font, dpi, item) + itemPadW);
                    if (maxWidth >= widthLimit)
                    {
                        maxWidth = widthLimit;
                        break;
                    }
                }

                // Ensure current selection isn't clipped when it lies outside the sample range.
                if (SelectedIndex >= sampleCount && SelectedIndex < _items.Count && maxWidth < widthLimit)
                {
                    var item = _items[SelectedIndex];
                    if (!string.IsNullOrEmpty(item))
                    {
                        maxWidth = Math.Max(maxWidth, _textWidthCache.GetOrMeasure(measure.Context, measure.Font, dpi, item) + itemPadW);
                    }
                }
            }
            else
            {
                _textWidthCache.SetCapacity(Math.Clamp(_items.Count, 64, 4096));
                double itemPadW = ItemPadding.HorizontalThickness;
                foreach (var item in _items)
                {
                    if (string.IsNullOrEmpty(item))
                    {
                        continue;
                    }

                    maxWidth = Math.Max(maxWidth, _textWidthCache.GetOrMeasure(measure.Context, measure.Font, dpi, item) + itemPadW);
                    if (maxWidth >= widthLimit)
                    {
                        maxWidth = widthLimit;
                        break;
                    }
                }
            }
        }

        double itemHeight = ResolveItemHeight();
        double height = _items.Count * itemHeight;

        // Cache extent/viewport for scroll bar (viewport is approximated here; final value computed in Arrange).
        _extentHeight = height;
        var dpiScale = GetDpi() / 96.0;
        _viewportHeight = double.IsPositiveInfinity(availableSize.Height)
                ? height
                : LayoutRounding.RoundToPixel(
                    Math.Max(0, availableSize.Height - Padding.VerticalThickness - borderInset * 2),
                    dpiScale);

        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(1, _extentHeight, _viewportHeight);

        // Vertical scrollbar is overlay in ListBox (it does not consume horizontal space).
        bool needV = _extentHeight > _viewportHeight + 0.5;

        double desiredHeight = double.IsPositiveInfinity(availableSize.Height)
            ? height
            : Math.Min(height, _viewportHeight);

        return new Size(maxWidth, desiredHeight)
            .Inflate(Padding)
            .Inflate(new Thickness(borderInset));
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        var snapped = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        var innerBounds = snapped.Deflate(new Thickness(borderInset));

        var dpiScale = GetDpi() / 96.0;
        _viewportHeight = LayoutRounding.RoundToPixel(Math.Max(0, innerBounds.Height - Padding.VerticalThickness), dpiScale);
        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(1, _extentHeight, _viewportHeight);
        _scroll.SetOffsetPx(1, _scroll.GetOffsetPx(1));

        bool needV = _extentHeight > _viewportHeight + 0.5;
        _vBar.IsVisible = needV;

        if (_vBar.IsVisible)
        {
            double t = Theme.Metrics.ScrollBarHitThickness;
            const double inset = 0;

            _vBar.Minimum = 0;
            _vBar.Maximum = Math.Max(0, _extentHeight - _viewportHeight);
            _vBar.ViewportSize = _viewportHeight;
            _vBar.SmallChange = Theme.Metrics.ScrollBarSmallChange;
            _vBar.LargeChange = Theme.Metrics.ScrollBarLargeChange;
            _vBar.Value = _scroll.GetOffsetDip(1);

            _vBar.Arrange(new Rect(
                innerBounds.Right - t - inset,
                innerBounds.Y + inset,
                t,
                Math.Max(0, innerBounds.Height - inset * 2)));
        }

        if (_pendingScrollIntoViewIndex is int pending)
        {
            _pendingScrollIntoViewIndex = null;
            ScrollIntoView(pending);
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        var dpiScale = GetDpi() / 96.0;
        double radius = Theme.Metrics.ControlCornerRadius;
        var borderInset = GetBorderVisualInset();
        double itemRadius = Math.Max(0, radius - borderInset);

        var bg = IsEnabled ? Background : Theme.Palette.DisabledControlBackground;
        var borderColor = BorderBrush;
        if (IsEnabled)
        {
            if (IsFocused)
            {
                borderColor = Theme.Palette.Accent;
            }
            else if (IsMouseOver)
            {
                borderColor = BorderBrush.Lerp(Theme.Palette.Accent, 0.6);
            }
        }
        DrawBackgroundAndBorder(context, bounds, bg, borderColor, radius);

        if (_items.Count == 0)
        {
            return;
        }

        var innerBounds = bounds.Deflate(new Thickness(borderInset));
        var viewportBounds = innerBounds;

        // Viewport/clip rect should not shrink due to edge rounding; snap outward.
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(viewportBounds.Deflate(Padding), dpiScale);

        context.Save();
        context.SetClip(LayoutRounding.MakeClipRect(contentBounds, dpiScale));

        var font = GetFont();
        double itemHeight = ResolveItemHeight();
        double verticalOffset = _scroll.GetOffsetDip(1);

        // Even when "virtualization" is disabled, only paint the visible range.
        // (Clipping makes off-screen work pure overhead for large item counts.)
        int first = itemHeight <= 0 ? 0 : Math.Max(0, (int)Math.Floor(verticalOffset / itemHeight));
        double offsetInItem = itemHeight <= 0 ? 0 : verticalOffset - first * itemHeight;
        double yStart = contentBounds.Y - offsetInItem;
        int visibleCount = itemHeight <= 0 ? _items.Count : (int)Math.Ceiling((contentBounds.Height + offsetInItem) / itemHeight) + 1;
        int lastExclusive = Math.Min(_items.Count, first + Math.Max(0, visibleCount));

        for (int i = first; i < lastExclusive; i++)
        {
            double y = yStart + (i - first) * itemHeight;
            var itemRect = new Rect(contentBounds.X, y, contentBounds.Width, itemHeight);

            bool selected = i == SelectedIndex;
            if (selected)
            {
                var selectionBg = Theme.Palette.SelectionBackground;
                if (itemRadius > 0)
                {
                    context.FillRoundedRectangle(itemRect, itemRadius, itemRadius, selectionBg);
                }
                else
                {
                    context.FillRectangle(itemRect, selectionBg);
                }
            }

            var textColor = selected ? Theme.Palette.SelectionText : (IsEnabled ? Foreground : Theme.Palette.DisabledText);
            var textBounds = itemRect.Deflate(ItemPadding);
            context.DrawText(_items[i] ?? string.Empty, textBounds, font, textColor, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }

        context.Restore();

        if (_vBar.IsVisible)
        {
            _vBar.Render(context);
        }
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEnabled)
        {
            return null;
        }

        if (_vBar.IsVisible && _vBar.Bounds.Contains(point))
        {
            return _vBar;
        }

        return base.OnHitTest(point);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        Focus();

        if (TryGetItemIndexAt(e.Position, out int index))
        {
            SelectedIndex = index;
            e.Handled = true;
        }
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (!IsEnabled || e.Handled || e.Button != MouseButton.Left)
        {
            return;
        }

        if (!TryGetItemIndexAt(e.Position, out int index))
        {
            return;
        }

        // Ensure selection matches the activated item (can differ if mouse-down started elsewhere).
        SelectedIndex = index;
        ItemActivated?.Invoke(index);
        e.Handled = true;
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
        _scroll.SetMetricsDip(1, _extentHeight, GetViewportHeightDip());
        _scroll.ScrollByNotches(1, -notches, Theme.Metrics.ScrollWheelStep);
        _vBar.Value = _scroll.GetOffsetDip(1);
        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!IsEnabled)
        {
            return;
        }

        if (e.Key == Key.Up)
        {
            if (_items.Count > 0)
            {
                SelectedIndex = Math.Max(0, SelectedIndex <= 0 ? 0 : SelectedIndex - 1);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            if (_items.Count > 0)
            {
                SelectedIndex = Math.Min(_items.Count - 1, SelectedIndex < 0 ? 0 : SelectedIndex + 1);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            if (SelectedIndex >= 0 && SelectedIndex < _items.Count)
            {
                ItemActivated?.Invoke(SelectedIndex);
                e.Handled = true;
            }
        }
    }

    public void ScrollIntoViewSelected() => ScrollIntoView(SelectedIndex);

    public void ScrollIntoView(int index)
    {
        if (index < 0 || index >= _items.Count)
        {
            return;
        }

        double viewport = GetViewportHeightDip();
        if (viewport <= 0 || double.IsNaN(viewport) || double.IsInfinity(viewport))
        {
            _pendingScrollIntoViewIndex = index;
            return;
        }

        double itemHeight = ResolveItemHeight();
        if (itemHeight <= 0)
        {
            return;
        }

        double itemTop = index * itemHeight;
        double itemBottom = itemTop + itemHeight;

        double oldOffset = _scroll.GetOffsetDip(1);
        double newOffset = oldOffset;
        if (itemTop < newOffset)
        {
            newOffset = itemTop;
        }
        else if (itemBottom > newOffset + viewport)
        {
            newOffset = itemBottom - viewport;
        }

        _scroll.DpiScale = GetDpi() / 96.0;
        _scroll.SetMetricsDip(1, _extentHeight, viewport);
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

        InvalidateVisual();
    }

    public void AddItem(string item)
    {
        _items.Add(item ?? string.Empty);
        InvalidateMeasure();
        InvalidateVisual();
    }

    public void ClearItems()
    {
        _items.Clear();
        SelectedIndex = -1;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private double ResolveItemHeight()
    {
        if (!double.IsNaN(ItemHeight) && ItemHeight > 0)
        {
            return ItemHeight;
        }

        return Math.Max(18, Theme.Metrics.BaseControlHeight - 2);
    }

    private double GetViewportHeightDip()
    {
        if (Bounds.Width > 0 && Bounds.Height > 0)
        {
            var snapped = GetSnappedBorderBounds(Bounds);
            var borderInset = GetBorderVisualInset();
            var innerBounds = snapped.Deflate(new Thickness(borderInset));
            var dpiScale = GetDpi() / 96.0;
            return LayoutRounding.RoundToPixel(Math.Max(0, innerBounds.Height - Padding.VerticalThickness), dpiScale);
        }

        return _viewportHeight;
    }

    public void SetSelectedIndexBinding(
        Func<int> get,
        Action<int> set,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        _selectedIndexBinding?.Dispose();
        _selectedIndexBinding = new ValueBinding<int>(
            get,
            set,
            subscribe,
            unsubscribe,
            () =>
            {
                _updatingFromSource = true;
                try { SelectedIndex = get(); }
                finally { _updatingFromSource = false; }
            });

        // Ensure the binding forwarder is registered once (no duplicates), without tracking extra state.
        SelectionChanged -= ForwardSelectionChangedToBinding;
        SelectionChanged += ForwardSelectionChangedToBinding;

        _updatingFromSource = true;
        try { SelectedIndex = get(); }
        finally { _updatingFromSource = false; }
    }

    private void ForwardSelectionChangedToBinding(int index)
    {
        if (_updatingFromSource)
        {
            return;
        }

        _selectedIndexBinding?.Set(index);
    }

    protected override void OnDispose()
    {
        SelectionChanged -= ForwardSelectionChangedToBinding;
        _selectedIndexBinding?.Dispose();
        _selectedIndexBinding = null;
    }
}
