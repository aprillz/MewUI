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
    private readonly FixedHeightItemsPresenter _presenter;
    private readonly ScrollViewer _scrollViewer;
    private bool _rebindVisibleOnNextRender = true;
    private ITreeItemsView _itemsSource = TreeItemsView.Empty;
    private TreeViewNode? _selectedNode;
    private int _hoverVisibleIndex = -1;
    private bool _hasLastMousePosition;
    private Point _lastMousePosition;
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

            _presenter.ItemsSource = _itemsSource;
            _presenter.RecycleAll();
            _rebindVisibleOnNextRender = true;

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
        get => _presenter.ItemTemplate;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _presenter.ItemTemplate = value;
            _rebindVisibleOnNextRender = true;
            InvalidateMeasure();
            InvalidateVisual();
        }
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

        _itemsSource.Changed += OnItemsChanged;
        _itemsSource.SelectionChanged += OnItemsSelectionChanged;

        _presenter = new FixedHeightItemsPresenter
        {
            ItemsSource = _itemsSource,
            ItemTemplate = CreateDefaultItemTemplate(),
            BeforeItemRender = OnBeforeItemRender,
            GetContainerRect = OnGetContainerRect,
            ItemHeight = ResolveItemHeight(),
            RebindExisting = true,
        };

        _scrollViewer = new ScrollViewer
        {
            VerticalScroll = ScrollMode.Auto,
            HorizontalScroll = ScrollMode.Auto,
            BorderThickness = 0,
            Background = default,
            Padding = Padding,
            Content = _presenter,
        };
        _scrollViewer.Parent = this;
        _scrollViewer.ScrollChanged += OnScrollViewerChanged;
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
        double itemRadius = _presenter.ItemRadius;

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
        double indentX = itemRect.X + depth * Indent + ItemPadding.Left;
        var glyphRect = new Rect(indentX, itemRect.Y, Indent, itemRect.Height);
        var textColor = selected ? Theme.Palette.SelectionText : (IsEffectivelyEnabled ? Foreground : Theme.Palette.DisabledText);
        if (_itemsSource.GetHasChildren(i))
        {
            DrawExpanderGlyph(context, glyphRect, _itemsSource.GetIsExpanded(i), textColor);
        }
    }

    private void OnItemsChanged(ItemsChange change)
    {
        _presenter.RecycleAll();
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
        double oldOffset = _scrollViewer.VerticalOffset;
        double newOffset = ItemsViewportMath.ComputeScrollOffsetToBringItemIntoView(index, itemHeight, viewport, oldOffset);
        if (newOffset.Equals(oldOffset))
        {
            return;
        }
        _scrollViewer.SetScrollOffsets(_scrollViewer.HorizontalOffset, newOffset);
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

        return TryHitRow(position, out index, out _);
    }

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
        visitor(_scrollViewer);
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

        _presenter.ItemHeight = itemHeight;
        _presenter.ExtentWidth = extentWidth;
        _scrollViewer.Padding = Padding;

        // Let ScrollViewer compute bar visibility/metrics for the current slot.
        _scrollViewer.Measure(new Size(
            double.IsPositiveInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - borderInset * 2),
            double.IsPositiveInfinity(availableSize.Height) ? double.PositiveInfinity : Math.Max(0, availableSize.Height - borderInset * 2)));

        double desiredHeight = double.IsPositiveInfinity(availableSize.Height)
            ? height
            : Math.Min(height, LayoutRounding.RoundToPixel(Math.Max(0, availableSize.Height - Padding.VerticalThickness - borderInset * 2), dpiScale));

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
        _scrollViewer.Arrange(innerBounds);

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
        double radius = Theme.Metrics.ControlCornerRadius;
        var borderInset = GetBorderVisualInset();

        var state = GetVisualState();
        var bg = PickControlBackground(state);
        var borderColor = PickAccentBorder(Theme, BorderBrush, state, 0.6);
        DrawBackgroundAndBorder(context, bounds, bg, borderColor, radius);

        if (_itemsSource.Count == 0)
        {
            return;
        }

        _scrollViewer.ViewportCornerRadius = Math.Max(0, radius - borderInset);
        _presenter.ItemRadius = Math.Max(0, radius - borderInset);

        _presenter.RebindExisting = _rebindVisibleOnNextRender;
        _rebindVisibleOnNextRender = false;

        _scrollViewer.Render(context);
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
        var hit = _scrollViewer.HitTest(point);
        if (hit != null)
        {
            return hit;
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
    }

    private void ReevaluateMouseOverAfterScroll()
    {
        if (FindVisualRoot() is Window window)
        {
            window.ReevaluateMouseOver();
        }
    }

    private void OnScrollViewerChanged()
    {
        if (_hasLastMousePosition && TryHitRow(_lastMousePosition, out int hover, out _))
        {
            _hoverVisibleIndex = hover;
        }
        else
        {
            _hoverVisibleIndex = -1;
        }

        InvalidateVisual();
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
        _presenter.VisitRealized((i, element) =>
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
        _presenter.VisitRealized((i, element) =>
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
        _presenter.VisitRealized((i, element) =>
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

        if (_itemsSource.Count == 0)
        {
            return false;
        }

        // Don't treat scrollbar interaction as row hit/activation.
        if (_scrollViewer.HitTest(position) is ScrollBar)
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
                _scrollViewer.VerticalOffset,
                itemHeight,
                _itemsSource.Count,
                out index))
        {
            return false;
        }

        double rowY = contentBounds.Y + index * itemHeight - _scrollViewer.VerticalOffset;
        double horizontalOffset = _scrollViewer.HorizontalOffset;
        int depth = _itemsSource.GetDepth(index);
        var glyphRect = new Rect(contentBounds.X - horizontalOffset + depth * Indent, rowY, Indent, itemHeight);
        onGlyph = glyphRect.Contains(position);
        return true;
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

        return 0;
    }

    private double GetViewportWidthDip()
    {
        if (Bounds.Width > 0 && Bounds.Height > 0)
        {
            var snapped = GetSnappedBorderBounds(Bounds);
            var borderInset = GetBorderVisualInset();
            var innerBounds = snapped.Deflate(new Thickness(borderInset));
            var dpiScale = GetDpi() / 96.0;
            return LayoutRounding.RoundToPixel(Math.Max(0, innerBounds.Width - Padding.HorizontalThickness), dpiScale);
        }

        return 0;
    }

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
