using System.Runtime.CompilerServices;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A hierarchical tree view control with expand/collapse functionality.
/// </summary>
public sealed class TreeView : Control, IVisualTreeHost
{
    private readonly TextWidthCache _textWidthCache = new(512);
    private readonly ScrollBar _vBar;
    private readonly ScrollController _scroll = new();
    private readonly TemplatedItemsHost _itemsHost;
    private bool _rebindVisibleOnNextRender = true;
    private ITreeItemsView _itemsSource = TreeItemsView.Empty;
    private TreeViewNode? _selectedNode;
    private int _hoverVisibleIndex = -1;
    private bool _hasLastMousePosition;
    private Point _lastMousePosition;

    private double _extentHeight;
    private double _viewportHeight;

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
    /// Initializes a new instance of the TreeView class.
    /// </summary>
    public TreeView()
    {
        BorderThickness = 1;
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
        var textColor = selected ? Theme.Palette.SelectionText : (IsEnabled ? Foreground : Theme.Palette.DisabledText);
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

        double maxWidth;
        if (HorizontalAlignment == HorizontalAlignment.Stretch && !double.IsPositiveInfinity(widthLimit))
        {
            maxWidth = widthLimit;
        }
        else
        {
            using var measure = BeginTextMeasurement();
            maxWidth = 0;

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
                maxWidth = Math.Max(maxWidth, indentW + _textWidthCache.GetOrMeasure(measure.Context, measure.Font, dpi, text) + padW);
                if (maxWidth >= widthLimit)
                {
                    maxWidth = widthLimit;
                    break;
                }
            }
        }

        double itemHeight = ResolveItemHeight();
        double height = _itemsSource.Count * itemHeight;

        _extentHeight = height;
        _viewportHeight = double.IsPositiveInfinity(availableSize.Height)
            ? height
            : LayoutRounding.RoundToPixel(Math.Max(0, availableSize.Height - Padding.VerticalThickness - borderInset * 2), dpiScale);

        _scroll.DpiScale = dpiScale;
        _scroll.SetMetricsDip(1, _extentHeight, _viewportHeight);

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

        if (_itemsSource.Count == 0)
        {
            return;
        }

        var innerBounds = bounds.Deflate(new Thickness(borderInset));
        var viewportBounds = innerBounds;
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(viewportBounds.Deflate(Padding), dpiScale);

        context.Save();
        context.SetClip(LayoutRounding.MakeClipRect(contentBounds, dpiScale));

        double itemHeight = ResolveItemHeight();
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

                if (tb.IsEnabled != IsEnabled)
                {
                    tb.IsEnabled = IsEnabled;
                }

                bool selected = index == _itemsSource.SelectedIndex;
                var fg = selected ? Theme.Palette.SelectionText : (IsEnabled ? Foreground : Theme.Palette.DisabledText);
                if (tb.Foreground != fg)
                {
                    tb.Foreground = fg;
                }
            });

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

        if (!TryHitRow(e.Position, out int index, out bool onGlyph))
        {
            return;
        }

        _itemsSource.SelectedIndex = index;
        if (onGlyph && _itemsSource.GetHasChildren(index))
        {
            _itemsSource.SetIsExpanded(index, !_itemsSource.GetIsExpanded(index));
        }

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

        if (_hasLastMousePosition && TryHitRow(_lastMousePosition, out int hover, out _))
        {
            _hoverVisibleIndex = hover;
        }
        else
        {
            _hoverVisibleIndex = -1;
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
        if (TryHitRow(e.Position, out int index, out _))
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

    private bool TryHitRow(Point position, out int index, out bool onGlyph)
    {
        index = -1;
        onGlyph = false;

        if (_vBar.IsVisible && _vBar.Bounds.Contains(position))
        {
            return false;
        }

        if (_itemsSource.Count == 0)
        {
            return false;
        }

        var bounds = GetSnappedBorderBounds(Bounds);
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
        int depth = _itemsSource.GetDepth(index);
        var glyphRect = new Rect(contentBounds.X + depth * Indent, rowY, Indent, itemHeight);
        onGlyph = glyphRect.Contains(position);
        return true;
    }

    private double GetViewportHeightDip() => _viewportHeight <= 0 ? 0 : _viewportHeight;

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
        ChevronGlyph.Draw(context, center, size, color, expanded ? ChevronDirection.Down : ChevronDirection.Right);
    }
}
