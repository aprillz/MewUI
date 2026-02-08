using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Base class for elements that support input handling and visibility.
/// </summary>
public abstract partial class UIElement : Element
{
    private List<IDisposable>? _bindings;
    private Dictionary<int, IDisposable>? _bindingSlots;
    private bool _suggestedIsEnabled = true;
    private bool _suggestedIsEnabledInitialized;

    /// <summary>
    /// Gets or sets whether the element is visible.
    /// </summary>
    public bool IsVisible
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                InvalidateMeasure();
                OnVisibilityChanged();
            }
        }
    } = true;

    /// <summary>
    /// Gets or sets whether the element is enabled for input.
    /// </summary>
    public bool IsEnabled
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                InvalidateVisual();
                OnEnabledChanged();
                NotifyDescendantEnabledSuggestionChanged();
            }
        }
    } = true;

    internal bool IsEffectivelyEnabled => IsEnabled && GetSuggestedIsEnabled();

    /// <summary>
    /// Gets or sets whether the element participates in hit testing.
    /// </summary>
    public bool IsHitTestVisible { get; set; } = true;

    /// <summary>
    /// Gets whether the element has keyboard focus.
    /// </summary>
    public bool IsFocused { get; private set; }

    /// <summary>
    /// Gets whether this element or any of its descendants has keyboard focus.
    /// Useful for container visuals (e.g. TabControl outline) and WinForms-like focus navigation.
    /// </summary>
    public bool IsFocusWithin { get; private set; }

    /// <summary>
    /// Gets whether the mouse is over this element.
    /// </summary>
    public bool IsMouseOver { get; private set; }

    /// <summary>
    /// Gets whether this element has mouse capture.
    /// </summary>
    public bool IsMouseCaptured { get; private set; }

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

    /// <summary>
    /// Occurs when text input is received while the element has focus.
    /// </summary>
    public event Action<TextInputEventArgs>? TextInput;

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

    public override void Render(IGraphicsContext context)
    {
        if (!IsVisible)
        {
            return;
        }

        OnRender(context);
    }

    /// <summary>
    /// Renders the element.
    /// </summary>
    /// <param name="context">The graphics context.</param>
    protected virtual void OnRender(IGraphicsContext context) { }

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
    {
        if (IsFocused != focused)
        {
            IsFocused = focused;
            if (focused)
            {
                OnGotFocus();
                GotFocus?.Invoke();
            }
            else
            {
                OnLostFocus();
                LostFocus?.Invoke();
            }
            InvalidateVisual();
        }
    }

    internal void SetFocusWithin(bool focusWithin)
    {
        if (IsFocusWithin != focusWithin)
        {
            IsFocusWithin = focusWithin;
            InvalidateVisual();
        }
    }

    internal void SetMouseOver(bool mouseOver)
    {
        if (IsMouseOver != mouseOver)
        {
            IsMouseOver = mouseOver;
            if (mouseOver)
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
            {
                InvalidateVisual();
            }
        }
    }

    /// <summary>
    /// Controls whether <see cref="SetMouseOver"/> triggers an <see cref="InvalidateVisual"/>.
    /// Container elements like panels can opt out to avoid redundant redraw on hover changes
    /// when they don't have any hover-dependent visuals.
    /// </summary>
    protected virtual bool InvalidateOnMouseOverChanged => true;

    internal void SetMouseCaptured(bool captured) => IsMouseCaptured = captured;

    /// <summary>
    /// Converts a point from element coordinates to screen coordinates.
    /// </summary>
    /// <param name="point">The point in element coordinates.</param>
    /// <returns>The point in screen coordinates.</returns>
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
    /// Converts a point from screen coordinates to element coordinates.
    /// </summary>
    /// <param name="point">The point in screen coordinates.</param>
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
    /// Converts a rectangle from element coordinates to screen coordinates.
    /// </summary>
    /// <param name="rect">The rectangle in element coordinates.</param>
    /// <returns>The rectangle in screen coordinates.</returns>
    public Rect RectToScreen(Rect rect)
    {
        var tl = PointToScreen(rect.TopLeft);
        var br = PointToScreen(rect.BottomRight);
        return new Rect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
    }

    /// <summary>
    /// Converts a rectangle from screen coordinates to element coordinates.
    /// </summary>
    /// <param name="rect">The rectangle in screen coordinates.</param>
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

    internal void RegisterBinding(IDisposable binding)
    {
        if (binding == null)
        {
            return;
        }

        _bindings ??= new List<IDisposable>(2);
        _bindings.Add(binding);
    }

    internal void ReplaceBinding(BindingSlot slot, IDisposable? binding)
    {
        if (_bindingSlots != null && _bindingSlots.TryGetValue(slot.Id, out var old))
        {
            try { old.Dispose(); }
            catch { /* best-effort */ }

            _bindingSlots.Remove(slot.Id);
        }

        if (binding == null)
        {
            if (_bindingSlots != null && _bindingSlots.Count == 0)
            {
                _bindingSlots = null;
            }
            return;
        }

        _bindingSlots ??= new Dictionary<int, IDisposable>(capacity: 2);
        _bindingSlots[slot.Id] = binding;
    }

    internal bool TryGetBinding<TBinding>(BindingSlot slot, out TBinding binding)
        where TBinding : class
    {
        if (_bindingSlots != null && _bindingSlots.TryGetValue(slot.Id, out var obj))
        {
            var typed = obj as TBinding;
            if (typed != null)
            {
                binding = typed;
                return true;
            }
        }

        binding = null!;
        return false;
    }

    internal void DisposeBindings()
    {
        if (_bindingSlots != null)
        {
            foreach (var kvp in _bindingSlots)
            {
                try { kvp.Value.Dispose(); }
                catch { /* best-effort */ }
            }

            _bindingSlots.Clear();
            _bindingSlots = null;
        }

        if (_bindings == null)
        {
            return;
        }

        for (int i = 0; i < _bindings.Count; i++)
        {
            try { _bindings[i].Dispose(); }
            catch { /* best-effort */ }
        }

        _bindings.Clear();
        _bindings = null;
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

    internal void RaiseTextInput(TextInputEventArgs e) => OnTextInput(e);

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
    /// Called when text input is received.
    /// </summary>
    /// <param name="e">Text input event arguments.</param>
    protected virtual void OnTextInput(TextInputEventArgs e) => TextInput?.Invoke(e);

    #endregion

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

    #region Binding Helpers

    #endregion
}
