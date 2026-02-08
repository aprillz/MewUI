using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A scrollable list control with item selection.
/// </summary>
public partial class ListBox : Control
    , IVisualTreeHost
{
    private readonly TextWidthCache _textWidthCache = new(512);
    private readonly TemplatedItemsHost _itemsHost;
    private int _hoverIndex = -1;
    private bool _hasLastMousePosition;
    private Point _lastMousePosition;
    private bool _rebindVisibleOnNextRender = true;
    private bool _updatingFromSource;
    private bool _suppressItemsSelectionChanged;
    private IItemsView _itemsSource = ItemsView.Empty;
    private readonly ScrollBar _vBar;
    private readonly ScrollController _scroll = new();
    private double _extentHeight;
    private double _viewportHeight;
    private int? _pendingScrollIntoViewIndex;

    /// <summary>
    /// Gets or sets the items data source.
    /// </summary>
    public IItemsView ItemsSource
    {
        get => _itemsSource;
        set
        {
            value ??= ItemsView.Empty;
            if (ReferenceEquals(_itemsSource, value))
            {
                return;
            }

            int oldIndex = SelectedIndex;

            _itemsSource.Changed -= OnItemsChanged;
            _itemsSource.SelectionChanged -= OnItemsSelectionChanged;

            _itemsSource = value;
            _itemsSource.SelectionChanged += OnItemsSelectionChanged;
            _itemsSource.Changed += OnItemsChanged;

            _suppressItemsSelectionChanged = true;
            try
            {
                _itemsSource.SelectedIndex = oldIndex;
            }
            finally
            {
                _suppressItemsSelectionChanged = false;
            }

            int newIndex = _itemsSource.SelectedIndex;
            if (newIndex != oldIndex)
            {
                OnItemsSelectionChanged(newIndex);
            }

            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the selected item index.
    /// </summary>
    public int SelectedIndex
    {
        get => ItemsSource.SelectedIndex;
        set => ItemsSource.SelectedIndex = value;
    }

    /// <summary>
    /// Gets the currently selected item object.
    /// </summary>
    public object? SelectedItem => ItemsSource.SelectedItem;

    /// <summary>
    /// Gets the currently selected item text.
    /// </summary>
    public string? SelectedText => SelectedIndex >= 0 && SelectedIndex < ItemsSource.Count ? ItemsSource.GetText(SelectedIndex) : null;

    /// <summary>
    /// Gets or sets the height of each list item.
    /// </summary>
    public double ItemHeight
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                InvalidateMeasure();
            }
        }
    } = double.NaN;

    /// <summary>
    /// Gets or sets the padding around each item's text.
    /// </summary>
    public Thickness ItemPadding
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                _rebindVisibleOnNextRender = true;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Gets or sets the item template. If not set explicitly, the default template is used.
    /// </summary>
    public IDataTemplate ItemTemplate
    {
        get => _itemsHost.ItemTemplate;
        set { _itemsHost.ItemTemplate = value; _rebindVisibleOnNextRender = true; }
    }

    /// <summary>
    /// Occurs when the selected item changes.
    /// </summary>
    public event Action<object?>? SelectionChanged;

    /// <summary>
    /// Occurs when an item is activated by click or Enter key.
    /// </summary>
    public event Action<int>? ItemActivated;

    /// <summary>
    /// Attempts to find the item index at the specified position.
    /// </summary>
    /// <param name="position">The position to test.</param>
    /// <param name="index">The item index if found.</param>
    /// <returns>True if an item was found at the position.</returns>
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

        return ItemsViewportMath.TryGetItemIndexAtY(
            position.Y,
            contentBounds.Y,
            _scroll.GetOffsetDip(1),
            itemHeight,
            ItemsSource.Count,
            out index);
    }

    /// <summary>
    /// Gets whether the listbox can receive keyboard focus.
    /// </summary>
    public override bool Focusable => true;

    /// <summary>
    /// Gets the default background color.
    /// </summary>
    protected override Color DefaultBackground => Theme.Palette.ControlBackground;

    /// <summary>
    /// Gets the default border brush color.
    /// </summary>
    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    /// <summary>
    /// Initializes a new instance of the ListBox class.
    /// </summary>
    public ListBox()
    {
        BorderThickness = 1;
        Padding = new Thickness(1);
        ItemPadding = Theme.Metrics.ItemPadding;

        var template = CreateDefaultItemTemplate();
        _itemsHost = new TemplatedItemsHost(
            owner: this,
            getItem: index => index >= 0 && index < ItemsSource.Count ? ItemsSource.GetItem(index) : null,
            invalidateMeasureAndVisual: () => { InvalidateMeasure(); InvalidateVisual(); },
            template: template);

        _itemsHost.Options = new TemplatedItemsHost.ItemsRangeOptions
        {
            BeforeItemRender = OnBeforeItemRender
        };

        _itemsSource.SelectionChanged += OnItemsSelectionChanged;
        _itemsSource.Changed += OnItemsChanged;

        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _vBar.Parent = this;
        _vBar.ValueChanged += v =>
        {
            _scroll.DpiScale = GetDpi() / 96.0;
            _scroll.SetMetricsDip(1, _extentHeight, GetViewportHeightDip());
            _scroll.SetOffsetDip(1, v);
            _hoverIndex = -1;
            _hasLastMousePosition = false;
            InvalidateVisual();
        };
    }

    private void OnBeforeItemRender(IGraphicsContext context, int i, Rect itemRect)
    {
        double itemRadius = _itemsHost.Layout.ItemRadius;

        if (i == SelectedIndex)
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
        else if (i == _hoverIndex)
        {
            var hoverBg = Theme.Palette.ControlBackground.Lerp(Theme.Palette.Accent, 0.15);
            if (itemRadius > 0)
            {
                context.FillRoundedRectangle(itemRect, itemRadius, itemRadius, hoverBg);
            }
            else
            {
                context.FillRectangle(itemRect, hoverBg);
            }
        }
    }

    private IDataTemplate CreateDefaultItemTemplate()
        => new DelegateTemplate<object?>(
            build: _ =>
                new Label
                {
                    IsHitTestVisible = false,
                    VerticalTextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.NoWrap,
                },
            bind: (view, _, index, _) =>
            {
                var tb = (Label)view;

                var text = ItemsSource.GetText(index);
                if (tb.Text != text)
                {
                    tb.Text = text;
                }

                if (!tb.Padding.Equals(ItemPadding))
                {
                    tb.Padding = ItemPadding;
                }

                if (tb.FontFamily != FontFamily)
                {
                    tb.FontFamily = FontFamily;
                }

                if (!tb.FontSize.Equals(FontSize))
                {
                    tb.FontSize = FontSize;
                }

                if (tb.FontWeight != FontWeight)
                {
                    tb.FontWeight = FontWeight;
                }

                if (tb.IsEnabled != IsEnabled)
                {
                    tb.IsEnabled = IsEnabled;
                }

                var fg = index == SelectedIndex
                    ? Theme.Palette.SelectionText
                    : (IsEnabled ? Foreground : Theme.Palette.DisabledText);

                if (tb.Foreground != fg)
                {
                    tb.Foreground = fg;
                }
            });

    private void OnItemsChanged(ItemsChange change)
    {
        _itemsHost.RecycleAll();
        _hoverIndex = -1;
        _rebindVisibleOnNextRender = true;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnItemsSelectionChanged(int index)
    {
        if (_suppressItemsSelectionChanged)
        {
            return;
        }

        _rebindVisibleOnNextRender = true;
        if (!_updatingFromSource)
        {
            if (TryGetBinding(SelectedIndexBindingSlot, out ValueBinding<int> binding))
            {
                binding.Set(index);
            }
        }

        SelectionChanged?.Invoke(SelectedItem);
        ScrollIntoView(index);
        InvalidateVisual();
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
        _itemsHost.VisitRealized(visitor);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var dpi = GetDpi();
        double widthLimit = double.IsPositiveInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width - Padding.HorizontalThickness - borderInset * 2);

        double maxWidth;
        int count = ItemsSource.Count;

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
            if (count > 4096)
            {
                double itemHeightEstimate = ResolveItemHeight();
                double viewportEstimate = double.IsPositiveInfinity(availableSize.Height)
                    ? Math.Min(count * itemHeightEstimate, itemHeightEstimate * 12)
                    : Math.Max(0, availableSize.Height - Padding.VerticalThickness - borderInset * 2);

                int visibleEstimate = itemHeightEstimate <= 0 ? count : (int)Math.Ceiling(viewportEstimate / itemHeightEstimate) + 1;
                int sampleCount = Math.Clamp(visibleEstimate, 32, 256);
                sampleCount = Math.Min(sampleCount, count);
                _textWidthCache.SetCapacity(Math.Clamp(visibleEstimate * 4, 256, 4096));
                double itemPadW = ItemPadding.HorizontalThickness;

                for (int i = 0; i < sampleCount; i++)
                {
                    var item = ItemsSource.GetText(i);
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
                if (SelectedIndex >= sampleCount && SelectedIndex < count && maxWidth < widthLimit)
                {
                    var item = ItemsSource.GetText(SelectedIndex);
                    if (!string.IsNullOrEmpty(item))
                    {
                        maxWidth = Math.Max(maxWidth, _textWidthCache.GetOrMeasure(measure.Context, measure.Font, dpi, item) + itemPadW);
                    }
                }
            }
            else
            {
                _textWidthCache.SetCapacity(Math.Clamp(count, 64, 4096));
                double itemPadW = ItemPadding.HorizontalThickness;
                for (int i = 0; i < count; i++)
                {
                    var item = ItemsSource.GetText(i);
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
        double height = count * itemHeight;

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

        if (ItemsSource.Count == 0)
        {
            return;
        }

        var innerBounds = bounds.Deflate(new Thickness(borderInset));
        var viewportBounds = innerBounds;

        // Viewport/clip rect should not shrink due to edge rounding; snap outward.
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(viewportBounds.Deflate(Padding), dpiScale);

        context.Save();
        context.SetClip(LayoutRounding.MakeClipRect(contentBounds, dpiScale));

        double itemHeight = ResolveItemHeight();
        double verticalOffset = _scroll.GetOffsetDip(1);

        ItemsViewportMath.ComputeVisibleRange(
            ItemsSource.Count,
            itemHeight,
            contentBounds.Height,
            contentBounds.Y,
            verticalOffset,
            out int first,
            out int lastExclusive,
            out double yStart,
            out _);

        bool rebind = _rebindVisibleOnNextRender;
        _rebindVisibleOnNextRender = false;

        _itemsHost.Layout = new TemplatedItemsHost.ItemsRangeLayout
        {
            ContentBounds = contentBounds,
            First = first,
            LastExclusive = lastExclusive,
            ItemHeight = itemHeight,
            YStart = yStart,
            ItemRadius = itemRadius,
            RebindExisting = rebind,
        };

        _itemsHost.Render(context);

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

        if (_hasLastMousePosition && TryGetItemIndexAt(_lastMousePosition, out int hover))
        {
            _hoverIndex = hover;
        }
        else
        {
            _hoverIndex = -1;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsEnabled)
        {
            return;
        }

        _hasLastMousePosition = true;
        _lastMousePosition = e.Position;

        int newHover = -1;
        if (TryGetItemIndexAt(e.Position, out int index))
        {
            newHover = index;
        }

        if (_hoverIndex != newHover)
        {
            _hoverIndex = newHover;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();

        _hasLastMousePosition = false;

        if (_hoverIndex != -1)
        {
            _hoverIndex = -1;
            InvalidateVisual();
        }
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
            int count = ItemsSource.Count;
            if (count > 0)
            {
                SelectedIndex = Math.Max(0, SelectedIndex <= 0 ? 0 : SelectedIndex - 1);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Down)
        {
            int count = ItemsSource.Count;
            if (count > 0)
            {
                SelectedIndex = Math.Min(count - 1, SelectedIndex < 0 ? 0 : SelectedIndex + 1);
            }

            e.Handled = true;
        }
        else if (e.Key == Key.Enter)
        {
            int count = ItemsSource.Count;
            if (SelectedIndex >= 0 && SelectedIndex < count)
            {
                ItemActivated?.Invoke(SelectedIndex);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Scrolls the selected item into view.
    /// </summary>
    public void ScrollIntoViewSelected() => ScrollIntoView(SelectedIndex);

    /// <summary>
    /// Scrolls the specified item index into view.
    /// </summary>
    /// <param name="index">The item index to scroll into view.</param>
    public void ScrollIntoView(int index)
    {
        int count = ItemsSource.Count;
        if (index < 0 || index >= count)
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

    /// <summary>
    /// Resolves the effective item height.
    /// </summary>
    /// <returns>The item height in DIPs.</returns>
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

    /// <summary>
    /// Sets a two-way binding for the SelectedIndex property.
    /// </summary>
    /// <param name="get">Function to get the current value.</param>
    /// <param name="set">Action to set the value.</param>
    /// <param name="subscribe">Optional action to subscribe to change notifications.</param>
    /// <param name="unsubscribe">Optional action to unsubscribe from change notifications.</param>
    public void SetSelectedIndexBinding(
        Func<int> get,
        Action<int> set,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        SetSelectedIndexBindingCore(get, set, subscribe, unsubscribe);
    }

    protected override void OnDispose()
    {
        _itemsSource.Changed -= OnItemsChanged;
        _itemsSource.SelectionChanged -= OnItemsSelectionChanged;
    }
}
