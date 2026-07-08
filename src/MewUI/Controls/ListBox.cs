using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A scrollable list control with item selection.
/// </summary>
public partial class ListBox : ScrollableItemsBase, IVirtualizedTabNavigationHost, ISelector, IIndexedSelector, IMultiSelector
{
    public static readonly MewProperty<int> SelectedIndexProperty =
        MewProperty<int>.Register<ListBox>(nameof(SelectedIndex), -1,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnSelectedIndexPropertyChanged(newVal));

    public static readonly MewProperty<ItemsSelectionMode> SelectionModeProperty =
        MewProperty<ItemsSelectionMode>.Register<ListBox>(nameof(SelectionMode), ItemsSelectionMode.Single,
            MewPropertyOptions.None,
            static (self, _, newVal) => self.OnSelectionModePropertyChanged(newVal));

    public static readonly MewProperty<object?> SelectedItemProperty =
        MewProperty<object?>.Register<ListBox>(nameof(SelectedItem), null,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnSelectedItemPropertyChanged(newVal));

    private static readonly MewPropertyKey<IReadOnlyList<object?>> SelectedItemsPropertyKey =
        MewProperty<IReadOnlyList<object?>>.RegisterReadOnly<ListBox>(nameof(SelectedItems), Array.Empty<object?>());
    public static readonly MewProperty<IReadOnlyList<object?>> SelectedItemsProperty = SelectedItemsPropertyKey.Property;

    public static readonly MewProperty<bool> ZebraStripingProperty =
        MewProperty<bool>.Register<ListBox>(nameof(ZebraStriping), true, MewPropertyOptions.AffectsRender);

    private readonly TextWidthCache _textWidthCache = new(512);
    private IItemsPresenter _presenter;
    private IDataTemplate _itemTemplate;

    private int _hoverIndex = -1;
    private bool _hasLastMousePosition;
    private Point _lastMousePosition;
    private readonly SelectionSync _selection;
    private bool _suppressItemsSelectionChanged;
    private ISelectableItemsView _itemsSource = ItemsView.EmptySelectable;

    public bool ZebraStriping
    {
        get => GetValue(ZebraStripingProperty);
        set => SetValue(ZebraStripingProperty, value);
    }

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
        if (_itemsSource is IMultiSelectableItemsView oldMulti)
        {
            oldMulti.SelectedIndicesChanged -= OnItemsSelectedIndicesChanged;
        }

        _itemsSource = value;
        _itemsSource.SelectionChanged += OnItemsSelectionChanged;
        _itemsSource.Changed += OnItemsChanged;
        if (_itemsSource is IMultiSelectableItemsView newMulti)
        {
            newMulti.SelectedIndicesChanged += OnItemsSelectedIndicesChanged;
        }

        // The selection mode is a control-level setting; re-apply it so it survives a source swap.
        _itemsSource.SetSelectionMode(SelectionMode);

        _presenter.ItemsSource = _itemsSource;

        _hoverIndex = -1;
        InvalidateItemBindings();

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
            _selection.SyncFromModel();
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
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    /// <summary>
    /// Gets the currently selected item object.
    /// </summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    private IMultiSelectableItemsView? MultiView => _itemsSource.AsMultiSelectable();

    /// <summary>
    /// Gets or sets the selection mode. Requires an items source that supports multi-selection
    /// (e.g. created from a typed list); otherwise stays <see cref="ItemsSelectionMode.Single"/>.
    /// </summary>
    public ItemsSelectionMode SelectionMode
    {
        get => GetValue(SelectionModeProperty);
        set => SetValue(SelectionModeProperty, value);
    }

    private void OnSelectionModePropertyChanged(ItemsSelectionMode mode)
        => _itemsSource.SetSelectionMode(mode);

    /// <summary>Gets the selected item indices in ascending order.</summary>
    public IReadOnlyList<int> SelectedIndices => _selection.SelectedIndices;

    /// <summary>Gets the selected items in ascending index order (read-only, bindable).</summary>
    public IReadOnlyList<object?> SelectedItems => GetValue(SelectedItemsProperty);

    /// <summary>Selects every item (multi-selection modes only; no-op otherwise).</summary>
    public void SelectAll() => _selection.SelectAll();

    /// <summary>Clears the entire selection.</summary>
    public void ClearSelection() => _selection.ClearSelection();

    /// <summary>Selects the inclusive range [start, end], replacing the current selection (multi only).</summary>
    public void SelectRange(int start, int end) => _selection.SelectRange(start, end);

    /// <summary>Occurs when the set of selected items changes (multi-select).</summary>
    public event Action? SelectedIndicesChanged;

    /// <summary>Returns whether the item at <paramref name="index"/> is selected.</summary>
    public bool IsSelected(int index) => _selection.IsSelected(index);

    /// <summary>
    /// Gets the currently selected item text.
    /// </summary>
    public string? SelectedText => SelectedIndex >= 0 && SelectedIndex < ItemsSource.Count ? ItemsSource.GetText(SelectedIndex) : null;

    /// <summary>
    /// Gets or sets the height of each list item.
    /// </summary>
    public static readonly MewProperty<double> ItemHeightProperty =
        MewProperty<double>.Register<ListBox>(nameof(ItemHeight), double.NaN, MewPropertyOptions.AffectsLayout);

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>
    /// Gets or sets the padding around each item's text.
    /// </summary>
    public static readonly MewProperty<Thickness> ItemPaddingProperty =
        MewProperty<Thickness>.Register<ListBox>(nameof(ItemPadding), default, MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnItemPaddingChanged());

    public Thickness ItemPadding
    {
        get => GetValue(ItemPaddingProperty);
        set => SetValue(ItemPaddingProperty, value);
    }

    private void OnItemPaddingChanged()
    {
        if (_presenter != null)
            _presenter.ItemPadding = ItemPadding;
        InvalidateItemBindings();
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
            InvalidateItemBindings();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }


    /// <summary>
    /// Occurs when the selected item changes.
    /// </summary>
    public event Action<object?>? SelectionChanged;

    // Internal commit signal for the ComboBox popup ("user picked a row, close"); not a public
    // feature. The public item-invoke concept lives on NavigationList.ItemInvoked.
    internal event Action<int>? ItemActivated;

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
        _selection = new SelectionSync(() => _itemsSource,
            value => SetValue(SelectedIndexProperty, value),
            value => SetValue(SelectedItemProperty, value),
            value => SetValue(SelectedItemsPropertyKey, value));

        _scrollViewer.HorizontalScroll = ScrollMode.Disabled;

        ItemPadding = Theme.Metrics.ItemPadding;

        _itemsSource.SelectionChanged += OnItemsSelectionChanged;
        _itemsSource.Changed += OnItemsChanged;
        if (_itemsSource is IMultiSelectableItemsView multi)
        {
            multi.SelectedIndicesChanged += OnItemsSelectedIndicesChanged;
        }

        _itemTemplate = CreateDefaultItemTemplate();
        _presenter = CreateDefaultPresenter();
        _tabFocusHelper = new PendingTabFocusHelper(
            getWindow: () => FindVisualRoot() as Window,
            getContainer: idx =>
            {
                FrameworkElement? container = null;
                _presenter.VisitRealized((i, el) => { if (i == idx) container = el; });
                return container;
            });

        _scrollViewer.SetBinding(PaddingProperty, this, PaddingProperty);
        _scrollViewer.Content = (UIElement)_presenter;
        _scrollViewer.ScrollChanged += OnScrollViewerChanged;
    }

    internal void SetPresenter(IItemsPresenter presenter)
    {
        double oldX = _scrollViewer.HorizontalOffset;
        double oldY = _scrollViewer.VerticalOffset;

        _presenter.OffsetCorrectionRequested -= OnPresenterOffsetCorrectionRequested;
        if (_presenter is IDisposable d)
        {
            d.Dispose();
        }

        InitializePresenter(presenter);

        _presenter = presenter;
        _scrollViewer.Content = (UIElement)_presenter;
        _scrollViewer.VerticalScroll = presenter is StackItemsPresenter
            ? ScrollMode.Disabled
            : ScrollMode.Auto;
        _scrollViewer.SetScrollOffsets(oldX, oldY);

        InvalidateItemBindings();
        _hoverIndex = -1;
        InvalidateMeasure();
        InvalidateVisual();
    }

    private IItemsPresenter CreateDefaultPresenter()
    {
        var p = new FixedHeightItemsPresenter();
        InitializePresenter(p);
        return p;
    }

    private void InitializePresenter(IItemsPresenter presenter)
    {
        presenter.ItemsSource = _itemsSource;
        presenter.ItemTemplate = _itemTemplate;
        presenter.BeforeItemRender = OnBeforeItemRender;
        presenter.ItemPadding = ItemPadding;
        presenter.ItemHeightHint = ResolveItemHeight();
        presenter.OffsetCorrectionRequested += OnPresenterOffsetCorrectionRequested;
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

        InvalidateItemBindings();
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

        _presenter.ItemHeightHint = itemHeight;
        _presenter.ExtentWidth = maxWidth;

        _scrollViewer.Measure(new Size(
            double.IsPositiveInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - borderInset * 2),
            double.IsPositiveInfinity(availableSize.Height) ? double.PositiveInfinity : Math.Max(0, availableSize.Height - borderInset * 2)));

        // - FixedHeight/VariableHeight/Stack: count * itemHeight
        // - Wrap: rows * itemHeight
        double height = _presenter.DesiredContentHeight;
        if (height <= 0)
            height = count * itemHeight;

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

        _presenter.ItemBindingGeneration = ItemBindingGeneration;

        _scrollViewer.Arrange(innerBounds);

        if (TryConsumeScrollIntoViewRequest(out var request) &&
            request.Kind == ScrollIntoViewRequestKind.Index)
        {
            ScrollIntoView(request.Index);
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        double radius = CornerRadius;

        DrawBackgroundAndBorder(
            context,
            bounds,
            GetValue(BackgroundProperty),
            GetValue(BorderBrushProperty),
            BorderThickness,
            radius);

        var borderInset = GetBorderVisualInset();
        var dpiScale = GetDpi() / 96.0;
        var clipR = Math.Max(0, LayoutRounding.RoundToPixel(radius, dpiScale) - borderInset);
        _scrollViewer.CornerRadius = clipR;
        _presenter.ItemRadius = clipR;

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
            var multi = MultiView;
            if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
            {
                ItemsSelectionInput.HandleClick(multi, index, e.Modifiers);
            }
            else
            {
                SelectedIndex = index;
            }

            ItemActivated?.Invoke(index);
            InvalidateVisual();
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

        int count = ItemsSource.Count;
        if (count == 0)
        {
            return;
        }

        var multi = MultiView;

        if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single && IsSelectAllShortcut(e))
        {
            multi.SelectRange(0, count - 1, clearExisting: true);
            e.Handled = true;
            InvalidateVisual();
            return;
        }

        int current = SelectedIndex;
        int target = current;
        bool navigated = false;
        switch (e.Key)
        {
            case Key.Up:
            case Key.Down:
            case Key.Home:
            case Key.End:
                navigated = TryGetListNavigationTarget(e.Key, current, count, pageStep: 1, supportsPaging: false, out target);
                e.Handled = navigated;
                break;
            case Key.Enter:
                if (current >= 0)
                {
                    ItemActivated?.Invoke(current);
                    e.Handled = true;
                }
                break;
        }

        if (navigated)
        {
            if (multi != null && multi.SelectionMode != ItemsSelectionMode.Single)
            {
                ItemsSelectionInput.HandleKeyboardMove(multi, target, (e.Modifiers & ModifierKeys.Shift) != 0);
            }
            else
            {
                SelectedIndex = target;
            }

            ScrollIntoView(target);
            InvalidateVisual();
        }
        else if (e.Handled)
        {
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
            RequestScrollIntoView(ScrollIntoViewRequest.IndexRequest(index));
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

        return TryMapPointToItemIndex(position, _presenter, out index);
    }

    private void OnBeforeItemRender(IGraphicsContext context, int i, Rect itemRect)
    {
        double itemRadius = _presenter.ItemRadius;

        bool selected = IsSelected(i);

        if (ZebraStriping && (i & 1) == 1 && !selected && i != _hoverIndex)
        {
            var theme = Theme;
            var bg = theme.Palette.ControlBackground.Lerp(theme.Palette.ButtonFace, theme.IsDark ? 0.45 : 0.33);
            context.FillRectangle(itemRect, bg);
        }

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
                new TextBlock
                {
                    IsHitTestVisible = false,
                    TextWrapping = TextWrapping.NoWrap,
                },
            bind: (view, _, index, _) =>
            {
                var tb = (TextBlock)view;

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

                var fg = ResolveItemForeground(IsSelected(index));

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
        InvalidateItemBindings();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnSelectedIndexPropertyChanged(int newIndex) => _selection.PushIndex(newIndex);

    private void OnSelectedItemPropertyChanged(object? item) => _selection.PushItem(item);

    private void OnItemsSelectionChanged(int index)
    {
        if (_suppressItemsSelectionChanged)
        {
            return;
        }

        InvalidateItemBindings();
        _selection.SyncFromModel();
        SelectionChanged?.Invoke(_itemsSource.SelectedItem);
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

    bool IVirtualizedTabNavigationHost.TryMoveFocusFromDescendant(UIElement focusedElement, bool moveForward)
    {
        if (!IsEffectivelyEnabled || ItemsSource.Count == 0)
        {
            return false;
        }

        if (!TryFindRealizedIndex(_presenter, focusedElement, out int found, out var foundContainer))
        {
            return false;
        }

        var edge = moveForward
            ? FocusManager.FindLastFocusable(foundContainer)
            : FocusManager.FindFirstFocusable(foundContainer);
        if (edge != null && !ReferenceEquals(edge, focusedElement))
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
        _tabFocusHelper.Schedule(target, moveForward);
        return true;
    }

    protected override void OnDispose()
    {
        _itemsSource.Changed -= OnItemsChanged;
        _itemsSource.SelectionChanged -= OnItemsSelectionChanged;
        if (_itemsSource is IMultiSelectableItemsView multi)
        {
            multi.SelectedIndicesChanged -= OnItemsSelectedIndicesChanged;
        }
    }

    private void OnItemsSelectedIndicesChanged()
    {
        _selection.SyncFromModel();
        SelectedIndicesChanged?.Invoke();
        InvalidateItemBindings();
        InvalidateVisual();
    }
}
