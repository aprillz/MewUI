using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI;

/// <summary>
/// Base class for elements that support input handling and visibility.
/// </summary>
public abstract class UIElement : Element
{
    private List<IDisposable>? _bindings;
    private ValueBinding<bool>? _isVisibleBinding;
    private ValueBinding<bool>? _isEnabledBinding;
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

    public event Action? GotFocus;

    public event Action? LostFocus;

    public event Action? MouseEnter;

    public event Action? MouseLeave;

    public event Action<MouseEventArgs>? MouseDown;

    public event Action<MouseEventArgs>? MouseUp;

    public event Action<MouseEventArgs>? MouseMove;

    public event Action<MouseWheelEventArgs>? MouseWheel;

    public event Action<KeyEventArgs>? KeyDown;

    public event Action<KeyEventArgs>? KeyUp;

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

    protected virtual void OnRender(IGraphicsContext context) { }

    /// <summary>
    /// Performs hit testing to find the element at the specified point.
    /// </summary>
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

    public Rect RectToScreen(Rect rect)
    {
        var tl = PointToScreen(rect.TopLeft);
        var br = PointToScreen(rect.BottomRight);
        return new Rect(tl.X, tl.Y, br.X - tl.X, br.Y - tl.Y);
    }

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

    internal void DisposeBindings()
    {
        _isVisibleBinding?.Dispose();
        _isVisibleBinding = null;
        _isEnabledBinding?.Dispose();
        _isEnabledBinding = null;

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

    protected virtual void OnGotFocus()
    { }

    protected virtual void OnLostFocus()
    { }

    protected virtual void OnMouseEnter()
    { }

    protected virtual void OnMouseLeave()
    { }

    internal void RaiseMouseDown(MouseEventArgs e) => OnMouseDown(e);

    internal void RaiseMouseUp(MouseEventArgs e) => OnMouseUp(e);

    internal void RaiseMouseMove(MouseEventArgs e) => OnMouseMove(e);

    internal void RaiseMouseWheel(MouseWheelEventArgs e) => OnMouseWheel(e);

    internal void RaiseKeyDown(KeyEventArgs e) => OnKeyDown(e);

    internal void RaiseKeyUp(KeyEventArgs e) => OnKeyUp(e);

    internal void RaiseTextInput(TextInputEventArgs e) => OnTextInput(e);

    // Protected virtual hooks for derived controls (public API surface stays small).
    protected virtual void OnMouseDown(MouseEventArgs e) => MouseDown?.Invoke(e);

    protected virtual void OnMouseUp(MouseEventArgs e) => MouseUp?.Invoke(e);

    protected virtual void OnMouseMove(MouseEventArgs e) => MouseMove?.Invoke(e);

    protected virtual void OnMouseWheel(MouseWheelEventArgs e) => MouseWheel?.Invoke(e);

    protected virtual void OnKeyDown(KeyEventArgs e) => KeyDown?.Invoke(e);

    protected virtual void OnKeyUp(KeyEventArgs e) => KeyUp?.Invoke(e);

    protected virtual void OnTextInput(TextInputEventArgs e) => TextInput?.Invoke(e);

    #endregion

    protected virtual void OnVisibilityChanged()
    { }

    protected virtual void OnEnabledChanged()
    { }

    #region Binding Helpers

    internal void SetIsVisibleBinding(Func<bool> get, Action<Action>? subscribe = null, Action<Action>? unsubscribe = null)
    {
        ArgumentNullException.ThrowIfNull(get);

        _isVisibleBinding?.Dispose();
        _isVisibleBinding = new ValueBinding<bool>(
            get,
            null,
            subscribe,
            unsubscribe,
            () => IsVisible = get());

        IsVisible = get();
    }

    internal void SetIsEnabledBinding(Func<bool> get, Action<Action>? subscribe = null, Action<Action>? unsubscribe = null)
    {
        ArgumentNullException.ThrowIfNull(get);

        _isEnabledBinding?.Dispose();
        _isEnabledBinding = new ValueBinding<bool>(
            get,
            null,
            subscribe,
            unsubscribe,
            () => IsEnabled = get());

        IsEnabled = get();
    }

    #endregion
}
