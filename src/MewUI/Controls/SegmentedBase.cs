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

    /// <summary>
    /// The host part of a template: a <see cref="Border"/> the control puts its segment strip into. The strip
    /// is the control's own working element, so a template without this part cannot show the segments.
    /// </summary>
    public const string PART_ITEMS_HOST = "PART_ItemsHost";

    private readonly IDataTemplate _defaultTemplate;
    private readonly List<TemplateContext> _contexts = new();

    private Panel _panel;
    private Border? _itemsHost;
    private SegmentSizing _sizing;
    private ISelectableItemsView _itemsSource = ItemsView.EmptySelectable;
    private IDataTemplate? _itemTemplate;
    private Action<SegmentButton, object?, int>? _prepareContainer;

    protected SegmentedBase(SegmentSizing defaultSizing)
    {
        _sizing = defaultSizing;
        _defaultTemplate = CreateDefaultItemTemplate();
        _panel = CreatePanel(_sizing);
        HostPanel();

        _itemsSource.SelectionChanged += OnItemsViewSelectionChangedCore;
        _itemsSource.Changed += OnItemsViewChangedCore;
    }

    /// <summary>
    /// Gets or sets the per-segment padding; the strip height follows the control, so use a small
    /// value for a compact, icon-only strip.
    /// </summary>
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

    /// <summary>Gets or sets the items source; the default is an empty selectable view.</summary>
    public ISelectableItemsView ItemsSource
    {
        get => _itemsSource;
        set => ApplyItemsSource(value);
    }

    /// <summary>Gets or sets the segment template; unset, a centered text template is used.</summary>
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

    private Panel CreatePanel(SegmentSizing sizing)
    {
        Panel panel = sizing == SegmentSizing.Uniform
            ? new UniformSegmentPanel { Rows = 1, Spacing = 0 }
            : new StackSegmentPanel { Orientation = Orientation.Horizontal, Spacing = 0 };
        ((ISegmentStrip)panel).ChromeSource = CreateChrome;
        return panel;
    }

    private void RebuildPanel()
    {
        _panel.Clear();
        UnhostPanel();

        _panel = CreatePanel(_sizing);
        HostPanel();

        RebuildSegments();
        OnSegmentsRebuilt();
    }

    private void HostPanel()
    {
        if (_itemsHost != null)
        {
            _itemsHost.Child = _panel;
        }
        else
        {
            _panel.Parent = this;
        }
    }

    private void UnhostPanel()
    {
        if (_itemsHost != null && ReferenceEquals(_itemsHost.Child, _panel))
        {
            _itemsHost.Child = null;
        }
        else
        {
            _panel.Parent = null;
        }
    }

    private protected override void OnTemplateInstanceAttached()
    {
        base.OnTemplateInstanceAttached();

        _itemsHost = GetTemplateChild<Border>(PART_ITEMS_HOST)
            ?? throw new InvalidOperationException(
                $"The {GetType().Name} template must register a {nameof(Border)} part named '{PART_ITEMS_HOST}'.");
        _panel.Parent = null;
        HostPanel();
    }

    private protected override void OnTemplateInstanceDetached()
    {
        base.OnTemplateInstanceDetached();

        if (_itemsHost == null)
        {
            return;
        }

        UnhostPanel();
        _itemsHost = null;
        HostPanel();
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
        if (HasTemplateInstance)
        {
            return base.MeasureContent(availableSize);
        }

        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        var inner = availableSize.Deflate(border).Deflate(Padding);
        _panel.Measure(inner);

        return _panel.DesiredSize.Inflate(Padding).Inflate(border);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (HasTemplateInstance)
        {
            base.ArrangeContent(bounds);
            return;
        }

        var borderInset = GetBorderVisualInset();
        var border = borderInset > 0 ? new Thickness(borderInset) : Thickness.Zero;

        var inner = bounds.Deflate(border).Deflate(Padding);
        _panel.Arrange(inner);
    }

    private SegmentStripChrome CreateChrome()
    {
        // Match the snapped inner edge of the container border (same formula as ListBox's clip radius)
        // so the rounded segment ends line up with the border at fractional DPI.
        var dpiScale = GetDpi() / 96.0;
        double borderInset = GetBorderVisualInset();
        var chrome = new SegmentStripChrome
        {
            SegmentRadius = Math.Max(0, LayoutRounding.RoundToPixel(CornerRadius, dpiScale) - borderInset),
            DividerColor = Theme.Palette.ControlBorder,
            DividerThickness = BorderThickness,
        };

        if (HasTemplateInstance)
        {
            // The host part frames the strip, so the end segments pin to the strip's own edges.
            chrome.PinToOwnEdges = true;
        }
        else
        {
            // Inner edges of the container's snapped border. Pin the first/last segment background to these so
            // the strip fills the frame; only when no container padding separates the strip from the border.
            var snapped = GetSnappedBorderBounds(Bounds);
            chrome.StripLeft = Padding.Left <= 0 ? snapped.Left + borderInset + Padding.Left : null;
            chrome.StripRight = Padding.Right <= 0 ? snapped.Right - borderInset - Padding.Right : null;
        }

        return chrome;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // A template owns the control's entire visuals; the frame and strip would double-render.
        if (HasTemplateInstance)
        {
            return;
        }

        var bounds = GetSnappedBorderBounds(Bounds);
        DrawBackgroundAndBorder(context, bounds, GetValue(BackgroundProperty), GetValue(BorderBrushProperty),
            BorderThickness, CornerRadius);

        // The strip paints its segments over the container background and its dividers on top of them.
        _panel.Render(context);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (HasTemplateInstance)
        {
            base.RenderSubtree(context);
        }

        // Without a template the strip is rendered inside OnRender, under the same recording as the frame.
    }

    internal override void WriteComposition(Rendering.Retained.CompositionPlanBuilder builder)
    {
        if (HasTemplateInstance)
        {
            base.WriteComposition(builder);
        }
        else
        {
            builder.Content(0);
        }
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
    {
        var templateRoot = TemplateVisualRoot;
        return templateRoot != null ? visitor(templateRoot) : visitor(_panel);
    }

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

                DefaultItemTemplates.SyncTextAndFont(tb, _itemsSource.GetText(index), this);
            });
}
