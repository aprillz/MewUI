using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A scrollable items host without built-in selection semantics.
/// </summary>
public sealed class ItemsControl : Control, IVisualTreeHost
{
    private readonly TextWidthCache _textWidthCache = new(512);
    private readonly TemplatedItemsHost _itemsHost;
    private int _hoverIndex = -1;
    private bool _hasLastMousePosition;
    private Point _lastMousePosition;
    private bool _rebindVisibleOnNextRender = true;
    private IItemsView _itemsSource = ItemsView.Empty;
    private readonly ScrollBar _vBar;
    private readonly ScrollController _scroll = new();
    private double _extentHeight;
    private double _viewportHeight;
    private ScrollIntoViewRequest _scrollIntoViewRequest;

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

            _itemsSource.Changed -= OnItemsChanged;
            _itemsSource = value;
            _itemsSource.Changed += OnItemsChanged;

            _hoverIndex = -1;
            _rebindVisibleOnNextRender = true;

            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the height of each item (in DIPs). Use NaN to use theme default.
    /// </summary>
    public double ItemHeight
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
                InvalidateArrange();
                InvalidateVisual();
            }
        }
    } = double.NaN;

    /// <summary>
    /// Gets or sets the padding for each item.
    /// </summary>
    public Thickness ItemPadding
    {
        get;
        set
        {
            if (Set(ref field, value))
            {
                InvalidateMeasure();
                InvalidateVisual();
            }
        }
    } = ThemeMetrics.Default.ItemPadding;

    /// <summary>
    /// Gets or sets the item template. When not set, a default label template is used.
    /// </summary>
    public IDataTemplate ItemTemplate
    {
        get => _itemsHost.ItemTemplate;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _itemsHost.ItemTemplate = value;
            _rebindVisibleOnNextRender = true;
            InvalidateMeasure();
            InvalidateArrange();
            InvalidateVisual();
        }
    }

    public ItemsControl()
    {
        var template = CreateDefaultItemTemplate();

        _itemsHost = new TemplatedItemsHost(
            owner: this,
            getItem: index => index >= 0 && index < ItemsSource.Count ? ItemsSource.GetItem(index) : null,
            invalidateMeasureAndVisual: () => { InvalidateMeasure(); InvalidateArrange(); InvalidateVisual(); },
            template: template);

        _itemsHost.Options = new TemplatedItemsHost.ItemsRangeOptions
        {
            BeforeItemRender = OnBeforeItemRender
        };

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
            ReevaluateMouseOverAfterScroll();
        };
    }

    private void OnItemsChanged(ItemsChange _)
    {
        _rebindVisibleOnNextRender = true;
        InvalidateMeasure();
        InvalidateArrange();
        InvalidateVisual();
    }

    private void OnBeforeItemRender(IGraphicsContext context, int i, Rect itemRect)
    {
        double radius = _itemsHost.Layout.ItemRadius;

        if (i == _hoverIndex && IsEffectivelyEnabled)
        {
            var hoverBg = Theme.Palette.ControlBackground.Lerp(Theme.Palette.Accent, 0.10);
            if (radius > 0)
            {
                context.FillRoundedRectangle(itemRect, radius, radius, hoverBg);
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
            {
                var label = new Label();
                return label;
            },
            bind: (view, item, index, _) =>
            {
                if (view is not Label label)
                {
                    return;
                }

                label.Text = ItemsSource.GetText(index);
                label.Padding = ItemPadding;
                label.VerticalTextAlignment = TextAlignment.Center;
            });

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

        if (HorizontalAlignment == HorizontalAlignment.Stretch && !double.IsPositiveInfinity(widthLimit))
        {
            maxWidth = widthLimit;
        }
        else
        {
            using var measure = BeginTextMeasurement();

            maxWidth = 0;
            if (count > 0 && widthLimit > 0)
            {
                double itemPadW = ItemPadding.HorizontalThickness;

                // Reuse the same cheap estimation approach as ListBox.
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

                    for (int i = 0; i < sampleCount; i++)
                    {
                        var text = ItemsSource.GetText(i);
                        if (string.IsNullOrEmpty(text))
                        {
                            continue;
                        }

                        maxWidth = Math.Max(maxWidth, _textWidthCache.GetOrMeasure(measure.Context, measure.Font, dpi, text) + itemPadW);
                        if (maxWidth >= widthLimit)
                        {
                            maxWidth = widthLimit;
                            break;
                        }
                    }
                }
                else
                {
                    _textWidthCache.SetCapacity(Math.Clamp(count, 64, 4096));
                    for (int i = 0; i < count; i++)
                    {
                        var text = ItemsSource.GetText(i);
                        if (string.IsNullOrEmpty(text))
                        {
                            continue;
                        }

                        maxWidth = Math.Max(maxWidth, _textWidthCache.GetOrMeasure(measure.Context, measure.Font, dpi, text) + itemPadW);
                        if (maxWidth >= widthLimit)
                        {
                            maxWidth = widthLimit;
                            break;
                        }
                    }
                }
            }

            maxWidth = Math.Min(maxWidth, widthLimit);
        }

        double itemHeight = ResolveItemHeight();
        _extentHeight = count == 0 || itemHeight <= 0 ? 0 : count * itemHeight;

        double desiredHeight;
        if (double.IsPositiveInfinity(availableSize.Height))
        {
            desiredHeight = count == 0 || itemHeight <= 0 ? 0 : Math.Min(count * itemHeight, itemHeight * 12);
        }
        else
        {
            desiredHeight = Math.Max(0, availableSize.Height - Padding.VerticalThickness - borderInset * 2);
        }

        return new Size(
            Math.Max(0, maxWidth + Padding.HorizontalThickness + borderInset * 2),
            Math.Max(0, desiredHeight + Padding.VerticalThickness + borderInset * 2));
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
        var dpiScale = GetDpi() / 96.0;
        double radius = Theme.Metrics.ControlCornerRadius;
        var borderInset = GetBorderVisualInset();
        double itemRadius = Math.Max(0, radius - borderInset);

        var state = GetVisualState();
        var bg = PickControlBackground(state);
        var borderColor = PickAccentBorder(Theme, BorderBrush, state, 0.6);
        DrawBackgroundAndBorder(context, bounds, bg, borderColor, radius);

        if (ItemsSource.Count == 0)
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
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        if (_vBar.IsVisible && _vBar.Bounds.Contains(point))
        {
            return _vBar;
        }

        return base.OnHitTest(point);
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

        if (_hasLastMousePosition && TryGetItemIndexAtCore(_lastMousePosition, out int hover))
        {
            _hoverIndex = hover;
        }
        else
        {
            _hoverIndex = -1;
        }

        InvalidateVisual();
        ReevaluateMouseOverAfterScroll();
        e.Handled = true;
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

    private bool TryGetItemIndexAtCore(Point position, out int index)
    {
        index = -1;

        int count = ItemsSource.Count;
        if (count <= 0)
        {
            return false;
        }

        var bounds = GetSnappedBorderBounds(Bounds);
        var dpiScale = GetDpi() / 96.0;
        var borderInset = GetBorderVisualInset();
        var innerBounds = bounds.Deflate(new Thickness(borderInset));
        var contentBounds = LayoutRounding.SnapViewportRectToPixels(innerBounds.Deflate(Padding), dpiScale);

        if (!contentBounds.Contains(position))
        {
            return false;
        }

        double itemHeight = ResolveItemHeight();
        if (itemHeight <= 0 || double.IsNaN(itemHeight) || double.IsInfinity(itemHeight))
        {
            return false;
        }

        double y = position.Y - contentBounds.Y + _scroll.GetOffsetDip(1);
        int i = (int)Math.Floor(y / itemHeight);
        if (i < 0 || i >= count)
        {
            return false;
        }

        index = i;
        return true;
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

        double itemHeight = ResolveItemHeight();
        if (itemHeight <= 0)
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
        ReevaluateMouseOverAfterScroll();
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
            var borderInset = GetBorderVisualInset();
            return Math.Max(0, Bounds.Height - Padding.VerticalThickness - borderInset * 2);
        }

        return 0;
    }

    private void ReevaluateMouseOverAfterScroll()
    {
        if (FindVisualRoot() is Window window)
        {
            Application.Current.Dispatcher?.Post(
                () => window.ReevaluateMouseOver(),
                UiDispatcherPriority.Render);
        }
    }
}
