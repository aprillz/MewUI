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
    private IItemsPresenter _presenter;
    private IDataTemplate _itemTemplate;
    private readonly ScrollViewer _scrollViewer;

    private int _hoverIndex = -1;
    private bool _hasLastMousePosition;
    private Point _lastMousePosition;

    private IItemsView _itemsSource = ItemsView.Empty;
    private ScrollIntoViewRequest _scrollIntoViewRequest;
    private ItemsPresenterMode _presenterMode = ItemsPresenterMode.Fixed;

    /// <summary>
    /// Gets the current vertical scroll offset in DIPs.
    /// </summary>
    public double VerticalOffset => _scrollViewer.VerticalOffset;

    /// <summary>
    /// Gets the current horizontal scroll offset in DIPs.
    /// </summary>
    public double HorizontalOffset => _scrollViewer.HorizontalOffset;

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

            _presenter.ItemsSource = _itemsSource;

            _hoverIndex = -1;
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
        get => _itemTemplate;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _itemTemplate = value;
            _presenter.ItemTemplate = value;
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

    public ItemsControl()
    {
        _itemsSource.Changed += OnItemsChanged;

        _scrollViewer = new ScrollViewer
        {
            VerticalScroll = ScrollMode.Auto,
            HorizontalScroll = ScrollMode.Disabled,
            BorderThickness = 0,
            Background = default,
            Padding = Padding,
            Content = null,
        };
        _scrollViewer.Parent = this;
        _scrollViewer.ScrollChanged += OnScrollViewerChanged;

        _itemTemplate = CreateDefaultItemTemplate();
        _presenter = CreatePresenter(PresenterMode);
        _scrollViewer.Content = (UIElement)_presenter;
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
        visitor(_scrollViewer);
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

            // Variable-height presenter is usually used with wrapping content (chat/messages).
            // In that case, using the max measured text width as horizontal extent causes
            // the presenter to lay out against an oversized width, pushing right-aligned
            // content outside the actual viewport.
            if (PresenterMode == ItemsPresenterMode.Variable && !double.IsPositiveInfinity(widthLimit))
            {
                maxWidth = widthLimit;
            }
        }

        double itemHeight = ResolveItemHeight();
        _presenter.ItemHeightHint = itemHeight;
        _presenter.ExtentWidth = maxWidth;
        _scrollViewer.Padding = Padding;

        // Let ScrollViewer update bar visibility/metrics for the current slot.
        var childAvailable = new Size(
            double.IsPositiveInfinity(availableSize.Width) ? double.PositiveInfinity : Math.Max(0, availableSize.Width - borderInset * 2),
            double.IsPositiveInfinity(availableSize.Height) ? double.PositiveInfinity : Math.Max(0, availableSize.Height - borderInset * 2));
        _scrollViewer.Measure(childAvailable);

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
        var borderInset = GetBorderVisualInset();

        var state = GetVisualState();
        var bg = PickControlBackground(state);
        var borderColor = PickAccentBorder(Theme, BorderBrush, state, 0.6);
        DrawBackgroundAndBorder(context, bounds, bg, borderColor, radius);

        _scrollViewer.ViewportCornerRadius = Math.Max(0, radius - borderInset);
        _scrollViewer.Render(context);
    }

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible)
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

    /// <summary>
    /// Sets both scroll offsets simultaneously.
    /// </summary>
    /// <param name="horizontalOffset">The horizontal offset in DIPs.</param>
    /// <param name="verticalOffset">The vertical offset in DIPs.</param>
    public void SetScrollOffsets(double horizontalOffset, double verticalOffset)
        => _scrollViewer.SetScrollOffsets(horizontalOffset, verticalOffset);

    private void OnItemsChanged(ItemsChange _)
    {
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void OnScrollViewerChanged()
    {
        if (_hasLastMousePosition && TryGetItemIndexAtCore(_lastMousePosition, out int idx))
        {
            _hoverIndex = idx;
        }
        else
        {
            _hoverIndex = -1;
        }

        InvalidateVisual();
    }

    private void OnBeforeItemRender(IGraphicsContext context, int i, Rect itemRect)
    {
        if (i == _hoverIndex && IsEffectivelyEnabled)
        {
            var hoverBg = Theme.Palette.ControlBackground.Lerp(Theme.Palette.Accent, 0.10);
            context.FillRectangle(itemRect, hoverBg);
        }
    }

    private IDataTemplate CreateDefaultItemTemplate()
        => new DelegateTemplate<object?>(
            build: _ => new Label(),
            bind: (view, _, index, _) =>
            {
                if (view is not Label label)
                {
                    return;
                }

                label.Text = ItemsSource.GetText(index);
                label.Padding = ItemPadding;
                label.VerticalTextAlignment = TextAlignment.Center;
            });

    private bool TryGetItemIndexAtCore(Point position, out int index)
    {
        index = -1;

        int count = ItemsSource.Count;
        if (count <= 0)
        {
            return false;
        }

        // Map to presenter-local coordinates to avoid duplicating ScrollViewer viewport math
        // (padding/scrollbar visibility/snapping) which can drift at fractional DPI.
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

        // Map presenter-local Y to content-space Y and ask the presenter to resolve index.
        // Keep rounding consistent with presenters (they align offsets to device pixels).
        double alignedLocalY = LayoutRounding.RoundToPixel(local.Y, dpiScale);
        double alignedOffsetY = LayoutRounding.RoundToPixel(_scrollViewer.VerticalOffset, dpiScale);
        double yContent = alignedLocalY + alignedOffsetY;
        return _presenter.TryGetItemIndexAtY(yContent, out index);
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

    protected override void OnDispose()
    {
        if (_scrollViewer is IDisposable dsv)
        {
            dsv.Dispose();
        }

        _presenter.OffsetCorrectionRequested -= OnPresenterOffsetCorrectionRequested;
        if (_presenter is IDisposable dp)
        {
            dp.Dispose();
        }
    }

    private IItemsPresenter CreatePresenter(ItemsPresenterMode mode)
    {
        IItemsPresenter presenter = mode == ItemsPresenterMode.Variable
            ? new VariableHeightItemsPresenter()
            : new FixedHeightItemsPresenter();

        presenter.ItemsSource = _itemsSource;
        presenter.ItemTemplate = _itemTemplate;
        presenter.BeforeItemRender = OnBeforeItemRender;
        presenter.GetContainerRect = null;
        presenter.ItemHeightHint = ResolveItemHeight();
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

        if (preserveScrollOffsets)
        {
            _scrollViewer.SetScrollOffsets(oldX, oldY);
        }
    }

    private void OnPresenterOffsetCorrectionRequested(Point offset)
    {
        _scrollViewer.SetScrollOffsets(_scrollViewer.HorizontalOffset, offset.Y);
    }
}
