using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Shared base for the joined-segment strip controls (<see cref="SegmentedControl"/>,
/// <see cref="ButtonGroup"/>). Owns the content model (items + template + per-container prepare hook),
/// the segment containers (<see cref="SegmentButton"/>), the sizing panel, and the chrome: it paints
/// the outer rounded border and the dividers between segments, while each segment paints only its
/// background. This keeps the frame and dividers independent of any child's state and stable across
/// fractional DPI.
/// <para>
/// The base carries no selection. <see cref="SegmentedControl"/> adds exclusive single selection;
/// <see cref="ButtonGroup"/> leaves segments independent (per-segment click / toggle).
/// </para>
/// </summary>
public abstract class SegmentedBase : Control, IVisualTreeHost
{
    /// <summary>
    /// Gets or sets the per-segment padding (mirrors <see cref="ListBox.ItemPadding"/>). The strip
    /// height follows the control, so use a small value for a compact, icon-only strip. The
    /// container's <c>Padding</c> stays the outer padding.
    /// </summary>
    public static readonly MewProperty<Thickness> ItemPaddingProperty =
        MewProperty<Thickness>.Register<SegmentedBase>(nameof(ItemPadding), new Thickness(12, 4),
            MewPropertyOptions.AffectsLayout);

    private readonly IDataTemplate _defaultTemplate;
    private readonly List<TemplateContext> _contexts = new();

    private Panel _panel;
    private SegmentSizing _sizing;
    private ISelectableItemsView _itemsSource = ItemsView.EmptySelectable;
    private IDataTemplate? _itemTemplate;
    private Action<SegmentButton, object?, int>? _prepareContainer;

    protected SegmentedBase(SegmentSizing defaultSizing)
    {
        _sizing = defaultSizing;
        _defaultTemplate = CreateDefaultItemTemplate();
        _panel = CreatePanel(_sizing);
        _panel.Parent = this;

        _itemsSource.SelectionChanged += OnItemsViewSelectionChangedCore;
        _itemsSource.Changed += OnItemsViewChangedCore;
    }

    public Thickness ItemPadding
    {
        get => GetValue(ItemPaddingProperty);
        set => SetValue(ItemPaddingProperty, value);
    }

    /// <summary>Gets or sets how segments are sized along the horizontal axis.</summary>
    public SegmentSizing Sizing
    {
        get => _sizing;
        set
        {
            if (_sizing == value)
            {
                return;
            }

            _sizing = value;
            RebuildPanel();
        }
    }

    /// <summary>Gets or sets the items data source. Defaults to an empty selectable view.</summary>
    public ISelectableItemsView ItemsSource
    {
        get => _itemsSource;
        set => ApplyItemsSource(value);
    }

    /// <summary>Gets or sets the segment template. When unset a centered text template is used.</summary>
    public IDataTemplate? ItemTemplate
    {
        get => _itemTemplate;
        set
        {
            _itemTemplate = value;
            RebuildSegments();
        }
    }

    private IDataTemplate EffectiveTemplate => _itemTemplate ?? _defaultTemplate;

    // --- Protected surface for subclasses -------------------------------------------------------

    /// <summary>The items view (subclasses read selection / items through it).</summary>
    protected ISelectableItemsView Items => _itemsSource;

    /// <summary>Number of built segment containers.</summary>
    protected int SegmentCount => _panel.Count;

    /// <summary>The segment container at <paramref name="index"/>, or <see langword="null"/>.</summary>
    protected SegmentButton? SegmentAt(int index)
        => (uint)index < (uint)_panel.Count ? _panel[index] as SegmentButton : null;

    /// <summary>Whether the segment at <paramref name="index"/> is enabled (container-level).</summary>
    protected bool IsSegmentEnabled(int index)
        => SegmentAt(index) is not SegmentButton btn || btn.IsEnabled;

    /// <summary>Called when a segment is clicked. Base does nothing; selection controls override.</summary>
    protected virtual void OnSegmentClicked(int index) { }

    /// <summary>Called after the items view raises a selection change. Base does nothing.</summary>
    protected virtual void OnItemsViewSelectionChanged(int index) { }

    /// <summary>Called after segments are rebuilt (items or template change). Base does nothing.</summary>
    protected virtual void OnSegmentsRebuilt() { }

    /// <summary>Called for each freshly built segment container. Base does nothing.</summary>
    protected virtual void OnSegmentCreated(SegmentButton button) { }

    // --- Prepare hook ---------------------------------------------------------------------------

    /// <summary>
    /// Sets the container-prepare hook, invoked per segment after content bind with the container,
    /// item, and index. Re-applied on every rebuild. Use the typed <c>PrepareContainer</c> extension.
    /// </summary>
    internal void SetPrepareContainer(Action<SegmentButton, object?, int>? hook)
    {
        _prepareContainer = hook;
        if (hook == null)
        {
            return;
        }

        for (int i = 0; i < _panel.Count; i++)
        {
            if (_panel[i] is SegmentButton btn)
            {
                hook(btn, _itemsSource.GetItem(i), i);
            }
        }
        InvalidateVisual();
    }

    // --- Items / panel plumbing -----------------------------------------------------------------

    private void ApplyItemsSource(ISelectableItemsView? value)
    {
        value ??= ItemsView.EmptySelectable;
        if (ReferenceEquals(_itemsSource, value))
        {
            return;
        }

        _itemsSource.SelectionChanged -= OnItemsViewSelectionChangedCore;
        _itemsSource.Changed -= OnItemsViewChangedCore;

        _itemsSource = value;
        _itemsSource.SelectionChanged += OnItemsViewSelectionChangedCore;
        _itemsSource.Changed += OnItemsViewChangedCore;

        RebuildSegments();
        OnSegmentsRebuilt();

        InvalidateMeasure();
        InvalidateVisual();
    }

    private static Panel CreatePanel(SegmentSizing sizing)
        => sizing == SegmentSizing.Uniform
            ? new UniformGrid { Rows = 1, Spacing = 0 }
            : new StackPanel { Orientation = Orientation.Horizontal, Spacing = 0 };

    private void RebuildPanel()
    {
        _panel.Clear();
        _panel.Parent = null;

        _panel = CreatePanel(_sizing);
        _panel.Parent = this;

        RebuildSegments();
        OnSegmentsRebuilt();
    }

    /// <summary>Rebuilds every segment container from the items view.</summary>
    protected void RebuildSegments()
    {
        ResetContexts();
        _panel.Clear();

        int count = _itemsSource.Count;
        if (_panel is UniformGrid grid)
        {
            grid.Columns = count;
        }

        for (int i = 0; i < count; i++)
        {
            var item = _itemsSource.GetItem(i);
            var button = new SegmentButton
            {
                Index = i,
                IsFirst = i == 0,
                IsLast = i == count - 1,
                Content = BuildSegmentContent(i),
                ClickedCallback = OnSegmentClicked,
            };

            // Bind so segment padding tracks ItemPadding live.
            button.SetBinding(Control.PaddingProperty, this, ItemPaddingProperty);

            _panel.Add(button);

            OnSegmentCreated(button);

            // Container-level configuration (click, checked, enabled, tooltip) via the prepare hook.
            _prepareContainer?.Invoke(button, item, i);
        }

        InvalidateMeasure();
        InvalidateVisual();
    }

    private FrameworkElement BuildSegmentContent(int index)
    {
        var template = EffectiveTemplate;
        var ctx = new TemplateContext();
        var view = template.Build(ctx);
        ctx.BindTemplate(view, template, _itemsSource.GetItem(index), index);
        _contexts.Add(ctx);
        return view;
    }

    private void ResetContexts()
    {
        for (int i = 0; i < _contexts.Count; i++)
        {
            _contexts[i].Dispose();
        }
        _contexts.Clear();
    }

    private void OnItemsViewSelectionChangedCore(int index) => OnItemsViewSelectionChanged(index);

    private void OnItemsViewChangedCore(ItemsChange change)
    {
        RebuildSegments();
        OnSegmentsRebuilt();
        InvalidateMeasure();
        InvalidateVisual();
    }

    // --- Layout / render / hit-test -------------------------------------------------------------

    protected override Size MeasureContent(Size availableSize)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        var inner = availableSize.Deflate(border).Deflate(Padding);
        _panel.Measure(inner);

        return _panel.DesiredSize.Inflate(Padding).Inflate(border);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        var inner = bounds.Deflate(border).Deflate(Padding);
        _panel.Arrange(inner);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = GetSnappedBorderBounds(Bounds);

        // Match the snapped inner edge of the container border (same formula as ListBox's clip radius)
        // so the rounded segment ends line up with the border at fractional DPI.
        var dpiScale = GetDpi() / 96.0;
        double borderInset = GetBorderVisualInset();
        double segmentRadius = Math.Max(0, LayoutRounding.RoundToPixel(CornerRadius, dpiScale) - borderInset);

        // Inner edges of the container's snapped border. Pin the first/last segment background to these so
        // the strip fills the frame; only when no container padding separates the strip from the border.
        double innerLeft = bounds.Left + borderInset + Padding.Left;
        double innerRight = bounds.Right - borderInset - Padding.Right;
        for (int i = 0; i < _panel.Count; i++)
        {
            if (_panel[i] is SegmentButton btn)
            {
                btn.Radius = segmentRadius;
                btn.StripLeft = btn.IsFirst && Padding.Left <= 0 ? innerLeft : null;
                btn.StripRight = btn.IsLast && Padding.Right <= 0 ? innerRight : null;
            }
        }

        DrawBackgroundAndBorder(context, bounds, GetValue(BackgroundProperty), GetValue(BorderBrushProperty),
            BorderThickness, CornerRadius);

        // Segments paint over the container background, then dividers are drawn on top.
        _panel.Render(context);
        DrawDividers(context);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        // The segment panel is rendered inside OnRender so dividers can be layered above it.
    }

    private void DrawDividers(IGraphicsContext context)
    {
        if (_panel.Count < 2 || BorderThickness <= 0)
        {
            return;
        }

        var color = Theme.Palette.ControlBorder;

        // Draw each internal boundary at the RIGHT segment's left edge. Per-child layout rounding
        // rounds X and Width independently, so at fractional cell widths (3+ items) a segment's
        // snapped right edge can drift 1px from its neighbor's snapped left edge. Segments render
        // in order (later on top), so the visible seam is the right neighbor's left edge - snap it
        // the same way the segment background does so the divider lands exactly on the seam.
        for (int i = 1; i < _panel.Count; i++)
        {
            if (_panel[i] is not UIElement right)
            {
                continue;
            }

            var b = GetSnappedBorderBounds(right.Bounds);
            if (b.Width <= 0 || b.Height <= 0)
            {
                continue;
            }

            double x = b.Left;
            context.DrawLine(new Point(x, b.Top), new Point(x, b.Bottom), color, BorderThickness, true);
        }
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => visitor(_panel);

    protected override void OnDispose()
    {
        _itemsSource.SelectionChanged -= OnItemsViewSelectionChangedCore;
        _itemsSource.Changed -= OnItemsViewChangedCore;
        ResetContexts();
        base.OnDispose();
    }

    private IDataTemplate CreateDefaultItemTemplate()
        => new DelegateTemplate<object?>(
            build: _ => new TextBlock
            {
                IsHitTestVisible = false,
                TextWrapping = TextWrapping.NoWrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                VerticalTextAlignment = TextAlignment.Center,
            },
            bind: (view, _, index, _) =>
            {
                var tb = (TextBlock)view;

                var text = _itemsSource.GetText(index);
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
            });
}
