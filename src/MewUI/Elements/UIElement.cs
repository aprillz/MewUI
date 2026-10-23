using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Diagnostics;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for elements that support input handling and visibility.
/// </summary>
public abstract partial class UIElement : Element
{
    private bool _suggestedIsEnabled = true;
    private bool _visualStateDirty;

    /// <summary>
    /// Whether the element is rendered and takes part in layout.
    /// </summary>
    public static readonly MewProperty<bool> IsVisibleProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsVisible), true,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsLayout);

    /// <summary>
    /// Whether the element is enabled for user interaction.
    /// </summary>
    public static readonly MewProperty<bool> IsEnabledProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsEnabled), true,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState);

    private static readonly MewPropertyKey<bool> IsEffectivelyEnabledPropertyKey =
        MewProperty<bool>.RegisterReadOnly<UIElement>(nameof(IsEffectivelyEnabled), true,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState);

    /// <summary>
    /// Whether this element and every ancestor are enabled; read-only, derived from <see cref="IsEnabled"/> and the parent chain.
    /// </summary>
    public static readonly MewProperty<bool> IsEffectivelyEnabledProperty =
        IsEffectivelyEnabledPropertyKey.Property;

    /// <summary>
    /// Whether the element has keyboard focus; read-only, set by <see cref="Input.FocusManager"/>.
    /// </summary>
    public static readonly MewProperty<bool> IsFocusedProperty =
        MewProperty<bool>.RegisterReadOnly<UIElement>(nameof(IsFocused), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState).Property;

    /// <summary>
    /// Whether this element or any of its descendants has keyboard focus; read-only, derived from the focus chain.
    /// </summary>
    public static readonly MewProperty<bool> IsFocusWithinProperty =
        MewProperty<bool>.RegisterReadOnly<UIElement>(nameof(IsFocusWithin), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState).Property;

    /// <summary>
    /// Whether the mouse is over this element; read-only, set by the input router.
    /// </summary>
    public static readonly MewProperty<bool> IsMouseOverProperty =
        MewProperty<bool>.RegisterReadOnly<UIElement>(nameof(IsMouseOver), false,
            MewPropertyOptions.AffectsVisualState).Property;

    /// <summary>
    /// Whether this element has mouse capture; read-only, set via <see cref="Window.CaptureMouse(UIElement)"/>.
    /// </summary>
    public static readonly MewProperty<bool> IsMouseCapturedProperty =
        MewProperty<bool>.RegisterReadOnly<UIElement>(nameof(IsMouseCaptured), false,
            MewPropertyOptions.AffectsVisualState).Property;

    /// <summary>
    /// The opacity of the element and everything under it, from 0 (invisible) to 1 (opaque); it affects
    /// neither layout nor hit testing, so a fully transparent element still occupies its box and takes the pointer.
    /// </summary>
    public static readonly MewProperty<double> OpacityProperty =
        MewProperty<double>.Register<UIElement>(nameof(Opacity), 1.0,
            MewPropertyOptions.AffectsRender);

    /// <summary>
    /// Whether the element participates in hit testing.
    /// </summary>
    public static readonly MewProperty<bool> IsHitTestVisibleProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsHitTestVisible), true,
            MewPropertyOptions.None);

    /// <summary>
    /// The position in the Tab order; non-negative finite values are visited ascending before elements
    /// without one, and NaN (the default), negative and infinite values keep tree order.
    /// </summary>
    public static readonly MewProperty<double> TabIndexProperty =
        MewProperty<double>.Register<UIElement>(nameof(TabIndex), double.NaN);

    /// <summary>
    /// Whether a focusable element is in the Tab order; false removes it from Tab traversal only,
    /// leaving click and programmatic focus unaffected.
    /// </summary>
    /// <remarks>
    /// Non-focusable elements are never promoted into the Tab order by this property. Control authors
    /// exclude internal parts (constructor/style); apps exclude individual instances.
    /// </remarks>
    public static readonly MewProperty<bool> IsTabStopProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsTabStop), true);

    /// <summary>
    /// Whether the element can receive keyboard focus.
    /// </summary>
    /// <remarks>
    /// Control authors set the per-type default (via <see cref="MewProperty{T}.OverrideDefaultValue{TOwner}"/>
    /// in a static constructor); apps may override individual instances, for example to drop a composed
    /// part from focus. Setting this false on the currently focused element does not move focus
    /// immediately; the next Tab or click resolves it.
    /// </remarks>
    public static readonly MewProperty<bool> FocusableProperty =
        MewProperty<bool>.Register<UIElement>(nameof(Focusable), false);

    /// <summary>
    /// Whether the element is drawn even when it lies outside the viewport; inherited by descendants.
    /// </summary>
    /// <remarks>
    /// The viewport cull in <see cref="Render"/> already follows transforms applied through the drawing
    /// context; set this on children whose visible area differs from their layout bounds in some other way.
    /// </remarks>
    public static readonly MewProperty<bool> SkipViewportCullProperty =
        MewProperty<bool>.Register<UIElement>(nameof(SkipViewportCull), false,
            MewPropertyOptions.Inherits);

    /// <summary>
    /// Gets or sets whether the element is drawn even when it lies outside the viewport; inherited
    /// by descendants.
    /// </summary>
    public bool SkipViewportCull
    {
        get => GetValue(SkipViewportCullProperty);
        set => SetValue(SkipViewportCullProperty, value);
    }

    /// <summary>
    /// The cursor shown while the mouse is over this element; null inherits from the parent or the
    /// platform default, and <see cref="CursorType.None"/> hides the cursor.
    /// </summary>
    public static readonly MewProperty<CursorType?> CursorProperty =
        MewProperty<CursorType?>.Register<UIElement>(nameof(Cursor), null,
            MewPropertyOptions.None);

    /// <summary>
    /// Re-resolves the effective enabled state when the parent chain changes.
    /// </summary>
    protected override void OnParentChanged()
    {
        base.OnParentChanged();

        // Both directions matter: attaching may inherit a disabled ancestor, detaching drops one.
        RefreshEnabledSubtree();
    }

    /// <summary>
    /// Called when a property value changes; invalidates layout or rendering as the property declares and applies property-specific side effects.
    /// </summary>
    protected override void OnMewPropertyChanged(MewProperty property)
    {
        if (Automation.AutomationGate.IsSupported)
        {
            AutomationPeerIfCreated?.NotifyOwnerPropertyChanged(property);
        }

        if (property.AffectsLayout)
            InvalidateMeasure();
        else if (property == OpacityProperty)
            RaiseRenderDirty(Rendering.Retained.RenderDirtyKind.State);
        else if (property.AffectsRender)
            InvalidateVisual();

        if (property.AffectsVisualState)
            InvalidateVisualState();

        if (property.Inherits)
            PropagateInheritedPropertyChange(property);

        if (property == IsVisibleProperty)
        {
            RaiseRenderDirty(Rendering.Retained.RenderDirtyKind.State);
            if (!IsVisible)
            {
                MarkSubtreeCulled();

                // A hidden holder, or one under this element, cannot receive the release that would end its capture.
                (FindVisualRoot() as Window)?.ReleaseCaptureWithin(this);
            }

            OnVisibilityChanged();
        }
        else if (property == IsEnabledProperty)
        {
            RefreshEnabledSubtree();
        }
        else if (property == IsEffectivelyEnabledProperty)
        {
            // Descendants refresh their own effective state, so only the holder itself is checked here.
            if (!IsEffectivelyEnabled && IsMouseCaptured)
            {
                (FindVisualRoot() as Window)?.ReleaseCaptureWithin(this);
            }

            OnEnabledChanged();
        }
        else if (property == IsFocusedProperty)
        {
            if (IsFocused)
            {
                OnGotFocus();
                GotFocus?.Invoke();
            }
            else
            {
                OnLostFocus();
                LostFocus?.Invoke();
            }
        }
        else if (property == IsMouseOverProperty)
        {
            if (IsMouseOver)
            {
                OnMouseEnter();
                MouseEnter?.Invoke();
            }
            else
            {
                OnMouseLeave();
                MouseLeave?.Invoke();
            }
        }
        else if (property == CursorProperty)
        {
            // If the mouse is currently over this element, update the window cursor immediately.
            if (IsMouseOver && FindVisualRoot() is Window window)
            {
                window.UpdateCursorForElement(this);
            }
        }
    }

    private void PropagateInheritedPropertyChange(MewProperty property)
    {
        if (this is not IVisualTreeHost host) return;

        host.VisitChildren(child =>
        {
            PropagateToDescendant(child, property);
            return true;
        });
    }

    private static void PropagateToDescendant(Element child, MewProperty property)
    {
        if (child is UIElement element && element.HasPropertyStore)
        {
            var source = element.PropertyStore.GetSource(property.Id);
            // Stop propagation if child has its own value (local, trigger, or style)
            if (source > ValueSource.Inherited)
                return;

            if (element.HasChangeObservers(property.Id))
            {
                // A binding observes this inherited property: clearing the cache lazily would never
                // notify it (inherited changes don't run the normal notification pipeline). Eagerly
                // re-resolve, cache the fresh value, and fire observers so the binding updates.
                object? oldValue = element.PropertyStore.GetBoxedValue(property);
                element.PropertyStore.ClearInherited(property.Id);
                object? newValue = element.ResolveInheritedValueBoxed(property);
                if (!Equals(oldValue, newValue))
                    element.NotifyObserversBoxed(property, oldValue, newValue);
            }
            else if (source == ValueSource.Inherited)
            {
                // Clear cached inherited value so it will be re-resolved on next access
                element.PropertyStore.ClearInherited(property.Id);
            }
        }

        // Invalidate font cache on controls for font property changes
        if (child is Control control)
            control.InvalidateFontCache(property);

        if (child is UIElement uiChild)
        {
            if (property.AffectsLayout)
                uiChild.InvalidateMeasure();
            else if (property.AffectsRender)
                uiChild.InvalidateVisual();
        }

        // Continue to grandchildren
        if (child is IVisualTreeHost childHost)
        {
            childHost.VisitChildren(grandchild =>
            {
                PropagateToDescendant(grandchild, property);
                return true;
            });
        }
    }

    /// <summary>
    /// Gets or sets whether the element is visible.
    /// </summary>
    public bool IsVisible
    {
        get => GetValue(IsVisibleProperty);
        set => SetValue(IsVisibleProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the element is enabled for input.
    /// </summary>
    public bool IsEnabled
    {
        get => GetValue(IsEnabledProperty);
        set => SetValue(IsEnabledProperty, value);
    }

    /// <summary>
    /// Gets whether this element and every ancestor are enabled; input and the disabled appearance follow this, not <see cref="IsEnabled"/>.
    /// </summary>
    public bool IsEffectivelyEnabled => GetValue(IsEffectivelyEnabledProperty);

    /// <summary>
    /// Gets or sets the opacity of the element and its subtree, from 0 to 1.
    /// </summary>
    public double Opacity
    {
        get => GetValue(OpacityProperty);
        set => SetValue(OpacityProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the element participates in hit testing.
    /// </summary>
    public bool IsHitTestVisible
    {
        get => GetValue(IsHitTestVisibleProperty);
        set => SetValue(IsHitTestVisibleProperty, value);
    }

    /// <summary>Gets or sets the position in the Tab order.</summary>
    public double TabIndex
    {
        get => GetValue(TabIndexProperty);
        set => SetValue(TabIndexProperty, value);
    }

    /// <summary>Gets or sets whether the element participates in Tab traversal.</summary>
    public bool IsTabStop
    {
        get => GetValue(IsTabStopProperty);
        set => SetValue(IsTabStopProperty, value);
    }

    /// <summary>
    /// Gets or sets the cursor shown while the mouse is over this element; null inherits from the
    /// parent or the platform default, and <see cref="CursorType.None"/> hides the cursor.
    /// </summary>
    public CursorType? Cursor
    {
        get => GetValue(CursorProperty);
        set => SetValue(CursorProperty, value);
    }

    /// <summary>
    /// Gets whether the element has keyboard focus.
    /// </summary>
    public bool IsFocused => GetValue(IsFocusedProperty);

    /// <summary>
    /// Gets whether this element or any of its descendants has keyboard focus.
    /// </summary>
    /// <remarks>
    /// Container visuals (a tab control outline, for example) and container-level focus navigation read this.
    /// </remarks>
    public bool IsFocusWithin => GetValue(IsFocusWithinProperty);

    /// <summary>
    /// Gets whether the mouse is over this element.
    /// </summary>
    public bool IsMouseOver => GetValue(IsMouseOverProperty);

    /// <summary>
    /// Gets whether this element has mouse capture.
    /// </summary>
    public bool IsMouseCaptured => GetValue(IsMouseCapturedProperty);

    /// <summary>
    /// Gets or sets whether this element can receive keyboard focus.
    /// </summary>
    public bool Focusable
    {
        get => GetValue(FocusableProperty);
        set => SetValue(FocusableProperty, value);
    }

    #region Events (using Action delegates for AOT compatibility)

    /// <summary>
    /// Occurs when the element receives keyboard focus.
    /// </summary>
    public event Action? GotFocus;

    /// <summary>
    /// Occurs when the element loses keyboard focus.
    /// </summary>
    public event Action? LostFocus;

    /// <summary>
    /// Occurs when the mouse enters the element.
    /// </summary>
    public event Action? MouseEnter;

    /// <summary>
    /// Occurs when the mouse leaves the element.
    /// </summary>
    public event Action? MouseLeave;

    /// <summary>
    /// Occurs when a mouse button is pressed over the element.
    /// </summary>
    public event Action<MouseEventArgs>? MouseDown;

    /// <summary>
    /// Occurs when the user double-clicks a mouse button over the element.
    /// </summary>
    public event Action<MouseEventArgs>? MouseDoubleClick;

    /// <summary>
    /// Occurs when a mouse button is released over the element.
    /// </summary>
    public event Action<MouseEventArgs>? MouseUp;

    /// <summary>
    /// Occurs when the mouse moves over the element.
    /// </summary>
    public event Action<MouseEventArgs>? MouseMove;

    /// <summary>
    /// Occurs when the mouse wheel is scrolled over the element.
    /// </summary>
    public event Action<MouseWheelEventArgs>? MouseWheel;

    /// <summary>
    /// Occurs when a key is pressed while the element has focus; marking it handled also drops the
    /// text input the keystroke would produce.
    /// </summary>
    public event Action<KeyEventArgs>? KeyDown;

    /// <summary>
    /// Occurs when a key is released while the element has focus.
    /// </summary>
    public event Action<KeyEventArgs>? KeyUp;

    #endregion

    protected override Size MeasureCore(Size availableSize)
    {
        if (!IsVisible)
        {
            return Size.Empty;
        }

        return MeasureOverride(availableSize);
    }

    protected virtual Size MeasureOverride(Size availableSize) => Size.Empty;

    protected override void ArrangeCore(Rect finalRect)
    {
        if (!IsVisible)
        {
            return;
        }

        ArrangeOverride(new Size(finalRect.Width, finalRect.Height));
    }

    protected virtual Size ArrangeOverride(Size finalSize) => finalSize;

    // Render-time cull viewport in the space of the frame's root context: the client rect of the surface being
    // drawn. Bounds are carried into that space through the context transform, so a subtree drawn under a
    // transform (a portal, a rotation) is judged where it lands. null (no active window frame) disables the cull.
    [ThreadStatic] private static Rect? _renderCullViewport;

    internal static Rect? RenderCullViewport
    {
        get => _renderCullViewport;
        set => _renderCullViewport = value;
    }

    /// <summary>
    /// Renders the element and its subtree unless it is invisible, fully transparent or outside the viewport.
    /// </summary>
    public sealed override void Render(IGraphicsContext context)
    {
        if (!IsVisible)
        {
            return;
        }

        double opacity = Opacity;
        if (opacity <= 0)
        {
            return;
        }

        bool outsideViewport =
            !SkipViewportCull && this is not Window &&
            _renderCullViewport is Rect cullViewport && !cullViewport.IntersectsWith(DrawnBounds(context));

        if (outsideViewport && _cacheSnapshotDepth == 0)
        {
            MarkSubtreeCulled();
            return;
        }

        if (outsideViewport)
        {
            // A cache snapshot has to fill its whole surface, so this still draws. What it draws never
            // reaches the screen while the cached element is scrolled this far, so the element stays
            // marked as culled and its later repaints only bump the cache version.
            MarkCulledWhileCaching();
        }
        else
        {
            MarkRendered();
        }

        using (DevToolsGate.IsSupported ? PerformanceProfiler.Instance.SampleElement(GetType(), ProfilerSampleCategory.Render, this) : default)
        {
            // Outside the cache branch: the cached bitmap is the element's own pixels, so fading it
            // here leaves the cache reusable while the opacity animates.
            if (opacity < 1)
            {
                var group = BeginOpacityGroup(context, opacity);
                try
                {
                    RenderVisual(group.Target);
                }
                finally
                {
                    group.End();
                }
            }
            else
            {
                RenderVisual(context);
            }
        }
    }

    /// <summary>
    /// The box this element covers in the cull viewport's space. A cache snapshot draws into its own surface,
    /// whose transform does not lead to the viewport, so there the layout box stands in as before.
    /// </summary>
    private Rect DrawnBounds(IGraphicsContext context)
    {
        if (_cacheSnapshotDepth > 0)
        {
            return Bounds;
        }

        var transform = context.GetTransform();
        if (transform.IsIdentity)
        {
            return Bounds;
        }
        else
        {
            return Rendering.Retained.RetainedGeometry.TransformRect(Bounds, transform);
        }
    }

    private OpacityGroup BeginOpacityGroup(IGraphicsContext context, double opacity)
    {
        // What a faded visual can draw on is not known before it draws, so the group covers all of the
        // frame that is in view. Outside a window frame nothing says how far that is.
        if (_renderCullViewport is not Rect viewport)
        {
            return OpacityGroup.Begin(context, null, opacity, default);
        }

        var factory = (FindVisualRoot() as Window)?.GraphicsFactory ?? Application.DefaultGraphicsFactory;
        return OpacityGroup.Begin(context, factory, opacity, Rendering.Retained.RetainedGeometry.TransformRect(viewport, context.GetTransform()));
    }

    private void RenderVisual(IGraphicsContext context)
    {
        if (_hasBitmapCache)
        {
            RenderCached(context);
            return;
        }

        // OnRender opens with an opaque fill, so everything after it - text above all - lands on
        // pixels the backend knows, which is what subpixel antialiasing needs on a surface that
        // carries per-pixel alpha (a popup). The scope has to start before OnRender: controls
        // such as ContextMenu and ListBox draw their own text there rather than in the subtree.
        if (this is Control { Background.A: 255 })
        {
            OpaqueBackdropScope.Begin(context, Bounds);
            try
            {
                OnRender(context);
                RenderSubtree(context);
            }
            finally
            {
                context.EndOpaqueBackdrop();
            }
        }
        else
        {
            OnRender(context);
            RenderSubtree(context);
        }
    }

    /// <summary>
    /// Resolves visual state (style triggers, state transitions). Called from the visual-state
    /// update (<see cref="Window.UpdateVisualStates"/>) before layout reads state-dependent values.
    /// </summary>
    /// <param name="snap">
    /// When true, target values are applied immediately (no animation). Used when the element is
    /// offscreen at update time (animating invisible pixels is wasteful) or when a hard state
    /// change must take effect before the next frame (style/theme reset).
    /// </param>
    protected virtual void ResolveVisualState(bool snap) { }

    /// <summary>
    /// Queues this element for visual-state reconciliation at the start of the next layout pass; call it
    /// when a property that feeds <see cref="Controls.Control.ComputeVisualState"/> changes.
    /// </summary>
    /// <remarks>
    /// Repeated calls before the pass runs are merged, and a detached element is skipped since
    /// attach-time style resolution recomputes its state.
    /// </remarks>
    public void InvalidateVisualState()
    {
        if (_visualStateDirty)
        {
            return;
        }

        // Detached elements skip the queue: attach-time style resolution recomputes the state.
        if (FindVisualRoot() is not Window window)
        {
            return;
        }

        _visualStateDirty = true;
        window.RegisterVisualStateDirty(this);

        // The visual-state update is the first step of the update pass; a render-only request
        // would never run it and the queued state change would starve.
        window.RequestUpdatePass();
    }

    internal bool IsVisualStateDirty => _visualStateDirty;

    internal void ClearVisualStateDirty() => _visualStateDirty = false;

    internal void ResolveVisualStateInternal(bool snap) => ResolveVisualState(snap);

    /// <summary>
    /// Renders the element's own visuals (background, border, text, etc.).
    /// </summary>
    /// <param name="context">The graphics context.</param>
    protected override void OnRender(IGraphicsContext context) { }

    /// <summary>
    /// Renders child elements and internal visual parts (content, chrome, scrollbars, etc.).
    /// Called after <see cref="OnRender"/>.
    /// </summary>
    protected virtual void RenderSubtree(IGraphicsContext context) { }

    /// <summary>
    /// Performs hit testing to find the element at the specified point.
    /// </summary>
    /// <param name="point">The point in window coordinates, the space of <see cref="Element.Bounds"/>.</param>
    /// <returns>The element at the point, or null.</returns>
    public UIElement? HitTest(Point point) => OnHitTest(point);

    protected virtual UIElement? OnHitTest(Point point)
    {
        // A disabled element still participates in hit testing so it absorbs the pointer instead of
        // letting it fall through to whatever is behind it; controls gate their own input on
        // IsEffectivelyEnabled, which propagates to the whole disabled subtree.
        if (!IsVisible || !IsHitTestVisible)
        {
            return null;
        }

        if (!Bounds.Contains(point))
        {
            return null;
        }

        // VisitChildren yields participating children topmost-first, so the first hit wins;
        // hosts whose yield order is not front-to-back keep their own override.
        if (this is IVisualTreeHost host)
        {
            UIElement? childHit = null;
            host.VisitChildren(child =>
            {
                if (child is UIElement childElement && childElement.HitTest(point) is UIElement hit)
                {
                    childHit = hit;
                    return false;
                }
                return true;
            });
            if (childHit != null)
            {
                return childHit;
            }
        }

        return this;
    }

    /// <summary>
    /// Attempts to focus this element.
    /// </summary>
    /// <returns>True if focus was set; otherwise, false.</returns>
    public bool Focus()
    {
        if (!Focusable || !IsEffectivelyEnabled || !IsVisible)
        {
            return false;
        }

        var root = FindVisualRoot();
        if (root is Window window)
        {
            return window.SetFocusedElement(this);
        }
        return false;
    }

    /// <summary>
    /// Allows focusable containers to redirect focus to a default descendant (WinForms-style).
    /// </summary>
    internal virtual UIElement GetDefaultFocusTarget() => this;

    internal void SetFocused(bool focused)
        => PropertyStore.SetLocal(IsFocusedProperty, focused);

    internal void SetFocusWithin(bool focusWithin)
        => PropertyStore.SetLocal(IsFocusWithinProperty, focusWithin);

    internal void SetMouseOver(bool mouseOver)
        => PropertyStore.SetLocal(IsMouseOverProperty, mouseOver);

    internal void SetMouseCaptured(bool captured)
        => PropertyStore.SetLocal(IsMouseCapturedProperty, captured);

    /// <summary>
    /// Converts a point from element coordinates in DIPs to screen coordinates in device pixels.
    /// </summary>
    /// <param name="point">The point in element coordinates.</param>
    /// <returns>The point in screen coordinates in device pixels.</returns>
    /// <exception cref="InvalidOperationException">The element is not connected to a shown window.</exception>
    public Point PointToScreen(Point point)
    {
        var root = FindVisualRoot();
        if (root is not Window window || window.Handle == 0)
        {
            throw new InvalidOperationException("The visual is not connected to a window.");
        }

        var inWindow = TranslatePoint(point, window);
        var surface = ResolveInputHostWindow() ?? window;
        return surface.ClientToScreen(surface.VisualTreePointToSurface(inWindow));
    }

    /// <summary>
    /// Converts a point from screen coordinates in device pixels to element coordinates in DIPs.
    /// </summary>
    /// <param name="point">The point in screen coordinates in device pixels.</param>
    /// <returns>The point in element coordinates.</returns>
    /// <exception cref="InvalidOperationException">The element is not connected to a shown window.</exception>
    public Point PointFromScreen(Point point)
    {
        var root = FindVisualRoot();
        if (root is not Window window || window.Handle == 0)
        {
            throw new InvalidOperationException("The visual is not connected to a window.");
        }

        var surface = ResolveInputHostWindow() ?? window;
        var inWindow = surface.SurfacePointToVisualTree(surface.ScreenToClient(point));
        return window.TranslatePoint(inWindow, this);
    }

    /// <summary>
    /// Converts a rectangle from element coordinates in DIPs to screen coordinates in device pixels.
    /// </summary>
    /// <param name="rect">The rectangle in element coordinates.</param>
    /// <returns>The bounding box of the rectangle in screen coordinates in device pixels.</returns>
    /// <exception cref="InvalidOperationException">The element is not connected to a shown window.</exception>
    public Rect RectToScreen(Rect rect)
    {
        var tl = PointToScreen(rect.TopLeft);
        var br = PointToScreen(rect.BottomRight);
        return new Rect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
    }

    /// <summary>
    /// Converts a rectangle from screen coordinates in device pixels to element coordinates in DIPs.
    /// </summary>
    /// <param name="rect">The rectangle in screen coordinates in device pixels.</param>
    /// <returns>The bounding box of the rectangle in element coordinates.</returns>
    /// <exception cref="InvalidOperationException">The element is not connected to a shown window.</exception>
    public Rect RectFromScreen(Rect rect)
    {
        var tl = PointFromScreen(rect.TopLeft);
        var br = PointFromScreen(rect.BottomRight);
        return new Rect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
    }

    /// <summary>
    /// Recomputes this element's own effective enabled state. Descendants are left untouched, so
    /// callers that are not already walking the subtree must use <see cref="RefreshEnabledSubtree"/>.
    /// </summary>
    internal void ReevaluateSuggestedIsEnabled()
    {
        _suggestedIsEnabled = ComputeIsEnabledSuggestionSafe();
        SetValue(IsEffectivelyEnabledPropertyKey, IsEnabled && _suggestedIsEnabled);
    }

    /// <summary>
    /// Recomputes the effective enabled state of this element and every descendant.
    /// </summary>
    internal void RefreshEnabledSubtree()
    {
        // Pre-order: a child reads its parent's effective value, so the parent must settle first.
        VisualTree.Visit(this, static element =>
        {
            if (element is UIElement uiElement)
            {
                uiElement.ReevaluateSuggestedIsEnabled();
            }
        });
    }

    protected virtual bool ComputeIsEnabledSuggestion() => true;

    private bool ComputeIsEnabledSuggestionSafe()
    {
        bool local;
        try { local = ComputeIsEnabledSuggestion(); }
        catch { return true; }

        if (!local)
        {
            return false;
        }

        if (Parent is UIElement parent)
        {
            return parent.IsEffectivelyEnabled;
        }

        return true;
    }

    internal void DisposeBindings()
    {
        DisposePropertyBindings();
    }

    #region Input Handlers

    /// <summary>
    /// Called when the element receives focus.
    /// </summary>
    protected virtual void OnGotFocus()
    { }

    /// <summary>
    /// Called when the element loses focus.
    /// </summary>
    protected virtual void OnLostFocus()
    { }

    /// <summary>
    /// Called when the mouse enters the element.
    /// </summary>
    protected virtual void OnMouseEnter()
    { }

    /// <summary>
    /// Called when the mouse leaves the element.
    /// </summary>
    protected virtual void OnMouseLeave()
    { }

    internal void RaiseMouseDown(MouseEventArgs e) => OnMouseDown(e);

    internal void RaiseMouseDoubleClick(MouseEventArgs e) => OnMouseDoubleClick(e);

    internal void RaiseMouseUp(MouseEventArgs e) => OnMouseUp(e);

    internal void RaiseMouseMove(MouseEventArgs e) => OnMouseMove(e);

    internal void RaiseMouseWheel(MouseWheelEventArgs e) => OnMouseWheel(e);

    internal void RaiseKeyDown(KeyEventArgs e) => OnKeyDown(e);

    internal void RaiseKeyUp(KeyEventArgs e) => OnKeyUp(e);

    // Protected virtual hooks for derived controls (public API surface stays small).
    /// <summary>
    /// Called when a mouse button is pressed.
    /// </summary>
    /// <param name="e">Mouse event arguments.</param>
    protected virtual void OnMouseDown(MouseEventArgs e) => MouseDown?.Invoke(e);

    /// <summary>
    /// Called when a mouse button is double-clicked.
    /// </summary>
    /// <param name="e">Mouse event arguments.</param>
    protected virtual void OnMouseDoubleClick(MouseEventArgs e) => MouseDoubleClick?.Invoke(e);

    /// <summary>
    /// Called when a mouse button is released.
    /// </summary>
    /// <param name="e">Mouse event arguments.</param>
    protected virtual void OnMouseUp(MouseEventArgs e) => MouseUp?.Invoke(e);

    /// <summary>
    /// Called when the mouse moves.
    /// </summary>
    /// <param name="e">Mouse event arguments.</param>
    protected virtual void OnMouseMove(MouseEventArgs e) => MouseMove?.Invoke(e);

    /// <summary>
    /// Called when the mouse wheel is scrolled.
    /// </summary>
    /// <param name="e">Mouse wheel event arguments.</param>
    protected virtual void OnMouseWheel(MouseWheelEventArgs e) => MouseWheel?.Invoke(e);

    /// <summary>
    /// Called when a key is pressed.
    /// </summary>
    /// <param name="e">Key event arguments.</param>
    protected virtual void OnKeyDown(KeyEventArgs e) => KeyDown?.Invoke(e);

    /// <summary>
    /// Called when a key is released.
    /// </summary>
    /// <param name="e">Key event arguments.</param>
    protected virtual void OnKeyUp(KeyEventArgs e) => KeyUp?.Invoke(e);

    /// <summary>
    /// Called when the element's access key is activated.
    /// Override to define control-specific activation behavior.
    /// Default: bubbles up the parent chain until a handler processes it.
    /// </summary>
    internal virtual void OnAccessKey()
    {
        if (Parent is UIElement parentUi)
            parentUi.OnAccessKey();
    }

    #endregion

    internal override void OnDetaching(Element? oldRoot)
    {
        base.OnDetaching(oldRoot);

        // Keep the single-focus invariant: a focused element leaving the tree would otherwise strand
        // FocusManager.FocusedElement on a detached element (stale IsFocused / focus border). Release it
        // here, while still attached, so focus-within unwinds up the retained ancestor chain.
        if (oldRoot is Window window)
        {
            // A holder leaving the tree can no longer receive the release that would end its capture.
            window.ReleaseCaptureWithin(this);

            if (IsFocused || IsFocusWithin)
            {
                window.FocusManager.ClearFocus();
            }
        }
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        if (newRoot == null)
        {
            // Stop property animations when detached from the visual tree.
            StopAllPropertyAnimations();

            // Release any cached GPU surface/image; re-attaching rebuilds it on next render.
            DisposeCacheEntry();

            // A popup lives in the window's popup layer, not under this element, so nothing else detaches
            // it when the element it is anchored to leaves (virtualization recycling, tab switch).
            if (oldRoot is Window oldWindow)
            {
                oldWindow.ClosePopupsOwnedBy(this);
            }
        }
    }

    /// <summary>
    /// Called when visibility changes.
    /// </summary>
    protected virtual void OnVisibilityChanged()
    { }

    /// <summary>
    /// Called when enabled state changes.
    /// </summary>
    protected virtual void OnEnabledChanged()
    { }

    #region Animation

    private Animation.PropertyAnimator? _animator;

    /// <summary>
    /// Gets or creates the property animator for this element.
    /// Used by the style system to drive animated transitions.
    /// </summary>
    internal Animation.PropertyAnimator Animator
        => _animator ??= new Animation.PropertyAnimator(this, PropertyStore);

    /// <summary>
    /// Stops all running property animations (e.g. when detached from the visual tree).
    /// </summary>
    internal void StopAllPropertyAnimations()
    {
        _animator?.StopAll();
    }

    #endregion
}
