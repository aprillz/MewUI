using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for all controls.
/// </summary>
public abstract class Control : FrameworkElement
    , IVisualTreeHost
{
    #region MewProperty Declarations

    /// <summary>Background color property.</summary>
    public static readonly MewProperty<Color> BackgroundProperty =
        MewProperty<Color>.Register<Control>(nameof(Background), Color.Transparent, MewPropertyOptions.AffectsRender);

    /// <summary>Border color property.</summary>
    public static readonly MewProperty<Color> BorderBrushProperty =
        MewProperty<Color>.Register<Control>(nameof(BorderBrush), Color.Transparent, MewPropertyOptions.AffectsRender);

    /// <summary>Template property. The built template tree replaces the control's own visuals.</summary>
    public static readonly MewProperty<ControlTemplate?> TemplateProperty =
        MewProperty<ControlTemplate?>.Register<Control>(nameof(Template), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnTemplateChanged());

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

    public static readonly MewProperty<Element?> ToolTipProperty =
        MewProperty<Element?>.Register<Control>(nameof(ToolTip), null, MewPropertyOptions.None);

    public static readonly MewProperty<ContextMenu?> ContextMenuProperty =
        MewProperty<ContextMenu?>.Register<Control>(nameof(ContextMenu), null, MewPropertyOptions.None);

    private static readonly MewPropertyKey<bool> IsPressedPropertyKey =
        MewProperty<bool>.RegisterReadOnly<Control>(nameof(IsPressed), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState);

    /// <summary>
    /// Whether this control is currently in the pressed state. Read-only; set internally via
    /// <see cref="SetPressed"/>. Participates in style triggers.
    /// </summary>
    public static readonly MewProperty<bool> IsPressedProperty = IsPressedPropertyKey.Property;

    #endregion

    private IFont? _font;
    private uint _fontDpi;
    private Point _lastMousePositionInWindow;

    // VisualState system fields
    private VisualState _visualState;

    private bool _forceApplyStyle;
    private bool _styleNameResolved;

    // ContextVersion at the time _style was resolved; a mismatch means the ancestor
    // chain changed since and the style must be re-resolved.
    private int _styleContextVersion = -1;

    private Style? _style;
    private string? _styleName;
    private Dictionary<string, UIElement>? _parts;

    private PathGeometry? _sharedOuterPath;
    private PathGeometry? _sharedInnerPath;

    /// <summary>
    /// Gets or sets the tooltip element for this control.
    /// Use the <c>ToolTip(string)</c> extension method for simple text tooltips.
    /// </summary>
    public Element? ToolTip
    {
        get => GetValue(ToolTipProperty);
        set => SetValue(ToolTipProperty, value);
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
    public bool IsPressed => GetValue(IsPressedProperty);

    /// <summary>
    /// Named style key. Resolved from the nearest StyleSheet up the tree.
    /// Higher priority than StyleSheet type rules and Theme style.
    /// </summary>
    public string? StyleName
    {
        get => _styleName;
        set
        {
            if (_styleName != value)
            {
                _styleName = value;
                _styleNameResolved = false;

                // Attached: apply now (with transitions). Detached controls resolve on attach or first Measure.
                if (FindVisualRoot() is Window)
                {
                    ResolveAndApplyStyle(animate: true);
                }
            }
        }
    }

    /// <summary>
    /// Sets the pressed state. Change notification drives <see cref="UIElement.InvalidateVisualState"/>
    /// and <see cref="Element.InvalidateVisual"/> via the property's AffectsVisualState/AffectsRender flags.
    /// </summary>
    protected void SetPressed(bool pressed) => SetValue(IsPressedPropertyKey, pressed);

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
            if (IsPressed) f |= VisualStateFlags.Pressed;
        }
        return new VisualState { Flags = f };
    }

    /// <summary>
    /// Called when the visual state changes.
    /// Most controls do NOT need to override this - Style + StateTrigger handles state-based values automatically.
    /// </summary>
    protected virtual void OnVisualStateChanged(VisualState oldState, VisualState newState)
    { }

    /// <summary>
    /// Ensures the control's style has been resolved at least once.
    /// Call from layout entry points that bypass <see cref="MeasureOverride"/> (e.g. Window.PerformLayout).
    /// </summary>
    protected void EnsureStyleResolved()
    {
        if (!_styleNameResolved || _styleContextVersion != ContextVersion)
        {
            ResolveAndApplyStyle();
        }
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        EnsureStyleResolved();
        ApplyTemplate();

        return base.MeasureOverride(availableSize);
    }

    private ControlTemplateInstance? _templateInstance;
    private bool _templateThemeStale;

    /// <summary>
    /// Gets or sets the template that provides this control's visual tree.
    /// Null keeps the control's own drawn visuals.
    /// </summary>
    public ControlTemplate? Template
    {
        get => GetValue(TemplateProperty);
        set => SetValue(TemplateProperty, value);
    }

    /// <summary>
    /// Builds and attaches the current template if it is not applied yet.
    /// Returns true when a new instance was built.
    /// </summary>
    protected bool ApplyTemplate()
    {
        if (_templateThemeStale)
        {
            _templateThemeStale = false;
            DetachTemplateInstance();
        }

        var template = Template;
        if (template == null || _templateInstance != null)
        {
            return false;
        }

        var context = new ControlTemplateContext(this);
        var root = template.Build(this, context)
            ?? throw new InvalidOperationException("The template build returned no visual root.");
        if (ReferenceEquals(root, this))
        {
            throw new InvalidOperationException("The template visual root cannot be the control itself.");
        }
        if (root.Parent != null)
        {
            throw new InvalidOperationException("The template visual root already has a visual parent.");
        }

        // Attach through the Parent setter so theme/DPI/inherited state fans out into the
        // template subtree before parts are used.
        root.Parent = this;
        var instance = new ControlTemplateInstance { VisualRoot = root, Context = context };
        _templateInstance = instance;

        // Wire presenters after the root is attached so projected content lands under a
        // rooted presenter and picks up the correct inherited state.
        VisualTree.Visit(root, element =>
        {
            if (element is ContentPresenter presenter && presenter.TemplatedParent == null)
            {
                instance.Presenters.Add(presenter);
                presenter.AttachToTemplatedParent(this);
            }
        });

        OnTemplateInstanceAttached();
        OnApplyTemplate();
        return true;
    }

    /// <summary>
    /// Called after the template instance is attached and presenters are wired,
    /// before <see cref="OnApplyTemplate"/>. Slots detach compat visual links here.
    /// </summary>
    private protected virtual void OnTemplateInstanceAttached() { }

    /// <summary>
    /// Called after the template instance is torn down. Slots re-host their
    /// logical children visually here so the non-template path keeps working.
    /// </summary>
    private protected virtual void OnTemplateInstanceDetached() { }

    internal Element? TemplateVisualRoot => _templateInstance?.VisualRoot;

    internal bool HasTemplateInstance => _templateInstance != null;

    internal void RefreshTemplatePresenters(MewProperty property)
    {
        var instance = _templateInstance;
        if (instance == null)
        {
            return;
        }

        for (int i = 0; i < instance.Presenters.Count; i++)
        {
            if (instance.Presenters[i].ContentSource == property)
            {
                instance.Presenters[i].UpdateProjection();
            }
        }
    }

    /// <summary>
    /// Called after the template's visual tree is built and attached. Look up named parts here.
    /// </summary>
    protected virtual void OnApplyTemplate() { }

    /// <summary>
    /// Returns the named template part, or null when no template is applied or the part is missing.
    /// </summary>
    /// <param name="name">The part name registered during the template build.</param>
    protected T? GetTemplateChild<T>(string name) where T : Element
        => _templateInstance?.Context.Find(name) as T;

    private void OnTemplateChanged()
    {
        // Tear down eagerly so focus inside the old tree unwinds via OnDetaching;
        // the replacement builds lazily on the next measure.
        DetachTemplateInstance();
    }

    private void DetachTemplateInstance()
    {
        var instance = _templateInstance;
        if (instance != null)
        {
            _templateInstance = null;

            // Release projected content first so it is not discarded with the template tree.
            for (int i = 0; i < instance.Presenters.Count; i++)
            {
                instance.Presenters[i].DetachFromTemplatedParent();
            }

            instance.Context.ReleaseBindings();

            if (instance.VisualRoot.Parent == this)
            {
                instance.VisualRoot.Parent = null;
            }

            OnTemplateInstanceDetached();
        }
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor)
        => _templateInstance == null || visitor(_templateInstance.VisualRoot);

    protected override Size MeasureContent(Size availableSize)
    {
        if (_templateInstance != null)
        {
            var root = _templateInstance.VisualRoot;
            root.Measure(availableSize);
            return root.DesiredSize;
        }

        return base.MeasureContent(availableSize);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (_templateInstance != null)
        {
            _templateInstance.VisualRoot.Arrange(bounds);
        }
        else
        {
            base.ArrangeContent(bounds);
        }
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (_templateInstance != null)
        {
            _templateInstance.VisualRoot.Render(context);
        }
        else
        {
            base.RenderSubtree(context);
        }
    }

    /// <summary>
    /// Queues a visual-state reconciliation that re-applies style values even when the state
    /// flags compare equal, snapping instead of animating. Used when a virtualization-pinned
    /// container re-enters the visible range after a rebind: its cached visual state may be
    /// stale, and animating from the previous item's visuals would cross-fade between items.
    /// </summary>
    internal void ForceStyleSnap()
    {
        _forceApplyStyle = true;
        InvalidateVisualState();
    }

    internal void SetStyle(Style? style, bool snap = true)
    {
        var oldStyle = _style;
        _style = style;
        _styleContextVersion = ContextVersion;

        // Values the old style set but the new one does not would otherwise linger
        // with Source=Style after the swap.
        if (oldStyle != null && !ReferenceEquals(oldStyle, style))
        {
            ClearStaleStyleValues(oldStyle, style);
        }

        // Apply the full style chain (base setters + matching triggers) immediately so
        // layout-affecting properties and current-state visuals are correct before the
        // next Measure/Arrange/Render. Using ApplyStyleValues (not a lightweight pre-apply)
        // is required so _activeTriggerPropertyIds and _visualState stay in sync -
        // otherwise a later state transition while offscreen (render culled) fails to
        // restore trigger-stamped values because bookkeeping was skipped here.
        var flags = ComputeVisualState().Flags;
        _visualState = new VisualState { Flags = flags };
        ApplyStyleValues(flags, snap || _forceApplyStyle);
        _forceApplyStyle = false;

        InvalidateVisual();
    }

    /// <summary>
    /// Resolves the effective Style for this control from:
    /// 1. StyleName (named style from nearest StyleSheet)
    /// 2. StyleSheet type rule (nearest container's type-matched rule)
    /// 3. Theme (type-based default)
    /// </summary>
    /// <param name="animate">When true, a runtime style swap applies with the new style's transitions.</param>
    internal void ResolveAndApplyStyle(bool animate = false)
    {
        Style? resolved = null;

        // 1. StyleName → walk StyleSheet chain
        if (_styleName != null)
        {
            resolved = FindNamedStyle(_styleName);
            if (resolved == null && !Application.IsRunning)
            {
                // StyleSheet not available yet (Application not running) - retry later
                _styleNameResolved = false;
                return;
            }
        }

        _styleNameResolved = true;

        // 2. StyleSheet type rule → nearest container type-matched rule
        if (resolved == null)
        {
            var controlType = GetType();
            for (Element? current = ContextParent; current != null; current = current.ContextParent)
            {
                if (current is FrameworkElement fe)
                {
                    resolved = fe.StyleSheet?.GetByType(controlType);
                    if (resolved != null) break;
                }
            }
        }

        // 3. Theme default style (walk type hierarchy)
        if (resolved == null)
        {
            var type = GetType();
            while (type != null && type != typeof(UIElement))
            {
                resolved = Style.ForType(type);
                if (resolved != null) break;
                type = type.BaseType;
            }
        }

        // Transitions only make sense for a runtime swap on an attached, already-styled
        // control; initial attach, theme change, and detached resolution snap.
        bool snap = !animate || _style == null || FindVisualRoot() is not Window;
        SetStyle(resolved, snap);
    }

    private Style? FindNamedStyle(string name)
    {
        for (Element? current = this; current != null; current = current.ContextParent)
        {
            if (current is FrameworkElement fe && fe.StyleSheet != null)
            {
                var style = fe.StyleSheet.Get(name);
                if (style != null) return style;
            }
        }
        return Application.IsRunning ? Application.Current.StyleSheet?.Get(name) : null;
    }

    protected override sealed void ResolveVisualState(bool snap)
    {
        var newState = ComputeVisualState();
        var oldState = _visualState;

        if (newState != oldState || _forceApplyStyle)
        {
            // _forceApplyStyle (virtualization rebind via ForceStyleSnap) always snaps: a recycled
            // container must re-apply even with equal flags and must not animate from the previous
            // item's visuals. Otherwise the caller chooses - the visual-state update snaps for
            // offscreen elements, animates for on-screen.
            bool effectiveSnap = snap || _forceApplyStyle;
            _forceApplyStyle = false;
            _visualState = newState;
            ApplyStyleValues(newState.Flags, effectiveSnap);
            OnVisualStateChanged(oldState, newState);
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        // A template owns the control's entire visuals; drawing the built-in chrome
        // underneath it would double-render and defeat re-templating.
        if (_templateInstance != null)
        {
            return;
        }

        var bg = GetValue(BackgroundProperty);
        var border = GetValue(BorderBrushProperty);

        if (bg.A == 0 && (BorderThickness <= 0 || border.A == 0))
        {
            return;
        }

        DrawBackgroundAndBorder(context, Bounds, bg, border, BorderThickness, CornerRadius);
    }

    /// <summary>
    /// Resolves and applies property values from Style + StateTrigger based on current flags.
    /// Trigger tracking is done at the top level (not per recursion) to avoid
    /// BasedOn recursion clobbering the active trigger set.
    /// </summary>
    private void ApplyStyleValues(VisualStateFlags flags, bool snap = false)
    {
        // 1. Collect ALL trigger property IDs from the entire style chain
        _newTriggerPropertyIds ??= new HashSet<int>();
        _newTriggerPropertyIds.Clear();
        CollectTriggerProperties(_style, flags, _newTriggerPropertyIds);

        // 2. Restore properties that were triggered before but not now
        if (_activeTriggerPropertyIds != null && _style != null)
        {
            foreach (var id in _activeTriggerPropertyIds)
            {
                if (!_newTriggerPropertyIds.Contains(id))
                    RestoreFromStyle(_style, id, snap);
            }
        }

        // 3. Apply base setters + matching triggers through the chain
        ApplyStyleChain(_style, flags, snap);

        // 4. Swap active sets
        (_activeTriggerPropertyIds, _newTriggerPropertyIds) =
            (_newTriggerPropertyIds, _activeTriggerPropertyIds);
    }

    // Tracks which property IDs were set by triggers in the previous state.
    // Reused across calls to avoid allocation.
    private HashSet<int>? _activeTriggerPropertyIds;

    private HashSet<int>? _newTriggerPropertyIds;

    private static void CollectTriggerProperties(Style? style, VisualStateFlags flags, HashSet<int> result)
    {
        if (style == null) return;
        CollectTriggerProperties(style.BasedOn, flags, result);

        for (int i = 0; i < style.Triggers.Count; i++)
        {
            var trigger = style.Triggers[i];
            if (trigger.Matches(flags))
            {
                for (int j = 0; j < trigger.Setters.Count; j++)
                {
                    if (trigger.Setters[j] is Setter s)
                        result.Add(s.Property.Id);
                }
            }
        }
    }

    /// <summary>
    /// Collects final (winning) values from the entire style chain, then applies once.
    /// BasedOn values are overridden by derived style values for the same property.
    /// This avoids intermediate animations when BasedOn and derived styles both set the same property.
    /// </summary>
    private void ApplyStyleChain(Style? style, VisualStateFlags flags, bool snap)
    {
        if (style == null) return;

        // Collect final values: later styles override earlier (BasedOn) ones
        _resolvedSetters ??= new();
        _resolvedSetters.Clear();
        CollectResolvedValues(style, flags, _resolvedSetters);

        // Apply all collected values once
        var theme = Theme;
        foreach (var kv in _resolvedSetters)
        {
            var (setter, source) = kv.Value;
            ApplySetter(setter, source, snap);
        }
    }

    private Dictionary<int, (SetterBase Setter, ValueSource Source)>? _resolvedSetters;

    private static void CollectResolvedValues(Style? style, VisualStateFlags flags,
        Dictionary<int, (SetterBase Setter, ValueSource Source)> result)
    {
        if (style == null) return;

        // BasedOn first (lower priority - will be overwritten by derived)
        CollectResolvedValues(style.BasedOn, flags, result);

        // Base setters
        for (int i = 0; i < style.Setters.Count; i++)
        {
            if (style.Setters[i] is Setter s)
                result[s.Property.Id] = (s, ValueSource.Style);
        }

        // Matching triggers (override base setters)
        for (int i = 0; i < style.Triggers.Count; i++)
        {
            var trigger = style.Triggers[i];
            if (trigger.Matches(flags))
            {
                for (int j = 0; j < trigger.Setters.Count; j++)
                {
                    if (trigger.Setters[j] is Setter s)
                        result[s.Property.Id] = (s, ValueSource.Trigger);
                }
            }
        }
    }

    private void RestoreFromStyle(Style style, int propertyId, bool snap)
    {
        // If a higher-priority source (Local) owns this property, don't touch it
        var currentSource = PropertyStore.GetSource(propertyId);
        if (currentSource >= ValueSource.Local)
            return;

        // Find the base setter value for this property from the style chain
        var setterValue = FindStyleSetterValue(style, propertyId);
        if (setterValue != null)
        {
            var property = MewPropertyRegistry.GetProperty(propertyId);
            if (property != null)
            {
                if (!snap && _style?.FindTransition(propertyId) is Transition transition)
                {
                    // Animate directly - Animator bypasses source priority and
                    // SetTargetDirect updates BaseSource to Style.
                    // No ClearSource needed; avoids intermediate null/default flash.
                    Animator.Animate(property, setterValue, transition.Duration, transition.Easing, ValueSource.Style);
                }
                else
                {
                    // Snap: force-set by clearing trigger source first, then setting style value
                    PropertyStore.ClearSource(propertyId, ValueSource.Trigger);
                    PropertyStore.SetStyle(property, setterValue);
                }
            }
        }
        else
        {
            // No style setter - clear trigger to let inherited/default take effect
            PropertyStore.ClearSource(propertyId, ValueSource.Trigger);
        }
    }

    private object? FindStyleSetterValue(Style? style, int propertyId)
    {
        while (style != null)
        {
            for (int i = 0; i < style.Setters.Count; i++)
            {
                if (style.Setters[i] is Setter s && s.Property.Id == propertyId)
                    return s.ResolveValue(Theme);
            }
            style = style.BasedOn;
        }
        return null;
    }

    /// <summary>
    /// Clears Style-sourced values that the old style chain set but the new chain no longer
    /// sets, so they fall back to default/inherited instead of lingering after a style swap.
    /// </summary>
    private void ClearStaleStyleValues(Style oldStyle, Style? newStyle)
    {
        for (Style? current = oldStyle; current != null; current = current.BasedOn)
        {
            for (int i = 0; i < current.Setters.Count; i++)
            {
                if (current.Setters[i] is Setter setter && !StyleChainSetsProperty(newStyle, setter.Property.Id))
                {
                    PropertyStore.ClearSource(setter.Property.Id, ValueSource.Style);
                }
            }
        }
    }

    private static bool StyleChainSetsProperty(Style? style, int propertyId)
    {
        while (style != null)
        {
            for (int i = 0; i < style.Setters.Count; i++)
            {
                if (style.Setters[i] is Setter s && s.Property.Id == propertyId)
                    return true;
            }
            style = style.BasedOn;
        }
        return false;
    }

    private void ApplySetter(SetterBase setter, ValueSource source, bool snap)
    {
        switch (setter)
        {
            case Setter s:
                // Don't override higher-priority sources (e.g. Local beats Trigger/Style)
                var currentSource = PropertyStore.GetSource(s.Property.Id);
                if (currentSource > source)
                    break;

                var value = s.ResolveValue(Theme);
                if (!snap && _style?.FindTransition(s.Property.Id) is Transition transition)
                    Animator.Animate(s.Property, value, transition.Duration, transition.Easing, source);
                else if (source == ValueSource.Style)
                    PropertyStore.SetStyle(s.Property, value);
                else
                    PropertyStore.SetTrigger(s.Property, value);
                break;

            case TargetSetter ts:
                GetPart(ts.TargetName)?.SetTargetInternal(ts.Property, ts.ResolveValue(Theme));
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

        // A template instance is an artifact of the theme it was built under (builds may bake
        // metrics/colors), so it is rebuilt lazily; deferring the detach keeps the theme
        // broadcast walk from mutating the tree it is traversing.
        if (_templateInstance != null)
        {
            _templateThemeStale = true;
            InvalidateMeasure();
        }
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        if (newRoot == null)
        {
            // Detached from visual tree - release style and parts references.
            _style = null;
            _parts?.Clear();
        }
        else
        {
            // Attached to visual tree - resolve style.
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
            OnFontCacheInvalidated(property);
        }
    }

    protected virtual void OnFontCacheInvalidated(MewProperty property)
    {
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
        return LayoutRounding.SnapThicknessToPixels(BorderThickness, dpiScale, 1);
    }

    internal BorderRenderMetrics GetBorderRenderMetrics(Rect bounds, double borderThicknessDip, double cornerRadiusDip, bool snapBounds = true)
    {
        var dpiScale = GetDpi() / 96.0;
        var borderThickness = borderThicknessDip <= 0 ? 0 : LayoutRounding.SnapThicknessToPixels(borderThicknessDip, dpiScale, 1);
        var radius = cornerRadiusDip <= 0 ? 0 : LayoutRounding.RoundToPixel(cornerRadiusDip, dpiScale);

        if (snapBounds)
            bounds = LayoutRounding.SnapBoundsRectToPixels(bounds, dpiScale);

        return new BorderRenderMetrics(bounds, dpiScale, new Thickness(borderThickness), new CornerRadius(radius));
    }

    protected void DrawBackgroundAndBorder(
        IGraphicsContext context,
        Rect bounds,
        Color background,
        Color borderBrush,
        double borderThicknessDip,
        double cornerRadiusDip)
    {
        if (background.A == 0 && (borderThicknessDip <= 0 || borderBrush.A == 0))
        {
            return;
        }

        var metrics = GetBorderRenderMetrics(bounds, borderThicknessDip, cornerRadiusDip);

        if (metrics.IsSimple)
        {
            DrawBackgroundAndBorderSimple(context, in metrics, background, borderBrush);
        }
        else
        {
            DrawBackgroundAndBorderComplex(context, in metrics, background, borderBrush);
        }
    }

    /// <summary>
    /// Creates DPI-snapped border render metrics from non-uniform thickness and corner radius.
    /// </summary>
    internal static BorderRenderMetrics CreateBorderRenderMetrics(
        Rect bounds, double dpiScale, Thickness borderThickness, CornerRadius cornerRadius)
    {
        bounds = LayoutRounding.SnapBoundsRectToPixels(bounds, dpiScale);

        var bt = new Thickness(
            borderThickness.Left <= 0 ? 0 : LayoutRounding.SnapThicknessToPixels(borderThickness.Left, dpiScale, 1),
            borderThickness.Top <= 0 ? 0 : LayoutRounding.SnapThicknessToPixels(borderThickness.Top, dpiScale, 1),
            borderThickness.Right <= 0 ? 0 : LayoutRounding.SnapThicknessToPixels(borderThickness.Right, dpiScale, 1),
            borderThickness.Bottom <= 0 ? 0 : LayoutRounding.SnapThicknessToPixels(borderThickness.Bottom, dpiScale, 1));

        var cr = new CornerRadius(
            cornerRadius.TopLeft <= 0 ? 0 : LayoutRounding.RoundToPixel(cornerRadius.TopLeft, dpiScale),
            cornerRadius.TopRight <= 0 ? 0 : LayoutRounding.RoundToPixel(cornerRadius.TopRight, dpiScale),
            cornerRadius.BottomRight <= 0 ? 0 : LayoutRounding.RoundToPixel(cornerRadius.BottomRight, dpiScale),
            cornerRadius.BottomLeft <= 0 ? 0 : LayoutRounding.RoundToPixel(cornerRadius.BottomLeft, dpiScale));

        cr = BorderGeometry.ClampRadii(bounds, cr);

        return new BorderRenderMetrics(bounds, dpiScale, bt, cr);
    }

    /// <summary>
    /// Draws background and border with per-side thickness and per-corner radius.
    /// Falls back to the optimized uniform path when both are uniform.
    /// </summary>
    protected void DrawBackgroundAndBorder(
        IGraphicsContext context,
        Rect bounds,
        Color background,
        Color borderBrush,
        Thickness borderThickness,
        CornerRadius cornerRadius)
    {
        if (background.A == 0 && (borderThickness == Thickness.Zero || borderBrush.A == 0))
        {
            return;
        }

        var metrics = CreateBorderRenderMetrics(bounds, GetDpi() / 96.0, borderThickness, cornerRadius);

        if (metrics.IsSimple)
        {
            DrawBackgroundAndBorderSimple(context, in metrics, background, borderBrush);
        }
        else
        {
            DrawBackgroundAndBorderComplex(context, in metrics, background, borderBrush);
        }
    }

    private static void DrawBackgroundAndBorderSimple(
        IGraphicsContext context,
        in BorderRenderMetrics metrics,
        Color background,
        Color borderBrush)
    {
        var bounds = metrics.Bounds;
        var borderThickness = metrics.UniformThickness;
        var radius = metrics.UniformRadius;

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

    private void DrawBackgroundAndBorderComplex(
        IGraphicsContext context,
        in BorderRenderMetrics metrics,
        Color background,
        Color borderBrush)
    {
        // Border first: fill entire outer contour with border color.
        // Background then overwrites the inner area - no seam at the boundary.
        // Gate on HasAnyBorder, not UniformThickness (= Left only): a non-uniform border may have Left == 0 while
        // its other sides are non-zero (e.g. a tab/border-tab open on one side), which must still draw a border.
        if (borderBrush.A > 0 && metrics.HasAnyBorder)
        {
            var outerPath = _sharedOuterPath ??= new PathGeometry();
            BorderGeometry.GenerateOuterContour(outerPath, in metrics);
            if (!outerPath.IsEmpty)
            {
                context.FillPath(outerPath, borderBrush);
            }
        }

        if (background.A > 0)
        {
            var innerPath = _sharedInnerPath ??= new PathGeometry();
            BorderGeometry.GenerateBackgroundRegion(innerPath, in metrics);
            if (!innerPath.IsEmpty)
            {
                context.FillPath(innerPath, background);
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

        if (ToolTip == null)
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

        var measureSize = new Size(Math.Max(0, client.Width), Math.Max(0, client.Height));
        Size desired = window.MeasureToolTip(ToolTip!, measureSize);

        double w = Math.Max(0, desired.Width);
        double h = Math.Max(0, desired.Height);

        x = PopupPlacement.ClampHorizontal(x, w, client.Width, floorToZero: false);

        if (y + h > client.Height)
        {
            y = Math.Max(0, anchor.Y - h - dy);
            if (y < 0)
            {
                y = Math.Max(0, client.Height - h);
            }
        }

        window.ShowToolTip(this, ToolTip!, new Rect(x, y, w, h));
    }

    private void HideToolTip()
    {
        if (ToolTip == null)
        {
            return;
        }

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
}
