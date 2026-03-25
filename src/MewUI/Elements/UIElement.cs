using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for elements that support input handling and visibility.
/// </summary>
public abstract partial class UIElement : Element
{

    private bool _suggestedIsEnabled = true;
    private bool _suggestedIsEnabledInitialized;

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
            MewPropertyOptions.AffectsRender);

    /// <summary>
    /// Whether the element has keyboard focus.
    /// </summary>
    public static readonly MewProperty<bool> IsFocusedProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsFocused), false,
            MewPropertyOptions.AffectsRender);

    /// <summary>
    /// Whether this element or any of its descendants has keyboard focus.
    /// </summary>
    public static readonly MewProperty<bool> IsFocusWithinProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsFocusWithin), false,
            MewPropertyOptions.AffectsRender);

    /// <summary>
    /// Whether the mouse is over this element.
    /// </summary>
    public static readonly MewProperty<bool> IsMouseOverProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsMouseOver), false,
            MewPropertyOptions.None);

    /// <summary>
    /// Whether this element has mouse capture.
    /// </summary>
    public static readonly MewProperty<bool> IsMouseCapturedProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsMouseCaptured), false,
            MewPropertyOptions.None);

    /// <summary>
    /// Controls whether the element participates in hit testing.
    /// </summary>
    public static readonly MewProperty<bool> IsHitTestVisibleProperty =
        MewProperty<bool>.Register<UIElement>(nameof(IsHitTestVisible), true,
            MewPropertyOptions.None);

    /// <summary>
    /// Specifies the cursor to display when the mouse is over this element.
    /// <see cref="CursorType.None"/> means no override (inherit from parent or platform default).
    /// </summary>
    public static readonly MewProperty<CursorType> CursorProperty =
        MewProperty<CursorType>.Register<UIElement>(nameof(Cursor), CursorType.None,
            MewPropertyOptions.None);

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

        if (property.Inherits)
            PropagateInheritedPropertyChange(property);

        if (property == IsVisibleProperty)
        {
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

            if (InvalidateOnMouseOverChanged)
                InvalidateVisual();
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
        // Stop propagation if child has its own value (local or style target)
        if (child is UIElement u && u.HasPropertyStore && u.PropertyStore.HasOwnValue(property.Id))
            return;

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

    /// <summary>
    /// Gets or sets the cursor to display when the mouse is over this element.
    /// <see cref="CursorType.None"/> means no override (inherit from parent or platform default).
    /// </summary>
    public CursorType Cursor
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
    /// Gets whether this element can receive focus.
    /// </summary>
    public virtual bool Focusable => false;

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

        ResolveVisualState();
        OnRender(context);
        RenderSubtree(context);
    }

    /// <summary>
    /// Called before <see cref="OnRender"/> to resolve visual state (e.g. style triggers, state transitions).
    /// </summary>
    protected virtual void ResolveVisualState() { }

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

    /// <summary>
    /// Controls whether mouse-over changes trigger an <see cref="Element.InvalidateVisual"/>.
    /// Container elements like panels can opt out to avoid redundant redraw on hover changes
    /// when they don't have any hover-dependent visuals.
    /// </summary>
    protected virtual bool InvalidateOnMouseOverChanged => true;

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

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        // Stop property animations when detached from the visual tree.
        if (newRoot == null)
        {
            StopAllPropertyAnimations();
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
