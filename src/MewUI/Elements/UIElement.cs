using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Diagnostics;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for elements that support input handling and visibility.
/// </summary>
public abstract partial class UIElement : Element
{
    private bool _suggestedIsEnabled = true;
    private bool _suggestedIsEnabledInitialized;
    private bool _visualStateDirty;
    private bool _resolvingVisualState;

    /// <summary>
    /// Controls visibility. When false, the element is not rendered and does not participate in layout.
    /// </summary>
    public static readonly MewProperty<bool> IsVisibleProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsVisible), true,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsLayout);

    /// <summary>
    /// Controls whether the element is enabled for user interaction.
    /// </summary>
    public static readonly MewProperty<bool> IsEnabledProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsEnabled), true,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState);

    /// <summary>
    /// Whether the element has keyboard focus. Read-only; set by <see cref="Input.FocusManager"/>.
    /// </summary>
    public static readonly MewProperty<bool> IsFocusedProperty =
        MewProperty<bool>.RegisterReadOnly<UIElement>(nameof(IsFocused), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState).Property;

    /// <summary>
    /// Whether this element or any of its descendants has keyboard focus.
    /// Read-only; derived from the focus chain.
    /// </summary>
    public static readonly MewProperty<bool> IsFocusWithinProperty =
        MewProperty<bool>.RegisterReadOnly<UIElement>(nameof(IsFocusWithin), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState).Property;

    /// <summary>
    /// Whether the mouse is over this element. Read-only; set by the input router.
    /// </summary>
    public static readonly MewProperty<bool> IsMouseOverProperty =
        MewProperty<bool>.RegisterReadOnly<UIElement>(nameof(IsMouseOver), false,
            MewPropertyOptions.AffectsVisualState).Property;

    /// <summary>
    /// Whether this element has mouse capture. Read-only; set via <see cref="Window.CaptureMouse"/>.
    /// </summary>
    public static readonly MewProperty<bool> IsMouseCapturedProperty =
        MewProperty<bool>.RegisterReadOnly<UIElement>(nameof(IsMouseCaptured), false,
            MewPropertyOptions.AffectsVisualState).Property;

    /// <summary>
    /// Controls whether the element participates in hit testing.
    /// </summary>
    public static readonly MewProperty<bool> IsHitTestVisibleProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsHitTestVisible), true,
            MewPropertyOptions.None);

    /// <summary>
    /// Position in the Tab order. Non-negative finite values are visited ascending, before
    /// elements without an explicit index; NaN (default), negative and infinite values keep tree order.
    /// </summary>
    public static readonly MewProperty<double> TabIndexProperty =
        MewProperty<double>.Register<UIElement>(nameof(TabIndex), double.NaN);

    /// <summary>
    /// Whether a focusable element participates in Tab traversal. False only removes it from the
    /// Tab order; click and programmatic focus are unaffected. Non-focusable elements are never
    /// promoted into the Tab order by this property. Control authors exclude internal parts
    /// (constructor/style); apps exclude individual instances.
    /// </summary>
    public static readonly MewProperty<bool> IsTabStopProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsTabStop), true);

    /// <summary>
    /// Whether the element can receive keyboard focus. Control authors set the per-type default
    /// (via <see cref="MewProperty{T}.OverrideDefaultValue{TOwner}"/> in a static constructor);
    /// apps may override individual instances, for example to drop a composed part from focus.
    /// Setting this false on the currently focused element does not move focus immediately; the
    /// next Tab or click resolves it.
    /// </summary>
    public static readonly MewProperty<bool> FocusableProperty =
        MewProperty<bool>.Register<UIElement>(nameof(Focusable), false);

    /// <summary>
    /// When <see langword="true"/>, the viewport-bounds cull check in <see cref="Render"/> is skipped.
    /// Set this on children whose layout bounds do not reflect their actual visible area
    /// (e.g. children rendered under a parent-applied scale/rotation transform).
    /// Inherits to descendants so deep child trees under a transform host are not culled.
    /// </summary>
    public static readonly MewProperty<bool> SkipViewportCullProperty =
        MewProperty<bool>.Register<UIElement>(nameof(SkipViewportCull), false,
            MewPropertyOptions.Inherits);

    public bool SkipViewportCull
    {
        get => GetValue(SkipViewportCullProperty);
        set => SetValue(SkipViewportCullProperty, value);
    }

    /// <summary>
    /// Specifies the cursor to display when the mouse is over this element.
    /// <see langword="null"/> means no override (inherit from parent or platform default);
    /// <see cref="CursorType.None"/> hides the cursor.
    /// </summary>
    public static readonly MewProperty<CursorType?> CursorProperty =
        MewProperty<CursorType?>.Register<UIElement>(nameof(Cursor), null,
            MewPropertyOptions.None);

    /// <summary>
    /// Clears cached inherited property values when the parent changes,
    /// so they will be re-resolved from the new parent chain.
    /// Cascades to descendants because their parent chain also effectively changed
    /// (they hang off this element) and their caches may be stale.
    /// </summary>
    protected override void OnParentChanged()
    {
        base.OnParentChanged();
        VisualTree.Visit(this, static e =>
        {
            if (e is UIElement u && u.HasPropertyStore)
                u.PropertyStore.ClearAllInherited();
        });
    }

    /// <summary>
    /// Called when a MewProperty value changes. Handles layout/render invalidation
    /// and property-specific side effects.
    /// </summary>
    protected override void OnMewPropertyChanged(MewProperty property)
    {
        if (property.AffectsLayout)
            InvalidateMeasure();
        else if (property.AffectsRender)
            InvalidateVisual();

        if (property.AffectsVisualState)
            InvalidateVisualState();

        if (property.Inherits)
            PropagateInheritedPropertyChange(property);

        if (property == IsVisibleProperty)
        {
            if (!IsVisible)
            {
                ReleaseBitmapCachesInSubtree();
            }

            OnVisibilityChanged();
        }
        else if (property == IsEnabledProperty)
        {
            OnEnabledChanged();
            NotifyDescendantEnabledSuggestionChanged();
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

    public bool IsEffectivelyEnabled => IsEnabled && GetSuggestedIsEnabled();

    /// <summary>
    /// Gets or sets whether the element participates in hit testing.
    /// </summary>
    public bool IsHitTestVisible
    {
        get => GetValue(IsHitTestVisibleProperty);
        set => SetValue(IsHitTestVisibleProperty, value);
    }

    public double TabIndex
    {
        get => GetValue(TabIndexProperty);
        set => SetValue(TabIndexProperty, value);
    }

    public bool IsTabStop
    {
        get => GetValue(IsTabStopProperty);
        set => SetValue(IsTabStopProperty, value);
    }

    /// <summary>
    /// Gets or sets the cursor to display when the mouse is over this element.
    /// <see langword="null"/> means no override (inherit from parent or platform default);
    /// <see cref="CursorType.None"/> hides the cursor.
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
    /// Useful for container visuals (e.g. TabControl outline) and WinForms-like focus navigation.
    /// </summary>
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
    /// Occurs when a key is pressed while the element has focus.
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

    public sealed override void Render(IGraphicsContext context)
    {
        if (!IsVisible)
        {
            return;
        }

        if (!SkipViewportCull && _cacheSnapshotDepth == 0 && this is not Window &&
            (FindVisualRoot() is not Window root || !new Rect(root.ClientSize).IntersectsWith(Bounds)))
        {
            ReleaseBitmapCachesInSubtree();
            return;
        }

        MarkBitmapCacheVisible();

        using (PerformanceProfiler.Instance.SampleElement(GetType(), ProfilerSampleCategory.Render, this))
        {
            ResolveVisualState(snap: false);
            ClearVisualStateDirty();

            if (_hasBitmapCache)
            {
                RenderCached(context);
            }
            else
            {
                OnRender(context);
                RenderSubtree(context);
            }
        }
    }

    /// <summary>
    /// Called before <see cref="OnRender"/> to resolve visual state (e.g. style triggers, state transitions).
    /// </summary>
    /// <param name="snap">
    /// When true, target values are applied immediately (no animation). Used when the element is
    /// offscreen at drain time (animating invisible pixels is wasteful) or when a hard state
    /// change must take effect before the next frame (style/theme reset).
    /// </param>
    protected virtual void ResolveVisualState(bool snap) { }

    /// <summary>
    /// Queues this element for visual-state reconciliation at the start of the next layout/render pass.
    /// Dedup'd via an internal dirty flag; safe to call repeatedly. Call when a property that feeds
    /// into <see cref="Controls.Control.ComputeVisualState"/> changes outside the normal render path.
    /// </summary>
    public void InvalidateVisualState()
    {
        // Reentrance: ApplyStyleValues may set properties that fire AffectsVisualState,
        // which would re-enter this method. Skip - the in-progress resolve picks up the
        // new state when it reads ComputeVisualState's inputs.
        if (_resolvingVisualState)
        {
            return;
        }

        _resolvingVisualState = true;
        try
        {
            ResolveVisualState(snap: false);
            ClearVisualStateDirty();
        }
        finally
        {
            _resolvingVisualState = false;
        }
    }

    internal bool IsVisualStateDirty => _visualStateDirty;

    internal void ClearVisualStateDirty() => _visualStateDirty = false;

    internal void ResolveVisualStateFromDrain(bool snap) => ResolveVisualState(snap);

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
    /// <param name="point">The point in element coordinates.</param>
    /// <returns>The element at the point, or null.</returns>
    public UIElement? HitTest(Point point) => OnHitTest(point);

    protected virtual UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible || !IsEffectivelyEnabled)
        {
            return null;
        }

        if (Bounds.Contains(point))
        {
            return this;
        }

        return null;
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
    public Point PointToScreen(Point point)
    {
        var root = FindVisualRoot();
        if (root is not Window window || window.Handle == 0)
        {
            throw new InvalidOperationException("The visual is not connected to a window.");
        }

        var inWindow = TranslatePoint(point, window);
        return window.ClientToScreen(inWindow);
    }

    /// <summary>
    /// Converts a point from screen coordinates in device pixels to element coordinates in DIPs.
    /// </summary>
    /// <param name="point">The point in screen coordinates in device pixels.</param>
    /// <returns>The point in element coordinates.</returns>
    public Point PointFromScreen(Point point)
    {
        var root = FindVisualRoot();
        if (root is not Window window || window.Handle == 0)
        {
            throw new InvalidOperationException("The visual is not connected to a window.");
        }

        var inWindow = window.ScreenToClient(point);
        return window.TranslatePoint(inWindow, this);
    }

    /// <summary>
    /// Converts a rectangle from element coordinates in DIPs to screen coordinates in device pixels.
    /// </summary>
    /// <param name="rect">The rectangle in element coordinates.</param>
    /// <returns>The rectangle in screen coordinates in device pixels.</returns>
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
    /// <returns>The rectangle in element coordinates.</returns>
    public Rect RectFromScreen(Rect rect)
    {
        var tl = PointFromScreen(rect.TopLeft);
        var br = PointFromScreen(rect.BottomRight);
        return new Rect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
    }

    internal void ReevaluateSuggestedIsEnabled()
    {
        bool old = _suggestedIsEnabledInitialized ? _suggestedIsEnabled : true;
        _suggestedIsEnabled = ComputeIsEnabledSuggestionSafe();
        _suggestedIsEnabledInitialized = true;

        if (old != _suggestedIsEnabled)
        {
            OnEnabledChanged();
            InvalidateVisual();
            InvalidateVisualState();
        }
    }

    private bool GetSuggestedIsEnabled()
    {
        if (!_suggestedIsEnabledInitialized)
        {
            _suggestedIsEnabled = ComputeIsEnabledSuggestionSafe();
            _suggestedIsEnabledInitialized = true;
        }
        return _suggestedIsEnabled;
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

    private void NotifyDescendantEnabledSuggestionChanged()
    {
        VisualTree.Visit(this, e =>
        {
            if (ReferenceEquals(e, this))
            {
                return;
            }

            if (e is UIElement u)
            {
                u.ReevaluateSuggestedIsEnabled();
            }
        });
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
        if ((IsFocused || IsFocusWithin) && oldRoot is Window window)
        {
            window.FocusManager.ClearFocus();
        }
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        if (newRoot != null)
        {
            // Re-evaluate enabled state - parent may already be disabled.
            ReevaluateSuggestedIsEnabled();
        }
        else
        {
            // Stop property animations when detached from the visual tree.
            StopAllPropertyAnimations();

            // Release any cached GPU surface/image; re-attaching rebuilds it on next render.
            DisposeCacheEntry();
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
        => _animator ??= new Animation.PropertyAnimator(PropertyStore);

    /// <summary>
    /// Sets the animation target for a visual property. No-op when target is unchanged.
    /// </summary>
    protected void SetTarget<T>(MewProperty<T> property, T value) => PropertyStore.SetTarget(property, value);

    /// <summary>
    /// Sets a property target value. Used by TargetSetter resolution.
    /// </summary>
    internal void SetTargetInternal(MewProperty property, object value)
        => PropertyStore.SetTarget(property, value);

    /// <summary>
    /// Stops all running property animations (e.g. when detached from the visual tree).
    /// </summary>
    internal void StopAllPropertyAnimations()
    {
        _animator?.StopAll();
    }

    #endregion
}
