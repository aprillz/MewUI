using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Styling;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for all controls.
/// </summary>
public abstract class Control : FrameworkElement
{
    #region MewProperty Declarations

    /// <summary>Background color property.</summary>
    public static readonly MewProperty<Color> BackgroundProperty =
        MewProperty<Color>.Register<Control>(nameof(Background), Color.Transparent, MewPropertyOptions.AffectsRender);

    /// <summary>Border color property.</summary>
    public static readonly MewProperty<Color> BorderBrushProperty =
        MewProperty<Color>.Register<Control>(nameof(BorderBrush), Color.Transparent, MewPropertyOptions.AffectsRender);

    /// <summary>Foreground (text) color property with inheritance support.</summary>
    public static readonly MewProperty<Color> ForegroundProperty =
        MewProperty<Color>.Register<Control>(nameof(Foreground), Color.Black,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.Inherits);

    /// <summary>Font family property with inheritance support.</summary>
    public static readonly MewProperty<string> FontFamilyProperty =
        MewProperty<string>.Register<Control>(nameof(FontFamily), "Segoe UI",
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.Inherits);

    /// <summary>Font size property with inheritance support.</summary>
    public static readonly MewProperty<double> FontSizeProperty =
        MewProperty<double>.Register<Control>(nameof(FontSize), 12.0,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.Inherits);

    /// <summary>Font weight property with inheritance support.</summary>
    public static readonly MewProperty<FontWeight> FontWeightProperty =
        MewProperty<FontWeight>.Register<Control>(nameof(FontWeight), FontWeight.Normal,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.Inherits);

    /// <summary>Corner radius for background/border rendering.</summary>
    public static readonly MewProperty<double> CornerRadiusProperty =
        MewProperty<double>.Register<Control>(nameof(CornerRadius), 0.0, MewPropertyOptions.AffectsRender);

    /// <summary>Border thickness property.</summary>
    public static readonly MewProperty<double> BorderThicknessProperty =
        MewProperty<double>.Register<Control>(nameof(BorderThickness), 0.0,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender);

    /// <summary>Inner padding property.</summary>
    public static readonly MewProperty<Thickness> PaddingProperty =
        MewProperty<Thickness>.Register<Control>(nameof(Padding), default, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<string?> ToolTipTextProperty =
        MewProperty<string?>.Register<Control>(nameof(ToolTipText), null, MewPropertyOptions.None);

    public static readonly MewProperty<Element?> ToolTipContentProperty =
        MewProperty<Element?>.Register<Control>(nameof(ToolTipContent), null, MewPropertyOptions.None);

    public static readonly MewProperty<ContextMenu?> ContextMenuProperty =
        MewProperty<ContextMenu?>.Register<Control>(nameof(ContextMenu), null, MewPropertyOptions.None);

    #endregion

    private IFont? _font;
    private uint _fontDpi;
    private Point _lastMousePositionInWindow;

    // VisualState system fields
    private VisualState _visualState;

    private bool _isPressed;
    private bool _forceApplyStyle;
    private bool _styleResolved;

    private Style? _style;
    private string? _styleName;
    private Dictionary<string, UIElement>? _parts;

    /// <summary>
    /// Gets or sets the tooltip text for this control.
    /// </summary>
    public string? ToolTipText
    {
        get => GetValue(ToolTipTextProperty);
        set => SetValue(ToolTipTextProperty, value);
    }

    /// <summary>
    /// Gets or sets the tooltip content for this control.
    /// When set, this takes precedence over <see cref="ToolTipText"/>.
    /// </summary>
    public Element? ToolTipContent
    {
        get => GetValue(ToolTipContentProperty);
        set => SetValue(ToolTipContentProperty, value);
    }

    /// <summary>
    /// Gets or sets the context menu for this control.
    /// </summary>
    public ContextMenu? ContextMenu
    {
        get => GetValue(ContextMenuProperty);
        set => SetValue(ContextMenuProperty, value);
    }

    /// <summary>
    /// Gets or sets the background color.
    /// </summary>
    public Color Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the foreground (text) color.
    /// </summary>
    public Color Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <summary>
    /// Gets or sets the border color.
    /// </summary>
    public Color BorderBrush
    {
        get => GetValue(BorderBrushProperty);
        set => SetValue(BorderBrushProperty, value);
    }

    /// <summary>
    /// Gets or sets the corner radius for background/border rendering.
    /// </summary>
    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <summary>
    /// Gets or sets the border thickness.
    /// </summary>
    public double BorderThickness
    {
        get => GetValue(BorderThicknessProperty);
        set => SetValue(BorderThicknessProperty, value);
    }

    /// <summary>
    /// Gets or sets the inner padding.
    /// </summary>
    public Thickness Padding
    {
        get => GetValue(PaddingProperty);
        set => SetValue(PaddingProperty, value);
    }

    /// <summary>
    /// Gets the content bounds (bounds minus padding).
    /// </summary>
    protected Rect ContentBounds => Bounds.Deflate(Padding);

    /// <summary>
    /// Gets or sets the font family.
    /// </summary>
    public string FontFamily
    {
        get => GetValue(FontFamilyProperty);
        set => SetValue(FontFamilyProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the font size.
    /// </summary>
    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>
    /// Gets or sets the font weight.
    /// </summary>
    public FontWeight FontWeight
    {
        get => GetValue(FontWeightProperty);
        set => SetValue(FontWeightProperty, value);
    }


    #region VisualState System

    /// <summary>
    /// Gets the current visual state. Updated automatically before each OnRender.
    /// </summary>
    protected VisualState CurrentVisualState => _visualState;

    /// <summary>
    /// Gets whether the control is currently pressed.
    /// </summary>
    protected bool IsPressed => _isPressed;

    /// <summary>
    /// Named style key. Resolved from the nearest StyleSheet up the tree.
    /// Higher priority than StyleScope and Theme style.
    /// </summary>
    public string? StyleName
    {
        get => _styleName;
        set
        {
            if (_styleName != value)
            {
                _styleName = value;
                ResolveAndApplyStyle();
            }
        }
    }

    /// <summary>
    /// Sets the pressed state and invalidates visual if changed.
    /// </summary>
    protected void SetPressed(bool pressed)
    {
        if (_isPressed != pressed)
        {
            _isPressed = pressed;
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Registers a child element as a named part for TargetSetter resolution.
    /// </summary>
    protected void RegisterPart(string name, UIElement element)
    {
        _parts ??= new();
        _parts[name] = element;
    }

    /// <summary>
    /// Gets a registered named part. Returns null if not found.
    /// </summary>
    internal UIElement? GetPart(string name)
        => _parts?.GetValueOrDefault(name);

    /// <summary>
    /// Computes the current visual state. Override to include control-specific state.
    /// Called once per render frame before OnRender.
    /// </summary>
    protected virtual VisualState ComputeVisualState()
    {
        var f = VisualStateFlags.None;
        var enabled = IsEffectivelyEnabled;
        if (enabled)
        {
            f |= VisualStateFlags.Enabled;
            if (IsMouseOver || IsMouseCaptured) f |= VisualStateFlags.Hot;
            if (IsFocused || IsFocusWithin) f |= VisualStateFlags.Focused;
            if (_isPressed) f |= VisualStateFlags.Pressed;
        }
        return new VisualState { Flags = f };
    }

    /// <summary>
    /// Called when the visual state changes.
    /// Most controls do NOT need to override this — Style + StateTrigger handles state-based values automatically.
    /// </summary>
    protected virtual void OnVisualStateChanged(VisualState oldState, VisualState newState)
    { }

    /// <summary>
    /// Ensures the control's style has been resolved at least once.
    /// Call from layout entry points that bypass <see cref="MeasureOverride"/> (e.g. Window.PerformLayout).
    /// </summary>
    protected void EnsureStyleResolved()
    {
        if (!_styleResolved)
        {
            ResolveAndApplyStyle();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureStyleResolved();

        return base.MeasureOverride(availableSize);
    }

    /// <summary>
    /// Sets the style for this control.
    /// </summary>
    /// <summary>
    /// Forces the next <see cref="OnRender"/> pass to snap style values immediately
    /// instead of animating from the cached <see cref="_visualState"/>.
    /// Used when a virtualization-pinned container re-enters the visible range
    /// and its cached visual state may be stale (e.g. still has Focused/Active
    /// flags from when it was off-screen).
    /// </summary>
    internal void ForceStyleSnap()
    {
        _forceApplyStyle = true;
    }

    internal void SetStyle(Style? style)
    {
        _style = style;
        _forceApplyStyle = true;

        // Pre-apply the full style chain (base setters + matching triggers)
        // via SetTarget so layout-affecting properties and current-state visuals
        // are immediately correct before the next Measure/Arrange/Render.
        // Without triggers, re-attachment would flash enabled colors because
        // PreApply only set base (enabled) values and the disabled trigger
        // was re-applied later via animation in Render.
        PreApplyStyle(style);

        InvalidateVisual();
    }

    private void PreApplyStyle(Style? style)
    {
        if (style == null) return;
        PreApplyStyle(style.BasedOn);

        var flags = ComputeVisualState().Flags;

        for (int i = 0; i < style.Setters.Count; i++)
        {
            if (style.Setters[i] is Setter s)
                PropertyStore.SetTarget(s.Property, s.Value);
        }

        for (int i = 0; i < style.Triggers.Count; i++)
        {
            var trigger = style.Triggers[i];
            if (trigger.Matches(flags))
            {
                for (int j = 0; j < trigger.Setters.Count; j++)
                {
                    if (trigger.Setters[j] is Setter s)
                        PropertyStore.SetTarget(s.Property, s.Value);
                }
            }
        }
    }

    /// <summary>
    /// Resolves the effective Style for this control from:
    /// 1. StyleName (named style from nearest StyleSheet)
    /// 2. StyleScope (nearest container's type-matched rule)
    /// 3. Theme (type-based default)
    /// </summary>
    internal void ResolveAndApplyStyle()
    {
        _styleResolved = true;
        Style? resolved = null;

        // TODO: 1. StyleName → nearest StyleSheet lookup
        // TODO: 2. StyleScope → nearest container type-matched rule

        // 3. Theme default style (walk type hierarchy)
        if (resolved == null)
        {
            var type = GetType();
            while (type != null && type != typeof(UIElement))
            {
                resolved = Theme.GetStyle(type);
                if (resolved != null) break;
                type = type.BaseType;
            }
        }

        SetStyle(resolved);
    }

    protected sealed override void ResolveVisualState()
    {
        var newState = ComputeVisualState();
        var oldState = _visualState;

        if (newState != oldState || _forceApplyStyle)
        {
            // When _forceApplyStyle is true (style just set/changed, re-attachment, theme change),
            // snap to target values immediately instead of animating. Animations are only for
            // interactive state changes (hover, press, focus).
            bool snap = _forceApplyStyle;
            _forceApplyStyle = false;
            _visualState = newState;
            ApplyStyleValues(newState.Flags, snap);
            OnVisualStateChanged(oldState, newState);
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        var bg = GetValue(BackgroundProperty);
        var border = GetValue(BorderBrushProperty);

        if (bg.A == 0 && (BorderThickness <= 0 || border.A == 0))
        {
            return;
        }

        DrawBackgroundAndBorder(context, Bounds, bg, border, CornerRadius);
    }

    /// <summary>
    /// Resolves and applies property values from Style + StateTrigger based on current flags.
    /// </summary>
    private void ApplyStyleValues(VisualStateFlags flags, bool snap = false)
    {
        ApplyStyleChain(_style, flags, snap);
    }

    private void ApplyStyleChain(Style? style, VisualStateFlags flags, bool snap)
    {
        if (style == null) return;

        // Process BasedOn first (lower priority)
        ApplyStyleChain(style.BasedOn, flags, snap);

        // Apply base setters
        for (int i = 0; i < style.Setters.Count; i++)
            ApplySetter(style.Setters[i], snap);

        // Apply matching triggers in declaration order.
        // Convention: declared in priority order (Hot < Focused < Pressed < Disabled).
        // Higher specificity triggers should be declared after lower specificity ones.
        for (int i = 0; i < style.Triggers.Count; i++)
        {
            var trigger = style.Triggers[i];
            if (trigger.Matches(flags))
            {
                for (int j = 0; j < trigger.Setters.Count; j++)
                    ApplySetter(trigger.Setters[j], snap);
            }
        }
    }

    private void ApplySetter(SetterBase setter, bool snap)
    {
        switch (setter)
        {
            case Setter s:
                if (!snap && _style?.FindTransition(s.Property.Id) is Transition transition)
                    Animator.Animate(s.Property, s.Value, transition.Duration, transition.Easing);
                else
                    PropertyStore.SetTarget(s.Property, s.Value);
                break;

            case TargetSetter ts:
                GetPart(ts.TargetName)?.SetTargetInternal(ts.Property, ts.Value);
                break;
        }
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        _font?.Dispose();
        _font = null;
        base.OnThemeChanged(oldTheme, newTheme);

        // Re-resolve style with new theme's palette colors.
        ResolveAndApplyStyle();
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        if (newRoot == null)
        {
            // Detached from visual tree — release style and parts references.
            _style = null;
            _parts?.Clear();
        }
        else
        {
            // Attached to visual tree — resolve style.
            ResolveAndApplyStyle();
        }
    }

    #endregion

    /// <summary>
    /// Handles font cache invalidation when font MewProperty values change.
    /// </summary>
    protected override void OnMewPropertyChanged(MewProperty property)
    {
        if (property.Id == FontFamilyProperty.Id ||
            property.Id == FontSizeProperty.Id ||
            property.Id == FontWeightProperty.Id)
        {
            _font?.Dispose();
            _font = null;
        }

        base.OnMewPropertyChanged(property);
    }

    /// <summary>
    /// Invalidates the cached font when an inherited font property changes on an ancestor.
    /// Called by the inheritance propagation system.
    /// </summary>
    internal void InvalidateFontCache(MewProperty property)
    {
        if (property.Id == FontFamilyProperty.Id ||
            property.Id == FontSizeProperty.Id ||
            property.Id == FontWeightProperty.Id)
        {
            _font?.Dispose();
            _font = null;
        }
    }

    protected TextMeasurementScope BeginTextMeasurement()
    {
        var factory = GetGraphicsFactory();
        var context = factory.CreateMeasurementContext(GetDpi());
        var font = GetFont(factory);
        return new TextMeasurementScope(factory, context, font);
    }

    /// <summary>
    /// Gets or creates the font for this control. Validates the cached font against
    /// current property values (which may be inherited from ancestors).
    /// </summary>
    protected IFont GetFont(IGraphicsFactory factory)
    {
        var family = FontFamily;
        var size = FontSize;
        var weight = FontWeight;
        var dpi = GetDpi();

        if (_font != null && _fontDpi == dpi &&
            _font.Family == family && _font.Size.Equals(size) && _font.Weight == weight)
        {
            return _font;
        }

        _font?.Dispose();
        _font = factory.CreateFont(family, size, dpi, weight);
        _fontDpi = dpi;
        return _font;
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        _font?.Dispose();
        _font = null;
    }

    protected Color PickAccentBorder(Theme theme, Color baseBorder, in VisualState state, double hoverMix = 0.6)
    {
        if (!state.IsEnabled)
        {
            return baseBorder;
        }

        var accent = theme.Palette.Accent;

        if (state.IsFocused || state.IsActive || state.IsPressed)
        {
            // If the control uses the standard border color, keep the strong accent border.
            // If a custom border was supplied, tint it toward the accent instead of hard-replacing it.
            // This avoids "jumping" to a ButtonFace/ControlBorder-based accent when Background/BorderBrush is customized.
            return baseBorder == theme.Palette.ControlBorder
                ? accent
                : Color.Composite(baseBorder, theme.Palette.AccentBorderActiveOverlay);
        }

        if (state.IsHot)
        {
            var overlay = hoverMix == 0.6
                ? theme.Palette.AccentBorderHotOverlay
                : accent.WithAlpha((byte)Math.Clamp(Math.Round(hoverMix * 255.0), 0, 255));

            return Color.Composite(baseBorder, overlay);
        }

        return baseBorder;
    }

    protected Color PickButtonBackground(in VisualState state, Color? normalBackground = null)
    {
        var baseBg = normalBackground ?? Background;

        if (!state.IsEnabled)
        {
            return Theme.Palette.ButtonDisabledBackground;
        }

        if (state.IsPressed || state.IsActive)
        {
            return Color.Composite(baseBg, Theme.Palette.AccentPressedOverlay);
        }

        if (state.IsHot)
        {
            return Color.Composite(baseBg, Theme.Palette.AccentHoverOverlay);
        }

        return baseBg;
    }

    protected Color PickControlBackground(in VisualState state, Color? normalBackground = null)
    {
        return state.IsEnabled ? (normalBackground ?? Background) : Theme.Palette.DisabledControlBackground;
    }

    protected Color PickControlBackground(in VisualState state, Color normalBackground)
    {
        return state.IsEnabled ? normalBackground : Theme.Palette.DisabledControlBackground;
    }

    /// <summary>
    /// Gets the font using the control's graphics factory.
    /// </summary>
    protected IFont GetFont() => GetFont(GetGraphicsFactory());

    protected double GetBorderVisualInset()
    {
        if (BorderThickness <= 0)
        {
            return 0;
        }

        var dpiScale = GetDpi() / 96.0;
        // Treat borders as an "inside" inset and snap thickness to whole device pixels.
        return LayoutRounding.SnapThicknessToPixels(BorderThickness, dpiScale, 1);
    }

    protected BorderRenderMetrics GetBorderRenderMetrics(Rect bounds, double cornerRadiusDip, bool snapBounds = true)
    {
        var dpiScale = GetDpi() / 96.0;
        var borderThickness = BorderThickness <= 0 ? 0 : LayoutRounding.SnapThicknessToPixels(BorderThickness, dpiScale, 1);
        var radius = cornerRadiusDip <= 0 ? 0 : LayoutRounding.RoundToPixel(cornerRadiusDip, dpiScale);

        if (snapBounds)
        {
            bounds = LayoutRounding.SnapBoundsRectToPixels(bounds, dpiScale);
        }

        return new BorderRenderMetrics(bounds, dpiScale, borderThickness, radius);
    }

    protected void DrawBackgroundAndBorder(
        IGraphicsContext context,
        Rect bounds,
        Color background,
        Color borderBrush,
        double cornerRadiusDip)
    {
        if (background.A == 0 && (BorderThickness <= 0 || borderBrush.A == 0))
        {
            return;
        }

        var metrics = GetBorderRenderMetrics(bounds, cornerRadiusDip);
        bounds = metrics.Bounds;
        var borderThickness = metrics.BorderThickness;
        var radius = metrics.CornerRadius;

        bool canUseFillStrokeTrick = borderThickness > 0 &&
                                  borderBrush.A > 0 &&
                                  background.A > 0;

#if USE_FILL_STROKE_TRICK
        if (canUseFillStrokeTrick || background.A == 255)
        {
            if (borderThickness > 0)
            {
                // Fill "stroke" using outer + inner shapes (avoids half-pixel pen alignment issues).
                if (radius > 0)
                {
                    context.FillRoundedRectangle(bounds, radius, radius, borderBrush);
                }
                else
                {
                    context.FillRectangle(bounds, borderBrush);
                }
            }

            var inner = bounds.Deflate(new Thickness(borderThickness));
            var innerRadius = metrics.InnerCornerRadius;

            if (inner.Width > 0 && inner.Height > 0)
            {
                if (innerRadius > 0)
                {
                    context.FillRoundedRectangle(inner, innerRadius, innerRadius, background);
                }
                else
                {
                    context.FillRectangle(inner, background);
                }
            }

            return;
        }
#endif

        if (background.A > 0)
        {
            if (radius > 0)
            {
                context.FillRoundedRectangle(bounds, radius, radius, background);
            }
            else
            {
                context.FillRectangle(bounds, background);
            }
        }

        if (borderThickness > 0 && borderBrush.A > 0)
        {
            if (radius > 0)
            {
                context.DrawRoundedRectangle(bounds, radius, radius, borderBrush, borderThickness, strokeInset: true);
            }
            else
            {
                context.DrawRectangle(bounds, borderBrush, borderThickness, strokeInset: true);
            }
        }
    }


    protected override void OnMouseEnter()
    {
        base.OnMouseEnter();
        ShowToolTip();
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        HideToolTip();
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        _lastMousePositionInWindow = e.Position;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        HideToolTip();

        if (e.Handled)
        {
            return;
        }

        if (e.Button == MouseButton.Right && ContextMenu != null)
        {
            ContextMenu.ShowAt(this, e.Position);
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

        // Hide tooltips on keyboard interaction.
        HideToolTip();
    }

    protected override void OnDispose()
    {
        base.OnDispose();

        // Release cached font resources.
        _font?.Dispose();
        _font = null;

        HideToolTip();
    }

    private void ShowToolTip()
    {
        if (!IsMouseOver)
        {
            return;
        }

        if (ToolTipContent == null && string.IsNullOrWhiteSpace(ToolTipText))
        {
            return;
        }

        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        var client = window.ClientSize;
        var anchor = window.LastMousePositionDip;
        if (anchor.X == 0 && anchor.Y == 0)
        {
            anchor = _lastMousePositionInWindow;
        }
        if (anchor.X == 0 && anchor.Y == 0 && Bounds.Width > 0 && Bounds.Height > 0)
        {
            anchor = new Point(Bounds.X + Bounds.Width / 2, Bounds.Bottom);
        }

        const double dx = 12;
        const double dy = 18;
        double x = anchor.X + dx;
        double y = anchor.Y + dy;

        Size desired;
        if (ToolTipContent != null)
        {
            // Measure using the window-owned tooltip instance so sizing matches what will be shown.
            // This avoids creating a tooltip per control.
            var measureSize = new Size(Math.Max(0, client.Width), Math.Max(0, client.Height));
            desired = window.MeasureToolTip(ToolTipContent, measureSize);
        }
        else
        {
            var toolTipText = ToolTipText!;
            var measureSize = new Size(Math.Max(0, client.Width), Math.Max(0, client.Height));
            desired = window.MeasureToolTip(toolTipText, measureSize);
        }

        double w = Math.Max(0, desired.Width);
        double h = Math.Max(0, desired.Height);

        if (x + w > client.Width)
        {
            x = Math.Max(0, client.Width - w);
        }

        if (y + h > client.Height)
        {
            y = Math.Max(0, anchor.Y - h - dy);
            if (y < 0)
            {
                y = Math.Max(0, client.Height - h);
            }
        }

        if (ToolTipContent != null)
        {
            window.ShowToolTip(this, ToolTipContent, new Rect(x, y, w, h));
        }
        else
        {
            window.ShowToolTip(this, ToolTipText!, new Rect(x, y, w, h));
        }
    }

    private void HideToolTip()
    {
        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        window.CloseToolTip(this);
    }

    protected readonly struct TextMeasurementScope : IDisposable
    {
        public TextMeasurementScope(IGraphicsFactory factory, IGraphicsContext context, IFont font)
        {
            Factory = factory;
            Context = context;
            Font = font;
        }

        public IGraphicsFactory Factory { get; }

        public IGraphicsContext Context { get; }

        public IFont Font { get; }

        public void Dispose() => Context.Dispose();
    }

    /// <summary>
    /// Represents the visual interaction state of a control.
    /// Stored on Control, compared per-frame, drives OnVisualStateChanged.
    /// </summary>
    protected readonly struct VisualState : IEquatable<VisualState>
    {
        /// <summary>Framework-defined state flags.</summary>
        public VisualStateFlags Flags { get; init; }

        /// <summary>
        /// Control-defined custom state flags. The framework never reads or modifies this value.
        /// </summary>
        public uint CustomFlags { get; init; }

        public bool IsEnabled => (Flags & VisualStateFlags.Enabled) != 0;

        public bool IsHot => (Flags & VisualStateFlags.Hot) != 0;

        public bool IsFocused => (Flags & VisualStateFlags.Focused) != 0;

        public bool IsPressed => (Flags & VisualStateFlags.Pressed) != 0;

        public bool IsActive => (Flags & VisualStateFlags.Active) != 0;

        public bool IsChecked => (Flags & VisualStateFlags.Checked) != 0;

        public bool IsIndeterminate => (Flags & VisualStateFlags.Indeterminate) != 0;

        public bool Equals(VisualState other)
            => Flags == other.Flags && CustomFlags == other.CustomFlags;

        public override bool Equals(object? obj) => obj is VisualState o && Equals(o);

        public override int GetHashCode() => HashCode.Combine(Flags, CustomFlags);

        public static bool operator ==(VisualState a, VisualState b) => a.Equals(b);

        public static bool operator !=(VisualState a, VisualState b) => !a.Equals(b);
    }

    protected readonly struct BorderRenderMetrics
    {
        public BorderRenderMetrics(Rect bounds, double dpiScale, double borderThickness, double cornerRadius)
        {
            Bounds = bounds;
            DpiScale = dpiScale;
            BorderThickness = borderThickness;
            CornerRadius = cornerRadius;
            InnerCornerRadius = Math.Max(0, cornerRadius - borderThickness);
        }

        public Rect Bounds { get; }

        public double DpiScale { get; }

        public double BorderThickness { get; }

        public double CornerRadius { get; }

        /// <summary>
        /// Corner radius for the inner contour of the border (content area).
        /// Computed from snapped CornerRadius and snapped BorderThickness so it matches
        /// the strokeInset inner edge exactly. Use this for clip radii instead of
        /// <c>RoundToPixel(outerRadius - thickness)</c> to avoid fractional-DPI rounding mismatches.
        /// </summary>
        public double InnerCornerRadius { get; }
    }
}
