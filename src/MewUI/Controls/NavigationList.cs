using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Single-selection vertical list for navigation panes. Rows are typed via <see cref="KindSelector"/>
/// (<see cref="NavigationItemKind"/>); headers and separators are skipped by selection, hover, and keyboard.
/// </summary>
public class NavigationList : ScrollableItemsBase, ISelector, IIndexedSelector
{
    public static readonly MewProperty<int> SelectedIndexProperty =
        MewProperty<int>.Register<NavigationList>(nameof(SelectedIndex), -1,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnSelectedIndexPropertyChanged(newVal));

    public static readonly MewProperty<object?> SelectedItemProperty =
        MewProperty<object?>.Register<NavigationList>(nameof(SelectedItem), null,
            MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, newVal) => self.OnSelectedItemPropertyChanged(newVal));

    /// <summary>Gets or sets the padding around each row's content (default from theme).</summary>
    public static readonly MewProperty<Thickness> ItemPaddingProperty =
        MewProperty<Thickness>.Register<NavigationList>(nameof(ItemPadding), default, MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnItemPaddingChanged());

    /// <summary>
    /// Gets or sets the minimum height of selectable <see cref="NavigationItemKind.Item"/> rows. Headers and
    /// separators self-size and ignore this. <see cref="double.NaN"/> (default) resolves from the theme.
    /// </summary>
    public static readonly MewProperty<double> ItemHeightProperty =
        MewProperty<double>.Register<NavigationList>(nameof(ItemHeight), double.NaN, MewPropertyOptions.AffectsLayout,
            static (self, _, _) => self.OnItemPaddingChanged());

    private readonly StackItemsPresenter _presenter;
    private IDataTemplate _itemTemplate;
    private ISelectableItemsView _itemsSource = ItemsView.EmptySelectable;
    private Func<object?, NavigationItemKind>? _kindSelector;

    private int _hoverIndex = -1;
    private bool _hasLastMousePosition;
    private Point _lastMousePosition;
    private readonly SelectionSync _selection;

    public NavigationList()
    {
        _selection = new SelectionSync(() => _itemsSource,
            value => SetValue(SelectedIndexProperty, value),
            value => SetValue(SelectedItemProperty, value));

        _itemTemplate = CreateDefaultItemTemplate();

        _presenter = new StackItemsPresenter();
        _presenter.ItemsSource = _itemsSource;
        // Each row is wrapped in a host that owns the padding and the per-kind min-height.
        _presenter.ItemTemplate = new RowTemplate(this);
        _presenter.BeforeItemRender = OnBeforeItemRender;
        _presenter.OffsetCorrectionRequested += OnPresenterOffsetCorrectionRequested;

        // Base creates the ScrollViewer (chrome, hit-test delegation); nav rows never scroll horizontally.
        _scrollViewer.HorizontalScroll = ScrollMode.Disabled;
        // Overlay auto-hiding scroll bar so the narrow rail shows clean centered icons at rest.
        _scrollViewer.AutoHideScrollBars = true;
        _scrollViewer.Content = _presenter;
        _scrollViewer.SetBinding(PaddingProperty, this, PaddingProperty);
        _scrollViewer.ScrollChanged += OnScrollViewerChanged;

        _itemsSource.SelectionChanged += OnItemsSelectionChanged;
        _itemsSource.Changed += OnItemsChanged;

        ItemPadding = Theme.Metrics.ItemPadding;
    }

    /// <summary>Gets or sets the items data source.</summary>
    public ISelectableItemsView ItemsSource
    {
        get => _itemsSource;
        set => ApplyItemsSource(value);
    }

    /// <summary>Gets or sets the item template.</summary>
    public IDataTemplate ItemTemplate
    {
        get => _itemTemplate;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _itemTemplate = value;
            // Re-wrap so pooled containers rebuild against the new inner template.
            _presenter.ItemTemplate = new RowTemplate(this);
            InvalidateItemBindings();
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the per-item kind selector. When <see langword="null"/> every row is a selectable
    /// <see cref="NavigationItemKind.Item"/>.
    /// </summary>
    public Func<object?, NavigationItemKind>? KindSelector
    {
        get => _kindSelector;
        set
        {
            _kindSelector = value;
            if (SelectedIndex >= 0 && KindAt(SelectedIndex) != NavigationItemKind.Item)
            {
                SelectedIndex = -1;
            }
            InvalidateItemBindings();
            InvalidateVisual();
        }
    }

    /// <summary>Gets or sets the selected row index (-1 = none). Non-item rows are rejected.</summary>
    public int SelectedIndex
    {
        get => GetValue(SelectedIndexProperty);
        set => SetValue(SelectedIndexProperty, value);
    }

    public Thickness ItemPadding
    {
        get => GetValue(ItemPaddingProperty);
        set => SetValue(ItemPaddingProperty, value);
    }

    public double ItemHeight
    {
        get => GetValue(ItemHeightProperty);
        set => SetValue(ItemHeightProperty, value);
    }

    /// <summary>Gets the selected item object, or <see langword="null"/>.</summary>
    public object? SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Occurs when the selection changes.</summary>
    public event Action<object?>? SelectionChanged;

    /// <summary>Occurs when a selectable row is activated by click or Enter.</summary>
    public event Action<object?>? ItemInvoked;

    public bool TryGetItemIndexAt(Point position, out int index) => TryGetItemIndexAtCore(position, out index);

    public bool TryGetItemIndexAt(MouseEventArgs e, out int index)
    {
        ArgumentNullException.ThrowIfNull(e);
        return TryGetItemIndexAtCore(e.GetPosition(this), out index);
    }

    /// <summary>Fully clears the container pool and rebuilds all rows (e.g. after a template-affecting change).</summary>
    public void RefreshItems()
    {
        InvalidateItemBindings();
        // Rebinding can change row sizes; force a re-arrange so the presenter rebuilds its arranged-item
        // list instead of rendering the previous mode's positions (what a manual scroll would otherwise fix).
        InvalidateArrange();
    }

    private NavigationItemKind KindAt(int index)
    {
        if (_kindSelector == null || index < 0 || index >= _itemsSource.Count)
        {
            return NavigationItemKind.Item;
        }
        return _kindSelector(_itemsSource.GetItem(index));
    }

    // Padding/min-height live on the row host; a change just needs a rebind.
    private void OnItemPaddingChanged()
    {
        InvalidateItemBindings();
        InvalidateMeasure();
    }

    // Min height for selectable item rows (headers/separators ignore it).
    private double ResolveItemHeight()
    {
        if (!double.IsNaN(ItemHeight) && ItemHeight > 0)
        {
            return ItemHeight;
        }
        return Math.Max(18, Theme.Metrics.BaseControlHeight - 2);
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

    private void ApplyItemsSource(ISelectableItemsView? value)
    {
        value ??= ItemsView.EmptySelectable;
        if (ReferenceEquals(_itemsSource, value))
        {
            return;
        }

        _itemsSource.Changed -= OnItemsChanged;
        _itemsSource.SelectionChanged -= OnItemsSelectionChanged;

        _itemsSource = value;
        _itemsSource.SelectionChanged += OnItemsSelectionChanged;
        _itemsSource.Changed += OnItemsChanged;
        _presenter.ItemsSource = _itemsSource;

        _hoverIndex = -1;
        _selection.SyncFromModel();

        InvalidateItemBindings();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnPresenterOffsetCorrectionRequested(Point offset)
        => _scrollViewer.SetScrollOffsets(_scrollViewer.HorizontalOffset, offset.Y);

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        // Publish the binding generation before measuring so SyncContainers rebinds realized rows this pass,
        // not one layout late. A mode switch that only bumps the generation must take effect immediately.
        _presenter.ItemBindingGeneration = ItemBindingGeneration;
        _presenter.ItemHeightHint = ResolveItemHeight();
        _presenter.ExtentWidth = double.IsPositiveInfinity(availableSize.Width)
            ? 0
            : Math.Max(0, availableSize.Width - Padding.HorizontalThickness - borderInset * 2);

        _scrollViewer.Measure(new Size(
            double.IsPositiveInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - borderInset * 2),
            double.IsPositiveInfinity(availableSize.Height) ? double.PositiveInfinity : Math.Max(0, availableSize.Height - borderInset * 2)));

        double height = _presenter.DesiredContentHeight;
        if (height <= 0)
        {
            height = ItemsSource.Count * ResolveItemHeight();
        }

        double width = double.IsPositiveInfinity(availableSize.Width)
            ? _scrollViewer.DesiredSize.Width
            : Math.Max(0, availableSize.Width - borderInset * 2);

        return new Size(
            Math.Max(0, width + borderInset * 2),
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

        if (TryConsumeScrollIntoViewRequest(out var request) && request.Kind == ScrollIntoViewRequestKind.Index)
        {
            _presenter.RequestScrollIntoView(request.Index);
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        double radius = CornerRadius;

        DrawBackgroundAndBorder(context, bounds, GetValue(BackgroundProperty), GetValue(BorderBrushProperty),
            BorderThickness, radius);

        var borderInset = GetBorderVisualInset();
        var dpiScale = GetDpi() / 96.0;
        var clipR = Math.Max(0, LayoutRounding.RoundToPixel(radius, dpiScale) - borderInset);
        _scrollViewer.CornerRadius = clipR;
        _presenter.ItemRadius = clipR;

        _scrollViewer.Render(context);
    }

    // The scroll viewer is rendered explicitly in OnRender so background/border layer correctly.
    protected override void RenderSubtree(IGraphicsContext context)
    {
    }

    private void OnBeforeItemRender(IGraphicsContext context, int i, Rect itemRect)
    {
        // Selection/hover backgrounds apply to selectable items only; headers/separators stay flat.
        if (KindAt(i) != NavigationItemKind.Item)
        {
            return;
        }

        double itemRadius = _presenter.ItemRadius;
        bool selected = i == SelectedIndex;

        if (selected)
        {
            var bg = Theme.Palette.SelectionBackground;
            if (itemRadius > 0)
            {
                context.FillRoundedRectangle(itemRect, itemRadius, itemRadius, bg);
            }
            else
            {
                context.FillRectangle(itemRect, bg);
            }
        }
        else if (i == _hoverIndex)
        {
            var bg = Theme.Palette.ControlBackground.Lerp(Theme.Palette.Accent, 0.15);
            if (itemRadius > 0)
            {
                context.FillRoundedRectangle(itemRect, itemRadius, itemRadius, bg);
            }
            else
            {
                context.FillRectangle(itemRect, bg);
            }
        }
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

        int hover = TryGetItemIndexAtCore(_lastMousePosition, out int index) && KindAt(index) == NavigationItemKind.Item ? index : -1;
        if (_hoverIndex != hover)
        {
            _hoverIndex = hover;
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

        if (e.Button == MouseButton.Left && TryGetItemIndexAt(e, out int index) && KindAt(index) == NavigationItemKind.Item)
        {
            SelectedIndex = index;
            ItemInvoked?.Invoke(_itemsSource.GetItem(index));
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

        switch (e.Key)
        {
            case Key.Up:
                MoveSelection(-1);
                e.Handled = true;
                break;
            case Key.Down:
                MoveSelection(+1);
                e.Handled = true;
                break;
            case Key.Home:
                SelectEdge(forward: true);
                e.Handled = true;
                break;
            case Key.End:
                SelectEdge(forward: false);
                e.Handled = true;
                break;
            case Key.Enter:
                if (SelectedIndex >= 0)
                {
                    ItemInvoked?.Invoke(SelectedItem);
                    e.Handled = true;
                }
                break;
        }
    }

    private void MoveSelection(int direction)
    {
        int count = ItemsSource.Count;
        int i = SelectedIndex;
        while (true)
        {
            i += direction;
            if (i < 0 || i >= count)
            {
                return;
            }
            if (KindAt(i) == NavigationItemKind.Item)
            {
                SelectedIndex = i;
                ScrollIntoView(i);
                InvalidateVisual();
                return;
            }
        }
    }

    private void SelectEdge(bool forward)
    {
        int count = ItemsSource.Count;
        if (forward)
        {
            for (int i = 0; i < count; i++)
            {
                if (KindAt(i) == NavigationItemKind.Item) { SelectedIndex = i; ScrollIntoView(i); InvalidateVisual(); return; }
            }
        }
        else
        {
            for (int i = count - 1; i >= 0; i--)
            {
                if (KindAt(i) == NavigationItemKind.Item) { SelectedIndex = i; ScrollIntoView(i); InvalidateVisual(); return; }
            }
        }
    }

    public void ScrollIntoView(int index)
    {
        if (index < 0 || index >= ItemsSource.Count)
        {
            return;
        }

        // Defer only until the viewport is known; otherwise drive the presenter directly so the
        // offset correction runs this cycle instead of waiting for a later arrange to forward it.
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

        if (ItemsSource.Count <= 0)
        {
            return false;
        }

        return TryMapPointToItemIndex(position, _presenter, out index);
    }

    private void OnItemsChanged(ItemsChange change)
    {
        if (change.Kind is ItemsChangeKind.Reset or ItemsChangeKind.Move)
        {
            _presenter.RecycleAll();
        }
        _hoverIndex = -1;
        InvalidateItemBindings();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnSelectedIndexPropertyChanged(int newIndex)
    {
        if (_selection.Syncing)
        {
            return;
        }

        // Reject selecting a non-item row; revert to the real selection.
        if (newIndex >= 0 && KindAt(newIndex) != NavigationItemKind.Item)
        {
            _selection.SyncFromModel();
            return;
        }

        _selection.PushIndex(newIndex);
    }

    private void OnSelectedItemPropertyChanged(object? item) => _selection.PushItem(item);

    private void OnItemsSelectionChanged(int index)
    {
        InvalidateItemBindings();
        _selection.SyncFromModel();
        SelectionChanged?.Invoke(_itemsSource.SelectedItem);
        // Bring the selection fully into view (covers click / keyboard / programmatic paths), so selecting
        // a row that sits on the scroll edge is not left clipped.
        ScrollIntoView(index);
        InvalidateVisual();
    }

    private void OnScrollViewerChanged()
    {
        _hoverIndex = _hasLastMousePosition && TryGetItemIndexAtCore(_lastMousePosition, out int hover) && KindAt(hover) == NavigationItemKind.Item
            ? hover
            : -1;
        InvalidateVisual();
    }

    private IDataTemplate CreateDefaultItemTemplate()
        => new DelegateTemplate<object?>(
            build: _ => new TextBlock { IsHitTestVisible = false, TextWrapping = TextWrapping.NoWrap },
            bind: (view, _, index, _) =>
            {
                var tb = (TextBlock)view;
                var text = ItemsSource.GetText(index);
                if (tb.Text != text) tb.Text = text;
                if (tb.FontFamily != FontFamily) tb.FontFamily = FontFamily;
                if (!tb.FontSize.Equals(FontSize)) tb.FontSize = FontSize;
            });

    protected override void OnDispose()
    {
        _itemsSource.Changed -= OnItemsChanged;
        _itemsSource.SelectionChanged -= OnItemsSelectionChanged;
    }

    // Wraps each row's content in a host that owns ItemPadding and, for Item rows, the min-height.
    private sealed class RowTemplate : IDataTemplate
    {
        private readonly NavigationList _owner;

        public RowTemplate(NavigationList owner) => _owner = owner;

        public FrameworkElement Build(TemplateContext context)
            => new Border { Padding = _owner.ItemPadding, Child = _owner._itemTemplate.Build(context) };

        public void Bind(FrameworkElement view, object? item, int index, TemplateContext context)
        {
            var host = (Border)view;
            host.Padding = _owner.ItemPadding;
            host.MinHeight = _owner.KindAt(index) == NavigationItemKind.Item ? _owner.ResolveItemHeight() : 0;
            _owner._itemTemplate.Bind((FrameworkElement)host.Child!, item, index, context);
        }

        public void Unbind(FrameworkElement view, object? item, int index, TemplateContext context)
            => _owner._itemTemplate.Unbind((FrameworkElement)((Border)view).Child!, item, index, context);
    }
}

/// <summary>
/// Role of a <see cref="NavigationList"/> row. Only <see cref="Item"/> is selectable; headers and
/// separators are skipped by selection, hover, and keyboard navigation.
/// </summary>
public enum NavigationItemKind
{
    /// <summary>Selectable navigation entry.</summary>
    Item,

    /// <summary>Non-interactive section header.</summary>
    Header,

    /// <summary>Non-interactive divider.</summary>
    Separator,
}
