using Aprillz.MewUI.Input;
using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Specifies which user interaction(s) toggle node expansion in a <see cref="TreeView"/>.
/// </summary>
public enum TreeViewExpandTrigger
{
    /// <summary>
    /// Expands/collapses when the expander chevron is clicked.
    /// </summary>
    ClickChevron,

    /// <summary>
    /// Expands/collapses when the expander chevron is clicked, or when a node row is double-clicked.
    /// </summary>
    DoubleClickNode,

    /// <summary>
    /// Expands/collapses when the expander chevron is clicked, or when a node row is single-clicked.
    /// </summary>
    ClickNode,
}

/// <summary>
/// A hierarchical tree view control with expand/collapse functionality.
/// </summary>
public sealed class TreeView : Control, IVisualTreeHost, IFocusIntoViewHost, IVirtualizedTabNavigationHost
{
    private readonly TextWidthCache _textWidthCache = new(512);
    private readonly ScrollBar _vBar;
    private readonly ScrollBar _hBar;
    private readonly ScrollController _scroll = new();
    private readonly TemplatedItemsHost _itemsHost;
    private bool _rebindVisibleOnNextRender = true;
    private ITreeItemsView _itemsSource = TreeItemsView.Empty;
    private TreeViewNode? _selectedNode;
    private int _hoverVisibleIndex = -1;
    private bool _hasLastMousePosition;
    private Point _lastMousePosition;

    private double _extentHeight;
    private double _extentWidth;
    private double _viewportHeight;
    private double _viewportWidth;
    private ScrollIntoViewRequest _scrollIntoViewRequest;
    private int _pendingTabFocusIndex = -1;
    private int _pendingTabFocusAttempts;

    protected override double DefaultBorderThickness => Theme.Metrics.ControlBorderThickness;

    /// <summary>
    /// Gets or sets the root nodes collection.
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

            _itemsSource.Changed -= OnItemsChanged;
            _itemsSource.SelectionChanged -= OnItemsSelectionChanged;

            _itemsSource = value as ITreeItemsView ?? new TreeViewNodeItemsView(value);
            _itemsSource.Changed += OnItemsChanged;
            _itemsSource.SelectionChanged += OnItemsSelectionChanged;

            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the currently selected tree node.
    /// </summary>
    public TreeViewNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            if (ReferenceEquals(_selectedNode, value))
            {
                return;
            }

            SetSelectedNodeCore(value);
        }
    }

    /// <summary>
    /// Gets or sets the selected node as an object for consistency with selector-style controls.
    /// </summary>
    public object? SelectedItem
    {
        get => SelectedNode;
        set => SelectedNode = value as TreeViewNode;
    }

    /// <summary>
    /// Occurs when the selected item changes.
    /// </summary>
    public event Action<object?>? SelectionChanged;

    /// <summary>
    /// Occurs when the selected node changes.
    /// </summary>
    public event Action<TreeViewNode?>? SelectedNodeChanged;

    /// <summary>
    /// Gets or sets the height of each tree node row.
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
    /// Gets or sets the padding around each node's text.
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
    /// Gets or sets the node template. If not set explicitly, the default template is used.
    /// </summary>
    public IDataTemplate ItemTemplate
    {
        get => _itemsHost.ItemTemplate;
        set { _itemsHost.ItemTemplate = value; _rebindVisibleOnNextRender = true; }
    }

    /// <summary>
    /// Gets or sets the horizontal indentation per tree level.
    /// </summary>
    public double Indent
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
    } = 16;

    /// <summary>
    /// Gets or sets which user interactions toggle node expansion.
    /// </summary>
    public TreeViewExpandTrigger ExpandTrigger { get; set; } = TreeViewExpandTrigger.ClickChevron;

    /// <summary>
    /// Initializes a new instance of the TreeView class.
    /// </summary>
    public TreeView()
    {
        Padding = new Thickness(1);
        ItemPadding = Theme.Metrics.ItemPadding;

        var template = CreateDefaultItemTemplate();
        _itemsHost = new TemplatedItemsHost(
            owner: this,
            getItem: index =>
            {
                return index >= 0 && index < _itemsSource.Count ? _itemsSource.GetItem(index) : null;
            },
            invalidateMeasureAndVisual: () => { InvalidateMeasure(); InvalidateVisual(); },
            template: template);

        _itemsHost.Options = new TemplatedItemsHost.ItemsRangeOptions
        {
            BeforeItemRender = OnBeforeItemRender,
            GetContainerRect = OnGetContainerRect,
        };

        _itemsSource.Changed += OnItemsChanged;
        _itemsSource.SelectionChanged += OnItemsSelectionChanged;

        _vBar = new ScrollBar { Orientation = Orientation.Vertical, IsVisible = false };
        _vBar.Parent = this;
        _vBar.ValueChanged += v =>
        {
            _scroll.DpiScale = GetDpi() / 96.0;
            _scroll.SetMetricsDip(1, _extentHeight, GetViewportHeightDip());
            _scroll.SetOffsetDip(1, v);
            _hoverVisibleIndex = -1;
            _hasLastMousePosition = false;
            InvalidateVisual();
            ReevaluateMouseOverAfterScroll();
        };

        _hBar = new ScrollBar { Orientation = Orientation.Horizontal, IsVisible = false };
        _hBar.Parent = this;
        _hBar.ValueChanged += v =>
        {
            _scroll.DpiScale = GetDpi() / 96.0;
            _scroll.SetMetricsDip(0, _extentWidth, GetViewportWidthDip());
            if (_scroll.SetOffsetDip(0, v))
            {
                _hoverVisibleIndex = -1;
                _hasLastMousePosition = false;
                InvalidateArrange();
                InvalidateVisual();
                ReevaluateMouseOverAfterScroll();
            }
        };
    }

    private Rect OnGetContainerRect(int i, Rect rowRect)
    {
        int depth = _itemsSource.GetDepth(i);
        double indentX = rowRect.X + depth * Indent;
        double glyphW = Indent;
        var contentX = indentX + glyphW;
        return new Rect(
            contentX,
            rowRect.Y,
            Math.Max(0, rowRect.Right - contentX),
            rowRect.Height);
    }

    private void OnBeforeItemRender(IGraphicsContext context, int i, Rect itemRect)
    {
        double itemRadius = _itemsHost.Layout.ItemRadius;

        bool selected = i == _itemsSource.SelectedIndex;
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
        else if (i == _hoverVisibleIndex)
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

        int depth = _itemsSource.GetDepth(i);
        double indentX = itemRect.X + depth * Indent;
        var glyphRect = new Rect(indentX, itemRect.Y, Indent, itemRect.Height);
        var textColor = selected ? Theme.Palette.SelectionText : (IsEffectivelyEnabled ? Foreground : Theme.Palette.DisabledText);
        if (_itemsSource.GetHasChildren(i))
        {
            DrawExpanderGlyph(context, glyphRect, _itemsSource.GetIsExpanded(i), textColor);
        }
    }

    private void OnItemsChanged(ItemsChange change)
    {
        _itemsHost.RecycleAll();
        _rebindVisibleOnNextRender = true;
        _hoverVisibleIndex = -1;
        InvalidateMeasure();
        InvalidateVisual();
        ReevaluateMouseOverAfterScroll();
    }

    private void OnItemsSelectionChanged(int index)
    {
        var node = _itemsSource.SelectedItem as TreeViewNode;
        if (ReferenceEquals(_selectedNode, node))
        {
            return;
        }

        _selectedNode = node;
        _rebindVisibleOnNextRender = true;

        SelectedNodeChanged?.Invoke(node);
        SelectionChanged?.Invoke(node);
        ScrollIntoView(index);
        InvalidateVisual();
    }

    /// <summary>
    /// Scrolls the selected node into view.
    /// </summary>
    public void ScrollIntoViewSelected() => ScrollIntoView(_itemsSource.SelectedIndex);

    /// <summary>
    /// Scrolls the specified visible item index into view.
    /// </summary>
    public void ScrollIntoView(int index)
    {
        int count = _itemsSource.Count;
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

        double itemHeight = ResolveItemHeight();
        if (itemHeight <= 0 || double.IsNaN(itemHeight) || double.IsInfinity(itemHeight))
        {
            return;
        }

        _extentHeight = count * itemHeight;

        double oldOffset = _scroll.GetOffsetDip(1);
        double newOffset = ItemsViewportMath.ComputeScrollOffsetToBringItemIntoView(index, itemHeight, viewport, oldOffset);

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
    /// Gets whether the tree view can receive keyboard focus.
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
    /// Attempts to find the item (row) index at the specified position in this control's coordinates.
    /// </summary>
    public bool TryGetItemIndexAt(Point position, out int index)
        => TryGetItemIndexAtCore(position, out index);

    /// <summary>
    /// Attempts to find the item (row) index for a mouse event routed by the window input router.
    /// </summary>
    public bool TryGetItemIndexAt(MouseEventArgs e, out int index)
    {
        ArgumentNullException.ThrowIfNull(e);
        return TryGetItemIndexAtCore(e.GetPosition(this), out index);
    }

    private bool TryGetItemIndexAtCore(Point position, out int index)
    {
        index = -1;

        // Don't treat scrollbar interaction as item hit/activation.
        if (_vBar.IsVisible && GetLocalBounds(_vBar).Contains(position))
        {
            return false;
        }

        if (_hBar.IsVisible && GetLocalBounds(_hBar).Contains(position))
        {
            return false;
        }

        return TryHitRow(position, out index, out _);
    }

    private Rect GetLocalBounds(UIElement element)
        => new(
            element.Bounds.X - Bounds.X,
            element.Bounds.Y - Bounds.Y,
            element.Bounds.Width,
            element.Bounds.Height);

    /// <summary>
    /// Called when the theme changes.
    /// </summary>
    /// <param name="oldTheme">The previous theme.</param>
    /// <param name="newTheme">The new theme.</param>
    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        if (ItemPadding == oldTheme.Metrics.ItemPadding)
        {
            ItemPadding = newTheme.Metrics.ItemPadding;
        }

        _rebindVisibleOnNextRender = true;
    }

    void IVisualTreeHost.VisitChildren(Action<Element> visitor)
    {
        visitor(_vBar);
        visitor(_hBar);
        _itemsHost.VisitRealized(visitor);
    }

    /// <summary>
    /// Checks whether the specified node is expanded.
    /// </summary>
    /// <param name="node">The node to check.</param>
    /// <returns>True if the node is expanded.</returns>
    public bool IsExpanded(TreeViewNode node)
    {
        if (_itemsSource is TreeViewNodeItemsView nodeView)
        {
            return nodeView.IsExpanded(node);
        }

        int idx = IndexOfNode(node);
        return idx >= 0 && _itemsSource.GetIsExpanded(idx);
    }

    /// <summary>
    /// Expands the specified node to show its children.
    /// </summary>
    /// <param name="node">The node to expand.</param>
    public void Expand(TreeViewNode node)
    {
        if (_itemsSource is TreeViewNodeItemsView nodeView)
        {
            nodeView.Expand(node);
            return;
        }

        int idx = IndexOfNode(node);
        if (idx >= 0)
        {
            _itemsSource.SetIsExpanded(idx, true);
        }
    }

    /// <summary>
    /// Collapses the specified node to hide its children.
    /// </summary>
    /// <param name="node">The node to collapse.</param>
    public void Collapse(TreeViewNode node)
    {
        if (_itemsSource is TreeViewNodeItemsView nodeView)
        {
            nodeView.Collapse(node);
            return;
        }

        int idx = IndexOfNode(node);
        if (idx >= 0)
        {
            _itemsSource.SetIsExpanded(idx, false);
        }
    }

    /// <summary>
    /// Toggles the expansion state of the specified node.
    /// </summary>
    /// <param name="node">The node to toggle.</param>
    public void Toggle(TreeViewNode node)
    {
        if (IsExpanded(node))
        {
            Collapse(node);
        }
        else
        {
            Expand(node);
        }
    }

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var dpi = GetDpi();
        var dpiScale = dpi / 96.0;

        double widthLimit = double.IsPositiveInfinity(availableSize.Width)
            ? double.PositiveInfinity
            : Math.Max(0, availableSize.Width - Padding.HorizontalThickness - borderInset * 2);

        // Measure an estimated horizontal extent so we can provide overlay-style horizontal scrolling.
        // Keep the measure sampling strategy to avoid O(N) on large trees.
        double extentWidth = 0;
        if (_itemsSource.Count > 0)
        {
            using var measure = BeginTextMeasurement();

            int count = _itemsSource.Count;
            int sampleCount = Math.Clamp(count, 32, 256);

            _textWidthCache.SetCapacity(Math.Clamp(sampleCount * 4, 256, 4096));
            double padW = ItemPadding.HorizontalThickness;

            for (int i = 0; i < sampleCount; i++)
            {
                var text = _itemsSource.GetText(i);
                if (string.IsNullOrEmpty(text))
                {
                    continue;
                }

                int depth = _itemsSource.GetDepth(i);
                double indentW = depth * Indent + Indent; // includes glyph column
                double itemW = indentW + _textWidthCache.GetOrMeasure(measure.Context, measure.Font, dpi, text) + padW;
                extentWidth = Math.Max(extentWidth, itemW);
            }
        }

        double desiredWidth;
        if (HorizontalAlignment == HorizontalAlignment.Stretch && !double.IsPositiveInfinity(widthLimit))
        {
            desiredWidth = widthLimit;
        }
        else if (double.IsPositiveInfinity(widthLimit))
        {
            desiredWidth = extentWidth;
        }
        else
        {
            desiredWidth = Math.Min(extentWidth, widthLimit);
        }

        double itemHeight = ResolveItemHeight();
        double height = _itemsSource.Count * itemHeight;

        _extentHeight = height;
        _extentWidth = extentWidth;

        _viewportWidth = double.IsPositiveInfinity(availableSize.Width)
            ? desiredWidth
            : LayoutRounding.RoundToPixel(widthLimit, dpiScale);

        _viewportHeight = double.IsPositiveInfinity(availableSize.Height)
            ? height
            : LayoutRounding.RoundToPixel(Math.Max(0, availableSize.Height - Padding.VerticalThickness - borderInset * 2), dpiScale);

        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(0, _extentWidth, _viewportWidth);
        _scroll.SetMetricsDip(1, _extentHeight, _viewportHeight);

        double desiredHeight = double.IsPositiveInfinity(availableSize.Height)
            ? height
            : Math.Min(height, _viewportHeight);

        return new Size(desiredWidth, desiredHeight)
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

        _viewportWidth = LayoutRounding.RoundToPixel(Math.Max(0, innerBounds.Width - Padding.HorizontalThickness), dpiScale);
        _viewportHeight = LayoutRounding.RoundToPixel(Math.Max(0, innerBounds.Height - Padding.VerticalThickness), dpiScale);

        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(0, _extentWidth, _viewportWidth);
        _scroll.SetMetricsDip(1, _extentHeight, _viewportHeight);
        _scroll.SetOffsetPx(0, _scroll.GetOffsetPx(0));
        _scroll.SetOffsetPx(1, _scroll.GetOffsetPx(1));

        bool needH = _extentWidth > _viewportWidth + 0.5;
        bool needV = _extentHeight > _viewportHeight + 0.5;
        _vBar.IsVisible = needV;
        _hBar.IsVisible = needH;

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
                Math.Max(0, innerBounds.Height - (needH ? t : 0) - inset * 2)));
        }

        if (_hBar.IsVisible)
        {
            double t = Theme.Metrics.ScrollBarHitThickness;
            const double inset = 0;

            _hBar.Minimum = 0;
            _hBar.Maximum = Math.Max(0, _extentWidth - _viewportWidth);
            _hBar.ViewportSize = _viewportWidth;
            _hBar.SmallChange = Theme.Metrics.ScrollBarSmallChange;
            _hBar.LargeChange = Theme.Metrics.ScrollBarLargeChange;
            _hBar.Value = _scroll.GetOffsetDip(0);

            _hBar.Arrange(new Rect(
                innerBounds.X + inset,
                innerBounds.Bottom - t - inset,
                Math.Max(0, innerBounds.Width - (needV ? t : 0) - inset * 2),
                t));
        }

        if (!_scrollIntoViewRequest.IsNone)
        {
            var request = _scrollIntoViewRequest;
            _scrollIntoViewRequest.Clear();

            if (request.Kind == ScrollIntoViewRequestKind.Index)
            {
                ScrollIntoView(request.Index);
            }
            else if (request.Kind == ScrollIntoViewRequestKind.Selected)
            {
                ScrollIntoViewSelected();
            }
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);
        var dpiScale = GetDpi() / 96.0;
        double radius = Theme.Metrics.ControlCornerRadius;
        var borderInset = GetBorderVisualInset();
        double itemRadius = Math.Max(0, radius - borderInset);

        var state = GetVisualState();
        var bg = PickControlBackground(state);
        var borderColor = PickAccentBorder(Theme, BorderBrush, state, 0.6);
        DrawBackgroundAndBorder(context, bounds, bg, borderColor, radius);

        if (_itemsSource.Count == 0)
        {
            return;
        }

        var innerBounds = bounds.Deflate(new Thickness(borderInset));
        var viewportBounds = innerBounds;
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(viewportBounds.Deflate(Padding), dpiScale);

        context.Save();
        var clipRect = LayoutRounding.MakeClipRect(contentBounds, dpiScale);
        var clipRadius = Math.Max(0, radius - borderInset);
        clipRadius = LayoutRounding.RoundToPixel(clipRadius, dpiScale);
        clipRadius = Math.Min(clipRadius, Math.Min(clipRect.Width, clipRect.Height) / 2);
        if (clipRadius > 0)
        {
            context.SetClipRoundedRect(clipRect, clipRadius, clipRadius);
        }
        else
        {
            context.SetClip(clipRect);
        }

        double itemHeight = ResolveItemHeight();
        double horizontalOffset = _scroll.GetOffsetDip(0);
        double verticalOffset = _scroll.GetOffsetDip(1);

        ItemsViewportMath.ComputeVisibleRange(
            _itemsSource.Count,
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

        // Shift the realized containers by horizontal scroll. Keep the clip in viewport space so content is clipped
        // correctly, but move the content origin so templates get arranged with the updated X.
        var scrollContentBounds = new Rect(
            contentBounds.X - horizontalOffset,
            contentBounds.Y,
            Math.Max(contentBounds.Width, _extentWidth),
            contentBounds.Height);

        _itemsHost.Layout = new TemplatedItemsHost.ItemsRangeLayout
        {
            ContentBounds = scrollContentBounds,
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

        if (_hBar.IsVisible)
        {
            _hBar.Render(context);
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
            bind: (view, item, index, _) =>
            {
                var tb = (Label)view;

                var text = _itemsSource.GetText(index);
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

                var enabled = IsEffectivelyEnabled;
                if (tb.IsEnabled != enabled)
                {
                    tb.IsEnabled = enabled;
                }

                bool selected = index == _itemsSource.SelectedIndex;
                var fg = selected ? Theme.Palette.SelectionText : (enabled ? Foreground : Theme.Palette.DisabledText);
                if (tb.Foreground != fg)
                {
                    tb.Foreground = fg;
                }
            });

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        if (_vBar.IsVisible && _vBar.Bounds.Contains(point))
        {
            return _vBar;
        }

        if (_hBar.IsVisible && _hBar.Bounds.Contains(point))
        {
            return _hBar;
        }

        return base.OnHitTest(point);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        Focus();

        if (!TryHitRow(e.GetPosition(this), out int index, out bool onGlyph))
        {
            return;
        }

        _itemsSource.SelectedIndex = index;
        bool hasChildren = _itemsSource.GetHasChildren(index);
        if (onGlyph && hasChildren)
        {
            _itemsSource.SetIsExpanded(index, !_itemsSource.GetIsExpanded(index));
        }
        else if (!onGlyph && hasChildren && ExpandTrigger == TreeViewExpandTrigger.ClickNode)
        {
            _itemsSource.SetIsExpanded(index, !_itemsSource.GetIsExpanded(index));
        }

        e.Handled = true;
    }

    protected override void OnMouseDoubleClick(MouseEventArgs e)
    {
        base.OnMouseDoubleClick(e);

        if (e.Handled || !IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        if (ExpandTrigger != TreeViewExpandTrigger.DoubleClickNode)
        {
            return;
        }

        if (!TryHitRow(e.GetPosition(this), out int index, out bool onGlyph) || onGlyph)
        {
            return;
        }

        if (!_itemsSource.GetHasChildren(index))
        {
            return;
        }

        Focus();
        _itemsSource.SelectedIndex = index;
        _itemsSource.SetIsExpanded(index, !_itemsSource.GetIsExpanded(index));
        e.Handled = true;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        if (e.Handled)
        {
            return;
        }

        int notches = Math.Sign(e.Delta);
        if (notches == 0)
        {
            return;
        }

        _scroll.DpiScale = GetDpi() / 96.0;

        int axis = e.IsHorizontal ? 0 : 1;
        if (axis == 0)
        {
            if (_extentWidth <= GetViewportWidthDip() + 0.5)
            {
                return;
            }

            _scroll.SetMetricsDip(0, _extentWidth, GetViewportWidthDip());
            _scroll.ScrollByNotches(0, -notches, Theme.Metrics.ScrollWheelStep);
            if (_hBar.IsVisible)
            {
                _hBar.Value = _scroll.GetOffsetDip(0);
            }
        }
        else
        {
            if (_extentHeight <= GetViewportHeightDip() + 0.5)
            {
                return;
            }

            _scroll.SetMetricsDip(1, _extentHeight, GetViewportHeightDip());
            _scroll.ScrollByNotches(1, -notches, Theme.Metrics.ScrollWheelStep);
            if (_vBar.IsVisible)
            {
                _vBar.Value = _scroll.GetOffsetDip(1);
            }

            if (_hasLastMousePosition && TryHitRow(_lastMousePosition, out int hover, out _))
            {
                _hoverVisibleIndex = hover;
            }
            else
            {
                _hoverVisibleIndex = -1;
            }
        }

        InvalidateVisual();
        ReevaluateMouseOverAfterScroll();
        e.Handled = true;
    }

    private void ReevaluateMouseOverAfterScroll()
    {
        if (FindVisualRoot() is Window window)
        {
            window.ReevaluateMouseOver();
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
        _lastMousePosition = e.Position;

        int newHover = -1;
        if (TryHitRow(e.GetPosition(this), out int index, out _))
        {
            newHover = index;
        }

        if (_hoverVisibleIndex != newHover)
        {
            _hoverVisibleIndex = newHover;
            InvalidateVisual();
        }
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();

        _hasLastMousePosition = false;

        if (_hoverVisibleIndex != -1)
        {
            _hoverVisibleIndex = -1;
            InvalidateVisual();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (e.Handled || !IsEffectivelyEnabled)
        {
            return;
        }

        int count = _itemsSource.Count;
        if (count <= 0)
        {
            return;
        }

        int selected = _itemsSource.SelectedIndex;
        int current = selected >= 0 ? selected : 0;

        switch (e.Key)
        {
            case Key.Up:
                _itemsSource.SelectedIndex = Math.Max(0, current - 1);
                e.Handled = true;
                break;

            case Key.Down:
                _itemsSource.SelectedIndex = Math.Min(count - 1, current + 1);
                e.Handled = true;
                break;

            case Key.Home:
                _itemsSource.SelectedIndex = 0;
                e.Handled = true;
                break;

            case Key.End:
                _itemsSource.SelectedIndex = count - 1;
                e.Handled = true;
                break;

            case Key.Space:
            {
                int index = _itemsSource.SelectedIndex;
                if (index < 0 || index >= count)
                {
                    _itemsSource.SelectedIndex = 0;
                    e.Handled = true;
                    break;
                }

                if (_itemsSource.GetHasChildren(index))
                {
                    _itemsSource.SetIsExpanded(index, !_itemsSource.GetIsExpanded(index));
                    e.Handled = true;
                }
            }
            break;

            case Key.Right:
            {
                int index = _itemsSource.SelectedIndex;
                if (index < 0 || index >= count)
                {
                    break;
                }

                if (_itemsSource.GetHasChildren(index) && !_itemsSource.GetIsExpanded(index))
                {
                    _itemsSource.SetIsExpanded(index, true);
                    e.Handled = true;
                }
            }
            break;

            case Key.Left:
            {
                int index = _itemsSource.SelectedIndex;
                if (index < 0 || index >= count)
                {
                    break;
                }

                if (_itemsSource.GetHasChildren(index) && _itemsSource.GetIsExpanded(index))
                {
                    _itemsSource.SetIsExpanded(index, false);
                    e.Handled = true;
                }
            }
            break;
        }

        if (e.Handled)
        {
            Focus();
            InvalidateVisual();
        }
    }

    bool IFocusIntoViewHost.OnDescendantFocused(UIElement focusedElement)
    {
        if (focusedElement == this)
        {
            return false;
        }

        int found = -1;
        _itemsHost.VisitRealized((i, element) =>
        {
            if (found != -1)
            {
                return;
            }

            if (IsInSubtreeOf(focusedElement, element))
            {
                found = i;
            }
        });

        if (found < 0 || found >= _itemsSource.Count)
        {
            return false;
        }

        if (_itemsSource.SelectedIndex != found)
        {
            _itemsSource.SelectedIndex = found;
        }
        else
        {
            ScrollIntoView(found);
        }

        return true;
    }

    bool IVirtualizedTabNavigationHost.TryMoveFocusFromDescendant(UIElement focusedElement, bool moveForward)
    {
        if (!IsEffectivelyEnabled || _itemsSource.Count == 0)
        {
            return false;
        }

        int found = -1;
        _itemsHost.VisitRealized((i, element) =>
        {
            if (found != -1)
            {
                return;
            }

            if (IsInSubtreeOf(focusedElement, element))
            {
                found = i;
            }
        });

        if (found < 0)
        {
            return false;
        }

        int target = moveForward ? found + 1 : found - 1;
        if (target < 0 || target >= _itemsSource.Count)
        {
            return false;
        }

        _itemsSource.SelectedIndex = target;
        ScrollIntoView(target);
        _pendingTabFocusIndex = target;
        _pendingTabFocusAttempts = 0;
        SchedulePendingTabFocus();
        return true;
    }

    private void SchedulePendingTabFocus()
    {
        if (_pendingTabFocusIndex < 0)
        {
            return;
        }

        if (FindVisualRoot() is not Window window)
        {
            return;
        }

        window.ApplicationDispatcher?.Post(ApplyPendingTabFocus, UiDispatcherPriority.Render);
    }

    private void ApplyPendingTabFocus()
    {
        if (_pendingTabFocusIndex < 0)
        {
            return;
        }

        if (FindVisualRoot() is not Window window)
        {
            _pendingTabFocusIndex = -1;
            return;
        }

        FrameworkElement? container = null;
        _itemsHost.VisitRealized((i, element) =>
        {
            if (i == _pendingTabFocusIndex)
            {
                container = element;
            }
        });

        if (container == null)
        {
            if (_pendingTabFocusAttempts++ < 4)
            {
                window.ApplicationDispatcher?.Post(ApplyPendingTabFocus, UiDispatcherPriority.Render);
            }
            else
            {
                _pendingTabFocusIndex = -1;
            }
            return;
        }

        var target = FocusManager.FindFirstFocusable(container);
        if (target != null)
        {
            window.FocusManager.SetFocus(target);
        }

        _pendingTabFocusIndex = -1;
    }

    private static bool IsInSubtreeOf(UIElement element, UIElement root)
    {
        for (Element? current = element; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
    }

    private bool TryHitRow(Point position, out int index, out bool onGlyph)
    {
        index = -1;
        onGlyph = false;

        if (_vBar.IsVisible && GetLocalBounds(_vBar).Contains(position))
        {
            return false;
        }

        if (_hBar.IsVisible && GetLocalBounds(_hBar).Contains(position))
        {
            return false;
        }

        if (_itemsSource.Count == 0)
        {
            return false;
        }

        var bounds = GetSnappedBorderBounds(new Rect(0, 0, Bounds.Width, Bounds.Height));
        var dpiScale = GetDpi() / 96.0;
        var innerBounds = bounds.Deflate(new Thickness(GetBorderVisualInset()));
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(innerBounds.Deflate(Padding), dpiScale);

        double itemHeight = ResolveItemHeight();
        if (itemHeight <= 0)
        {
            return false;
        }

        if (!ItemsViewportMath.TryGetItemIndexAtY(
                position.Y,
                contentBounds.Y,
                _scroll.GetOffsetDip(1),
                itemHeight,
                _itemsSource.Count,
                out index))
        {
            return false;
        }

        double rowY = contentBounds.Y + index * itemHeight - _scroll.GetOffsetDip(1);
        double horizontalOffset = _scroll.GetOffsetDip(0);
        int depth = _itemsSource.GetDepth(index);
        var glyphRect = new Rect(contentBounds.X - horizontalOffset + depth * Indent, rowY, Indent, itemHeight);
        onGlyph = glyphRect.Contains(position);
        return true;
    }

    private double GetViewportHeightDip() => _viewportHeight <= 0 ? 0 : _viewportHeight;

    private double GetViewportWidthDip() => _viewportWidth <= 0 ? 0 : _viewportWidth;

    private double ResolveItemHeight()
    {
        if (!double.IsNaN(ItemHeight) && ItemHeight > 0)
        {
            return ItemHeight;
        }

        return Math.Max(Theme.Metrics.BaseControlHeight, 24);
    }

    private void SetSelectedNodeCore(TreeViewNode? node)
    {
        _itemsSource.SelectedItem = node;
    }

    private int IndexOfNode(TreeViewNode node)
    {
        int count = _itemsSource.Count;
        for (int i = 0; i < count; i++)
        {
            if (ReferenceEquals(_itemsSource.GetItem(i), node))
            {
                return i;
            }
        }

        return -1;
    }

    private static void DrawExpanderGlyph(IGraphicsContext context, Rect glyphRect, bool expanded, Color color)
    {
        // Match the ComboBox drop-down chevron style for visual consistency.
        var center = new Point(glyphRect.X + glyphRect.Width / 2, glyphRect.Y + glyphRect.Height / 2);
        double size = 4;
        Glyph.Draw(context, center, size, color, expanded ? GlyphKind.ChevronDown : GlyphKind.ChevronRight);
    }
}
