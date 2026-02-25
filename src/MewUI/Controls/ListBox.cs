using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A scrollable list control with item selection.
/// </summary>
public partial class ListBox : VirtualizedItemsBase, IVirtualizedTabNavigationHost
{
    private readonly TextWidthCache _textWidthCache = new(512);
    private IItemsPresenter _presenter;
    private IDataTemplate _itemTemplate;
    private ItemsPresenterMode _presenterMode = ItemsPresenterMode.Fixed;

    private int _hoverIndex = -1;
    private bool _hasLastMousePosition;
    private Point _lastMousePosition;
    private bool _updatingFromSource;
    private bool _suppressItemsSelectionChanged;
    private ISelectableItemsView _itemsSource = ItemsView.EmptySelectable;

    /// <summary>
    /// Gets or sets the items data source.
    /// </summary>
    public ISelectableItemsView ItemsSource
    {
        get => _itemsSource;
        set
        {
            ApplyItemsSource(value, preserveListBoxSelection: true);
        }
    }

    internal void ApplyItemsSource(ISelectableItemsView? value, bool preserveListBoxSelection)
    {
        value ??= ItemsView.EmptySelectable;
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

        _presenter.ItemsSource = _itemsSource;

        _hoverIndex = -1;
        _rebindVisibleOnNextRender = true;

        if (preserveListBoxSelection)
        {
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
        }
        else
        {
            int newIndex = _itemsSource.SelectedIndex;
            if (newIndex >= 0)
            {
                ScrollIntoView(newIndex);
            }
        }

        InvalidateMeasure();
        InvalidateVisual();
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
                InvalidateVisual();
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
                _presenter?.ItemPadding = value;
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
        get => _itemTemplate;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _itemTemplate = value;
            _presenter.ItemTemplate = value;
            _rebindVisibleOnNextRender = true;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Selects the virtualization strategy for this control.
    /// </summary>
    public ItemsPresenterMode PresenterMode
    {
        get => _presenterMode;
        set
        {
            if (Set(ref _presenterMode, value))
            {
                ReplacePresenter(value, preserveScrollOffsets: true);
                _hoverIndex = -1;
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
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
    /// Attempts to find the item index at the specified position in this control's coordinates.
    /// </summary>
    public bool TryGetItemIndexAt(Point position, out int index)
        => TryGetItemIndexAtCore(position, out index);

    /// <summary>
    /// Attempts to find the item index for a mouse event routed by the window input router.
    /// </summary>
    public bool TryGetItemIndexAt(MouseEventArgs e, out int index)
    {
        ArgumentNullException.ThrowIfNull(e);
        return TryGetItemIndexAtCore(e.GetPosition(this), out index);
    }

    /// <summary>
    /// Initializes a new instance of the ListBox class.
    /// </summary>
    public ListBox()
    {
        _scrollViewer.HorizontalScroll = ScrollMode.Disabled;

        ItemPadding = Theme.Metrics.ItemPadding;

        _itemsSource.SelectionChanged += OnItemsSelectionChanged;
        _itemsSource.Changed += OnItemsChanged;

        _itemTemplate = CreateDefaultItemTemplate();
        _presenter = CreatePresenter(PresenterMode);
        _tabFocusHelper = new PendingTabFocusHelper(
            getWindow: () => FindVisualRoot() as Window,
            getContainer: idx =>
            {
                FrameworkElement? container = null;
                _presenter.VisitRealized((i, el) => { if (i == idx) container = el; });
                return container;
            });

        _scrollViewer.Padding = Padding;
        _scrollViewer.Content = (UIElement)_presenter;
        _scrollViewer.ScrollChanged += OnScrollViewerChanged;
    }

    private IItemsPresenter CreatePresenter(ItemsPresenterMode mode)
    {
        IItemsPresenter presenter = mode == ItemsPresenterMode.Variable
            ? new VariableHeightItemsPresenter()
            : new FixedHeightItemsPresenter();

        presenter.ItemsSource = _itemsSource;
        presenter.ItemTemplate = _itemTemplate;
        presenter.BeforeItemRender = OnBeforeItemRender;
        presenter.ItemPadding = ItemPadding;
        presenter.ItemHeightHint = ResolveItemHeight();
        presenter.ExtentWidth = double.NaN;
        presenter.ItemRadius = 0;
        presenter.RebindExisting = true;
        presenter.OffsetCorrectionRequested += OnPresenterOffsetCorrectionRequested;
        return presenter;
    }

    private void ReplacePresenter(ItemsPresenterMode mode, bool preserveScrollOffsets)
    {
        double oldX = _scrollViewer.HorizontalOffset;
        double oldY = _scrollViewer.VerticalOffset;

        _presenter.OffsetCorrectionRequested -= OnPresenterOffsetCorrectionRequested;
        if (_presenter is IDisposable d)
        {
            d.Dispose();
        }

        _presenter = CreatePresenter(mode);
        _scrollViewer.Content = (UIElement)_presenter;
        _rebindVisibleOnNextRender = true;

        if (preserveScrollOffsets)
        {
            _scrollViewer.SetScrollOffsets(oldX, oldY);
        }
    }

    private void OnPresenterOffsetCorrectionRequested(Point offset)
    {
        _scrollViewer.SetScrollOffsets(_scrollViewer.HorizontalOffset, offset.Y);
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        if (ItemPadding == oldTheme.Metrics.ItemPadding)
        {
            ItemPadding = newTheme.Metrics.ItemPadding;
        } 

        _rebindVisibleOnNextRender = true;
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

        if (HorizontalAlignment == HorizontalAlignment.Stretch && !double.IsPositiveInfinity(widthLimit))
        {
            maxWidth = widthLimit;
        }
        else
        {
            using var measure = BeginTextMeasurement();

            maxWidth = 0;
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

        _presenter.ItemHeightHint = itemHeight;
        _presenter.ExtentWidth = maxWidth;
        _scrollViewer.Padding = Padding;

        _scrollViewer.Measure(new Size(
            double.IsPositiveInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - borderInset * 2),
            double.IsPositiveInfinity(availableSize.Height) ? double.PositiveInfinity : Math.Max(0, availableSize.Height - borderInset * 2)));

        // Desired height is governed by availableSize (viewport). Extent is used by ScrollViewer.
        if (double.IsPositiveInfinity(availableSize.Height))
        {
            height = Math.Min(height, itemHeight * 12);
        }

        return new Size(
            Math.Max(0, maxWidth + Padding.HorizontalThickness + borderInset * 2),
            Math.Max(0, height + Padding.VerticalThickness + borderInset * 2));
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        var snapped = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        var innerBounds = snapped.Deflate(new Thickness(borderInset));
        _scrollViewer.Arrange(innerBounds);

        if (!_scrollIntoViewRequest.IsNone)
        {
            var request = _scrollIntoViewRequest;
            _scrollIntoViewRequest.Clear();

            if (request.Kind == ScrollIntoViewRequestKind.Index)
            {
                ScrollIntoView(request.Index);
            }
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        double radius = Theme.Metrics.ControlCornerRadius;

        var state = GetVisualState();
        var borderColor = PickAccentBorder(Theme, BorderBrush, state, 0.6);

        DrawBackgroundAndBorder(
            context,
            bounds,
            PickControlBackground(state),
            borderColor,
            radius);

        var borderInset = GetBorderVisualInset();
        var clipR = LayoutRounding.RoundToPixel(Math.Max(0, radius - BorderThickness), GetDpi() / 96.0);
        _scrollViewer.ViewportCornerRadius = clipR;
        _presenter.ItemRadius = clipR;

        _presenter.RebindExisting = _rebindVisibleOnNextRender;
        _rebindVisibleOnNextRender = false;

        _scrollViewer.Render(context);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        if (!IsEffectivelyEnabled)
        {
            return;
        }

        _hasLastMousePosition = true;
        _lastMousePosition = e.GetPosition(this);

        if (!TryGetItemIndexAtCore(_lastMousePosition, out int index))
        {
            if (_hoverIndex != -1)
            {
                _hoverIndex = -1;
                InvalidateVisual();
            }
            return;
        }

        if (_hoverIndex != index)
        {
            _hoverIndex = index;
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

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        if (TryGetItemIndexAt(e, out int index))
        {
            SelectedIndex = index;
            InvalidateVisual();
            e.Handled = true;
        }
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        if (TryGetItemIndexAt(e, out int index))
        {
            ItemActivated?.Invoke(index);
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (ItemsSource.Count == 0)
        {
            return;
        }

        int index = SelectedIndex;
        switch (e.Key)
        {
            case Key.Up:
                index = Math.Max(0, index <= 0 ? 0 : index - 1);
                e.Handled = true;
                break;
            case Key.Down:
                index = Math.Min(ItemsSource.Count - 1, index < 0 ? 0 : index + 1);
                e.Handled = true;
                break;
            case Key.Home:
                index = 0;
                e.Handled = true;
                break;
            case Key.End:
                index = ItemsSource.Count - 1;
                e.Handled = true;
                break;
            case Key.Enter:
                if (index >= 0)
                {
                    ItemActivated?.Invoke(index);
                    e.Handled = true;
                }
                break;
        }

        if (e.Handled)
        {
            SelectedIndex = index;
            ScrollIntoView(index);
            InvalidateVisual();
        }
    }

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
            _scrollIntoViewRequest = ScrollIntoViewRequest.IndexRequest(index);
            return;
        }

        _presenter.RequestScrollIntoView(index);
        InvalidateVisual();
    }

    private bool TryGetItemIndexAtCore(Point position, out int index)
    {
        index = -1;

        var hit = _scrollViewer.HitTest(position);
        if (hit is ScrollBar)
        {
            return false;
        }

        int count = ItemsSource.Count;
        if (count <= 0)
        {
            return false;
        }

        if (_presenter is not Element presenterElement)
        {
            return false;
        }

        var dpiScale = GetDpi() / 96.0;
        var local = TranslatePoint(position, presenterElement);
        var presenterRect = new Rect(0, 0, presenterElement.RenderSize.Width, presenterElement.RenderSize.Height);
        if (!presenterRect.Contains(local))
        {
            return false;
        }

        double alignedLocalY = LayoutRounding.RoundToPixel(local.Y, dpiScale);
        double alignedOffsetY = LayoutRounding.RoundToPixel(_scrollViewer.VerticalOffset, dpiScale);
        double yContent = alignedLocalY + alignedOffsetY;
        return _presenter.TryGetItemIndexAtY(yContent, out index);
    }

    private void OnBeforeItemRender(IGraphicsContext context, int i, Rect itemRect)
    {
        double itemRadius = _presenter.ItemRadius;

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

                var enabled = IsEffectivelyEnabled;
                if (tb.IsEnabled != enabled)
                {
                    tb.IsEnabled = enabled;
                }

                var fg = index == SelectedIndex
                    ? Theme.Palette.SelectionText
                    : (enabled ? Foreground : Theme.Palette.DisabledText);

                if (tb.Foreground != fg)
                {
                    tb.Foreground = fg;
                }
            });

    private void OnItemsChanged(ItemsChange change)
    {
        // VariableHeightItemsPresenter handles Add/Remove/Replace internally:
        // it remaps realized indices and preserves the scroll anchor.
        // Force a full recycle only for Reset/Move where the presenter itself resets.
        if (change.Kind is ItemsChangeKind.Reset or ItemsChangeKind.Move)
        {
            _presenter.RecycleAll();
        }
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

    private void OnScrollViewerChanged()
    {
        if (_hasLastMousePosition && TryGetItemIndexAtCore(_lastMousePosition, out int hover))
        {
            _hoverIndex = hover;
        }
        else
        {
            _hoverIndex = -1;
        }

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

    bool IVirtualizedTabNavigationHost.TryMoveFocusFromDescendant(UIElement focusedElement, bool moveForward)
    {
        if (!IsEffectivelyEnabled || ItemsSource.Count == 0)
        {
            return false;
        }

        int found = -1;
        _presenter.VisitRealized((i, element) =>
        {
            if (found != -1)
            {
                return;
            }

            if (VisualTree.IsInSubtreeOf(focusedElement, element))
            {
                found = i;
            }
        });

        if (found < 0)
        {
            return false;
        }

        int target = moveForward ? found + 1 : found - 1;
        if (target < 0 || target >= ItemsSource.Count)
        {
            return false;
        }

        SelectedIndex = target;
        ScrollIntoView(target);
        _tabFocusHelper.Schedule(target);
        return true;
    }

    protected override void OnDispose()
    {
        _itemsSource.Changed -= OnItemsChanged;
        _itemsSource.SelectionChanged -= OnItemsSelectionChanged;
    }
}
