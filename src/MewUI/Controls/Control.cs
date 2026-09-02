using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for all controls.
/// </summary>
public abstract partial class Control : TextElement
    , IVisualTreeHost
{
    static Control() { }

    private static readonly bool _defaultStyleRegistered =
        DefaultStyles.Register<Control>(DefaultStyles.CreateControlBaseStyle);

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

    private static readonly MewPropertyKey<bool> IsPressedPropertyKey =
        MewProperty<bool>.RegisterReadOnly<Control>(nameof(IsPressed), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState);

    /// <summary>
    /// Whether this control is currently in the pressed state. Read-only; set internally via
    /// <see cref="SetPressed"/>. Participates in style triggers.
    /// </summary>
    public static readonly MewProperty<bool> IsPressedProperty = IsPressedPropertyKey.Property;

    private static readonly IReadOnlyList<ValidationError> EMPTY_VALIDATION_ERRORS =
        Array.Empty<ValidationError>();

    private static readonly MewPropertyKey<bool> HasValidationErrorPropertyKey =
        MewProperty<bool>.RegisterReadOnly<Control>(nameof(HasValidationError), false,
            MewPropertyOptions.AffectsVisualState);

    /// <summary>
    /// Gets whether any binding on this control currently has a validation error.
    /// </summary>
    public static readonly MewProperty<bool> HasValidationErrorProperty =
        HasValidationErrorPropertyKey.Property;

    private static readonly MewPropertyKey<IReadOnlyList<ValidationError>> ValidationErrorsPropertyKey =
        MewProperty<IReadOnlyList<ValidationError>>.RegisterReadOnly<Control>(
            nameof(ValidationErrors),
            EMPTY_VALIDATION_ERRORS);

    /// <summary>
    /// Gets an immutable snapshot of the current per-property binding validation errors on this control.
    /// </summary>
    public static readonly MewProperty<IReadOnlyList<ValidationError>> ValidationErrorsProperty =
        ValidationErrorsPropertyKey.Property;

    #endregion

    // VisualState system fields
    private VisualState _visualState;

    private bool _forceApplyStyle;
    private bool _styleNameResolved;

    // ContextVersion at the time the style cascade was resolved; a mismatch means the ancestor
    // chain changed since and the style must be re-resolved.
    private int _styleContextVersion = -1;

    private Style? _defaultStyle;
    private Style? _style;
    private HashSet<Style>? _applicationStyleChain;
    private string? _styleName;
    private Dictionary<int, ValidationError>? _validationErrors;

    private PathGeometry? _sharedOuterPath;
    private PathGeometry? _sharedInnerPath;

    /// <summary>
    /// Gets or sets the background color.
    /// </summary>
    public Color Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
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
    /// Takes focus when the control can hold it. A control that runs something on its access key says so
    /// by overriding this; for the rest, taking focus is the whole of what the key can do, and letting it
    /// bubble past a control that can be focused loses it.
    /// </summary>
    internal override void OnAccessKey()
    {
        if (Focusable && IsEffectivelyEnabled)
        {
            Focus();
            return;
        }

        base.OnAccessKey();
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
    /// Gets whether any binding on this control currently has an error.
    /// </summary>
    public bool HasValidationError => GetValue(HasValidationErrorProperty);

    /// <summary>
    /// Gets an immutable snapshot of the current per-property binding errors on this control.
    /// </summary>
    public IReadOnlyList<ValidationError> ValidationErrors => GetValue(ValidationErrorsProperty);

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
    /// Computes the current visual state. Override to include control-specific state.
    /// Called once per render frame before OnRender.
    /// </summary>
    protected virtual VisualState ComputeVisualState()
    {
        var f = VisualStateFlags.None;
        if (HasValidationError)
        {
            f |= VisualStateFlags.Invalid;
        }

        var enabled = IsEffectivelyEnabled;
        if (enabled)
        {
            f |= VisualStateFlags.Enabled;
            if (IsMouseOver || IsMouseCaptured) f |= VisualStateFlags.Hot;
            if ((IsFocused || IsFocusWithin) &&
                (FindVisualRoot() is not Window window || window.IsActive))
            {
                f |= VisualStateFlags.Focused;
            }
            if (IsPressed) f |= VisualStateFlags.Pressed;
        }
        return new VisualState { Flags = f };
    }

    internal override void OnBindingErrorChanged(int propertyId, BindingError? error)
    {
        bool changed;
        if (error?.Status != BindingStatus.ValidationError)
        {
            changed = _validationErrors?.Remove(propertyId) == true;
        }
        else
        {
            var property = MewPropertyRegistry.GetProperty(propertyId);
            if (property == null)
            {
                return;
            }

            var validationError = new ValidationError(property, error.Message);
            _validationErrors ??= new Dictionary<int, ValidationError>(capacity: 2);
            changed = !_validationErrors.TryGetValue(propertyId, out var previous) ||
                previous != validationError;
            _validationErrors[propertyId] = validationError;
        }

        if (!changed)
        {
            return;
        }

        IReadOnlyList<ValidationError> snapshot;
        if (_validationErrors == null || _validationErrors.Count == 0)
        {
            snapshot = EMPTY_VALIDATION_ERRORS;
        }
        else
        {
            var entries = _validationErrors.ToArray();
            Array.Sort(entries, static (left, right) => left.Key.CompareTo(right.Key));
            var errors = new ValidationError[entries.Length];
            for (int i = 0; i < entries.Length; i++)
            {
                errors[i] = entries[i].Value;
            }
            snapshot = Array.AsReadOnly(errors);
        }

        SetValue(ValidationErrorsPropertyKey, snapshot);
        SetValue(HasValidationErrorPropertyKey, snapshot.Count != 0);
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
    private bool _templateStale;

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
        if (_templateStale)
        {
            _templateStale = false;
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

    /// <summary>
    /// The slot a <see cref="ContentPresenter"/> projects when its ContentSource is not set.
    /// Null leaves such a presenter empty.
    /// </summary>
    private protected virtual MewProperty<Element?>? DefaultContentSource => null;

    internal MewProperty<Element?>? ResolveDefaultContentSource() => DefaultContentSource;

    internal void RefreshTemplatePresenters(MewProperty property)
    {
        var instance = _templateInstance;
        if (instance == null)
        {
            return;
        }

        for (int i = 0; i < instance.Presenters.Count; i++)
        {
            if (instance.Presenters[i].ResolvedContentSource == property)
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
        style?.Freeze();
        ValidateStyleTarget(style);

        var defaultStyle = style?.OverridesDefaultStyle == true
            ? null
            : ResolveFrameworkDefaultStyle();
        defaultStyle?.Freeze();
        ValidateStyleTarget(defaultStyle);

        _applicationStyleChain = BuildStyleIdentitySet(style);
        _defaultStyle = defaultStyle;
        _style = style;
        _styleContextVersion = ContextVersion;

        // Apply the full style chain (base setters + matching triggers) immediately so
        // layout-affecting properties and current-state visuals are correct before the
        // next Measure/Arrange/Render. ApplyStyleValues also replaces the previous final
        // winner map, so properties absent from the new style are cleared in the same pass.
        var flags = ComputeVisualState().Flags;
        _visualState = new VisualState { Flags = flags };
        ApplyStyleValues(flags, snap || _forceApplyStyle);
        _forceApplyStyle = false;

        InvalidateVisual();
    }

    private Style? ResolveFrameworkDefaultStyle()
    {
        for (Type? type = GetType();
             type != null && typeof(Control).IsAssignableFrom(type);
             type = type.BaseType)
        {
            var style = DefaultStyles.GetStyle(type);
            if (style != null)
            {
                return style;
            }
        }

        return null;
    }

    private void ValidateStyleTarget(Style? style)
    {
        if (style != null && !style.TargetType.IsAssignableFrom(GetType()))
        {
            throw new InvalidOperationException(
                $"Style targeting '{style.TargetType.FullName}' cannot be applied to " +
                $"control type '{GetType().FullName}'.");
        }
    }

    private static HashSet<Style>? BuildStyleIdentitySet(Style? style)
    {
        if (style == null)
        {
            return null;
        }

        var result = new HashSet<Style>(ReferenceEqualityComparer.Instance);
        for (var current = style; current != null; current = current.BasedOn)
        {
            result.Add(current);
        }

        return result;
    }

    /// <summary>
    /// Resolves the effective Style for this control from:
    /// 1. StyleName (named style from nearest StyleSheet)
    /// 2. StyleSheet type rule (nearest container's type-matched rule)
    /// The selected named or type-rule style is layered over the nearest framework default style.
    /// </summary>
    /// <param name="animate">When true, a runtime style swap applies with the new style's transitions.</param>
    internal void ResolveAndApplyStyle(bool animate = false)
    {
        StyleSheet? applicationStyleSheet = Application.IsRunning
            ? Application.Current.StyleSheet
            : null;
        // 1. StyleName → walk StyleSheet chain
        // 2. StyleSheet type rule → nearest container type-matched rule, which may name its style rather
        //    than hold it. Either way an unresolved name is the same situation, so it is handled once.
        var resolved = StyleScopeResolver.Resolve(
            this, _styleName, applicationStyleSheet, out string? unresolvedName);

        if (_styleName != null && resolved == null)
        {
            unresolvedName = _styleName;
        }

        if (unresolvedName != null)
        {
            bool isAttached = FindVisualRoot() is Window;
            if (!isAttached || applicationStyleSheet == null)
            {
                // A detached control or a headless tree without an Application does not yet
                // have the complete scope chain. Retry on attach or the next layout pass.
                _styleNameResolved = false;
                return;
            }

            string scopes = StyleScopeResolver.DescribeScopes(this, includesApplication: true);
            throw new InvalidOperationException(
                $"StyleName '{unresolvedName}' was not found for control type '{GetType().FullName}'. " +
                $"Searched scopes: {scopes}.");
        }

        _styleNameResolved = true;

        // Transitions only make sense for a runtime swap on an attached, already-styled
        // control; initial attach, theme change, and detached resolution snap.
        bool snap = !animate || (_style == null && _defaultStyle == null) || FindVisualRoot() is not Window;
        SetStyle(resolved, snap);
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
    /// Resolves the final Style candidate for each property and applies the difference from the
    /// previous map. StateTrigger values are provenance within the Style tier, not a separate
    /// property-system source.
    /// </summary>
    private void ApplyStyleValues(VisualStateFlags flags, bool snap = false)
    {
        _nextStyleValues ??= new();
        _nextStyleValues.Clear();
        CollectResolvedValues(
            _defaultStyle,
            flags,
            Theme,
            _nextStyleValues,
            _applicationStyleChain);
        CollectResolvedValues(_style, flags, Theme, _nextStyleValues, skippedStyles: null);

        if (_appliedStyleValues != null)
        {
            foreach (var pair in _appliedStyleValues)
            {
                if (!_nextStyleValues.ContainsKey(pair.Key))
                {
                    ApplyStyleCandidate(pair.Value.Property, value: null, hasValue: false, snap);
                }
            }
        }

        foreach (var pair in _nextStyleValues)
        {
            ApplyStyleCandidate(pair.Value.Property, pair.Value.Value, hasValue: true, snap);
        }

        (_appliedStyleValues, _nextStyleValues) = (_nextStyleValues, _appliedStyleValues);
    }

    private Dictionary<int, ResolvedStyleValue>? _appliedStyleValues;
    private Dictionary<int, ResolvedStyleValue>? _nextStyleValues;

    private readonly record struct ResolvedStyleValue(
        MewProperty Property,
        object Value,
        Style DeclaringStyle,
        StateTrigger? Trigger);

    internal StyleCascadeTrace GetStyleCascadeTrace(MewProperty property)
    {
        ArgumentNullException.ThrowIfNull(property);

        var entries = new List<StyleCascadeEntryTrace>();
        int finalEntryIndex = -1;
        CollectStyleCascadeTrace(
            _defaultStyle,
            StyleCascadeLayer.FrameworkDefault,
            isNewlyInherited: _style != null,
            _applicationStyleChain,
            property,
            _visualState.Flags,
            Theme,
            entries,
            ref finalEntryIndex);
        CollectStyleCascadeTrace(
            _style,
            StyleCascadeLayer.Application,
            isNewlyInherited: false,
            skippedStyles: null,
            property,
            _visualState.Flags,
            Theme,
            entries,
            ref finalEntryIndex);

        ResolvedStyleValue applied = default;
        bool hasStyleCandidate = _appliedStyleValues?.TryGetValue(property.Id, out applied) == true;
        object? styleValue = hasStyleCandidate ? applied.Value : null;

        if (finalEntryIndex >= 0)
        {
            var finalEntry = entries[finalEntryIndex];
            entries[finalEntryIndex] = finalEntry with
            {
                IsFinal = true,
                IsWinner = hasStyleCandidate && !finalEntry.IsUnset,
            };
        }

        var valueTrace = GetPropertyValueTrace(property);
        return new StyleCascadeTrace(
            property,
            entries.ToArray(),
            hasStyleCandidate,
            styleValue,
            valueTrace.EffectiveSource,
            valueTrace.IsAnimated);
    }

    private static void CollectStyleCascadeTrace(
        Style? style,
        StyleCascadeLayer layer,
        bool isNewlyInherited,
        HashSet<Style>? skippedStyles,
        MewProperty property,
        VisualStateFlags flags,
        Theme theme,
        List<StyleCascadeEntryTrace> entries,
        ref int finalEntryIndex)
    {
        if (style == null || skippedStyles?.Contains(style) == true)
        {
            return;
        }

        CollectStyleCascadeTrace(
            style.BasedOn,
            layer,
            isNewlyInherited,
            skippedStyles,
            property,
            flags,
            theme,
            entries,
            ref finalEntryIndex);

        CollectStyleCascadeSetters(
            style,
            trigger: null,
            layer,
            isNewlyInherited,
            style.Setters,
            property,
            isActive: true,
            theme,
            entries,
            ref finalEntryIndex);

        for (int i = 0; i < style.Triggers.Count; i++)
        {
            var trigger = style.Triggers[i];
            CollectStyleCascadeSetters(
                style,
                trigger,
                layer,
                isNewlyInherited,
                trigger.Setters,
                property,
                trigger.Matches(flags),
                theme,
                entries,
                ref finalEntryIndex);
        }
    }

    private static void CollectStyleCascadeSetters(
        Style style,
        StateTrigger? trigger,
        StyleCascadeLayer layer,
        bool isNewlyInherited,
        IReadOnlyList<SetterBase> setters,
        MewProperty property,
        bool isActive,
        Theme theme,
        List<StyleCascadeEntryTrace> entries,
        ref int finalEntryIndex)
    {
        for (int i = 0; i < setters.Count; i++)
        {
            var setter = setters[i];
            if (!ReferenceEquals(setter.Property, property))
            {
                continue;
            }

            bool isUnset = setter is UnsetSetter;
            bool hasResolvedValue = setter is Setter valueSetter &&
                (isActive || valueSetter.ThemeResolver == null);
            object? resolvedValue = hasResolvedValue
                ? ((Setter)setter).ResolveValue(theme)
                : null;
            int entryIndex = entries.Count;
            entries.Add(new StyleCascadeEntryTrace(
                style,
                trigger,
                layer,
                isNewlyInherited,
                isActive,
                isUnset,
                hasResolvedValue,
                resolvedValue,
                IsFinal: false,
                IsWinner: false));

            if (!isActive)
            {
                continue;
            }

            finalEntryIndex = entryIndex;
        }
    }

    private static void CollectResolvedValues(
        Style? style,
        VisualStateFlags flags,
        Theme theme,
        Dictionary<int, ResolvedStyleValue> result,
        HashSet<Style>? skippedStyles)
    {
        if (style == null || skippedStyles?.Contains(style) == true) return;

        // BasedOn first (lower priority - will be overwritten by derived)
        CollectResolvedValues(style.BasedOn, flags, theme, result, skippedStyles);

        // Base setters
        for (int i = 0; i < style.Setters.Count; i++)
        {
            if (style.Setters[i] is Setter s)
                result[s.Property.Id] = new(s.Property, s.ResolveValue(theme), style, Trigger: null);
            else if (style.Setters[i] is UnsetSetter u)
                result.Remove(u.Property.Id);
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
                        result[s.Property.Id] = new(s.Property, s.ResolveValue(theme), style, trigger);
                    else if (trigger.Setters[j] is UnsetSetter u)
                        result.Remove(u.Property.Id);
                }
            }
        }
    }

    private void ApplyStyleCandidate(MewProperty property, object? value, bool hasValue, bool snap)
    {
        object? from = PropertyStore.GetCurrentVisualValue(property.Id)
            ?? GetBindingValue(property);
        var mutation = hasValue
            ? PropertyStore.SetValue(property, value, ValueSource.Style)
            : PropertyStore.ClearSource(property.Id, ValueSource.Style);

        if (!snap && mutation.IsEffectiveChange && from != null && mutation.NewValue != null &&
            FindStyleTransition(property.Id) is Transition transition)
        {
            Animator.AnimateFromTo(
                property,
                from,
                mutation.NewValue,
                transition.Duration,
                transition.Easing);
        }
    }

    internal Transition? FindStyleTransition(int propertyId)
        => _style?.FindTransition(propertyId)
            ?? _defaultStyle?.FindTransition(propertyId);

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        // Re-resolve style with new theme's palette colors.
        ResolveAndApplyStyle();

        // A template instance is an artifact of the theme it was built under (builds may bake
        // metrics/colors), so it is rebuilt lazily; deferring the detach keeps the theme
        // broadcast walk from mutating the tree it is traversing.
        InvalidateTemplateInstance();
    }

    /// <summary>Marks the applied template instance for a lazy rebuild.</summary>
    private void InvalidateTemplateInstance()
    {
        if (_templateInstance == null)
        {
            return;
        }

        _templateStale = true;
        InvalidateMeasure();
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        if (newRoot == null)
        {
            // Detached from visual tree - release the resolved style reference.
            _defaultStyle = null;
            _style = null;
            _applicationStyleChain = null;
        }
        else
        {
            // Attached to visual tree - resolve style.
            ResolveAndApplyStyle();
        }
    }

    #endregion

    /// <summary>
    /// Notifies controls when an inherited font property changes on an ancestor.
    /// Called by the inheritance propagation system.
    /// </summary>
    internal void InvalidateFontCache(MewProperty property)
    {
        if (property.Id == FontFamilyProperty.Id ||
            property.Id == FontSizeProperty.Id ||
            property.Id == FontWeightProperty.Id)
        {
            OnFontCacheInvalidated(property);
        }
    }

    protected virtual void OnFontCacheInvalidated(MewProperty property)
    {
    }

    /// <summary>
    /// Returns this control's inherited font properties in text-engine form. The style folds to the
    /// engine's italic flag here: two states are all a font backend can tell apart today.
    /// </summary>
    protected TextRunStyle GetTextRunStyle()
        => new(FontFamily, FontSize, FontWeight, FontStyle == FontStyle.Italic);

    protected Size MeasureEngineText(
        ReadOnlySpan<char> text,
        double maxWidth = double.PositiveInfinity,
        TextWrapping wrapping = TextWrapping.NoWrap,
        bool transient = false)
    {
        if (text.IsEmpty)
        {
            return Size.Empty;
        }

        var style = GetTextRunStyle();
        return TextLayoutOperations.Measure(
            GetGraphicsFactory(), text.ToString(), GetDpi(), in style, maxWidth, wrapping, transient);
    }

    protected void DrawEngineText(
        IGraphicsContext context,
        ReadOnlySpan<char> text,
        Rect bounds,
        Color color,
        TextAlignment horizontalAlignment = TextAlignment.Left,
        TextAlignment verticalAlignment = TextAlignment.Top,
        TextWrapping wrapping = TextWrapping.NoWrap,
        TextTrimming trimming = TextTrimming.None,
        object? owner = null,
        bool transient = false)
    {
        if (text.IsEmpty || bounds.Width <= 0 || bounds.Height <= 0)
        {
            return;
        }

        var style = GetTextRunStyle();
        var layout = TextLayoutOperations.GetOrCreate(
            GetGraphicsFactory(),
            text.ToString(),
            GetDpi(),
            in style,
            bounds.Width,
            bounds.Height,
            wrapping,
            trimming,
            horizontalAlignment,
            transient: transient);
        TextLayoutOperations.DrawInBounds(
            context, layout, bounds, color, verticalAlignment, owner ?? this, transient: transient);
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);

        // Builds may bake device-pixel-snapped metrics, which the new scale invalidates.
        InvalidateTemplateInstance();
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
            // The background stops at the stroke's centre line, the way WPF draws its simple
            // border: filled to the outer contour it would sit under the stroke's outer
            // antialiased fringe and bleed past the border.
            var backgroundBounds = bounds;
            double backgroundRadius = radius;
            if (borderThickness > 0 && borderBrush.A > 0)
            {
                double inset = borderThickness / 2;
                backgroundBounds = bounds.Inflate(-inset, -inset);
                backgroundRadius = Math.Max(0, radius - inset);
            }

            if (backgroundBounds.Width > 0 && backgroundBounds.Height > 0)
            {
                if (backgroundRadius > 0)
                {
                    context.FillRoundedRectangle(backgroundBounds, backgroundRadius, backgroundRadius, background);
                }
                else
                {
                    context.FillRectangle(backgroundBounds, background);
                }
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
