using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for controls that own a popup: it manages the open state and the popup lifecycle
/// (show, close, bounds, placement, focus and dismissal) but renders nothing of its own. Subclasses
/// draw their trigger and provide the popup content. <see cref="DropDownBase"/> layers the
/// ComboBox-style header and arrow chrome on top.
/// </summary>
public abstract class PopupOwnerBase : Control, IPopupOwner
{
    private UIElement? _popup;
    private Rect? _lastPopupBounds;
    private bool _closingPopup;
    private bool _popupBoundsDirty = true;
    private Window? _popupBoundsWindow;

    public static readonly MewProperty<bool> IsDropDownOpenProperty =
        MewProperty<bool>.Register<PopupOwnerBase>(nameof(IsDropDownOpen), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.AffectsVisualState,
            static (self, oldValue, newValue) => self.OnIsDropDownOpenChanged(oldValue, newValue));

    public static readonly MewProperty<double> MaxDropDownHeightProperty =
        MewProperty<double>.Register<PopupOwnerBase>(nameof(MaxDropDownHeight), 240.0, MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnMaxDropDownHeightChanged(oldValue, newValue));

    static PopupOwnerBase()
    {
        FocusableProperty.OverrideDefaultValue<PopupOwnerBase>(true);
    }

    /// <summary>
    /// Gets or sets whether the popup is open.
    /// </summary>
    public bool IsDropDownOpen
    {
        get => GetValue(IsDropDownOpenProperty);
        set => SetValue(IsDropDownOpenProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum height of the popup.
    /// </summary>
    public double MaxDropDownHeight
    {
        get => GetValue(MaxDropDownHeightProperty);
        set => SetValue(MaxDropDownHeightProperty, value);
    }

    private void OnMaxDropDownHeightChanged(double oldValue, double newValue)
    {
        if (IsDropDownOpen)
            _popupBoundsDirty = true;
    }

    protected virtual void OnIsDropDownOpenChanged(bool oldValue, bool newValue)
    {
        if (!_closingPopup)
        {
            if (newValue)
                ShowPopupCore();
            else
                ClosePopupCore();
        }
    }

    internal override void OnAccessKey() { Focus(); IsDropDownOpen = true; }

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        if (IsDropDownOpen)
            return state with { Flags = state.Flags | VisualStateFlags.Active };
        return state;
    }

    /// <summary>
    /// Creates the popup content (cached and reused).
    /// </summary>
    protected abstract UIElement CreatePopupContent();

    /// <summary>
    /// Updates the popup content before showing/updating bounds (e.g. sync selection).
    /// </summary>
    protected virtual void SyncPopupContent(UIElement popup)
    { }

    /// <summary>
    /// Gets the element to focus when the popup opens. Defaults to the popup itself.
    /// </summary>
    protected virtual UIElement GetPopupFocusTarget(UIElement popup) => popup;

    /// <summary>
    /// Gets whether a click inside the header should toggle the dropdown.
    /// Override to limit toggling to the arrow button area only.
    /// </summary>
    protected virtual bool IsToggleHit(in Rect headerRect, Point positionInControl) => headerRect.Contains(positionInControl);

    /// <summary>
    /// When true, the popup width comes from the popup's own content rather than this control's width,
    /// so the popup manager re-derives that width once the popup is connected and its inherited font
    /// applies. Fixed-width dropdowns (sized to the control) leave this false.
    /// </summary>
    protected virtual bool PopupSizesToContent => false;

    /// <summary>
    /// Resolves the anchor height used to place the popup just below this control. Defaults to the
    /// arranged height so the popup opens right below the trigger.
    /// </summary>
    protected virtual double ResolveAnchorHeight() => Bounds.Height;

    /// <summary>
    /// Calculates the popup bounds. Override for specialized controls (e.g. ComboBox list sizing).
    /// </summary>
    protected virtual Rect CalculatePopupBounds(Window window, UIElement popup)
    {
        var bounds = Bounds;

        double width = Math.Max(0, bounds.Width);
        if (width <= 0)
        {
            width = 120;
        }

        var region = window.GetPopupPlacementRegion(bounds);
        double x = PopupPlacement.ClampHorizontal(bounds.X, width, region, floorToLeftEdge: true);

        double maxHeight = Math.Max(0, MaxDropDownHeight);
        if (maxHeight <= 0)
        {
            maxHeight = Math.Max(0, region.Height);
        }

        // Avoid infinite height to keep scrollable content stable.
        popup.Measure(new Size(width, maxHeight));
        double desiredHeight = Math.Min(Math.Max(0, popup.DesiredSize.Height), maxHeight);

        // Open downward when the dropdown fits below the header (standard behavior), flipping up only
        // when it does not fit below - preferring the side with more raw space would open upward for a
        // control low in the window because the native work-area region extends far above it.
        double belowY = bounds.Y + ResolveAnchorHeight();
        var (y, height) = PopupPlacement.ResolveVerticalPreferBelowIfFits(bounds.Y, belowY, region, desiredHeight);

        return new Rect(x, y, width, height);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        base.ArrangeContent(bounds);

        // Arrange runs on any bounds change, including a position-only move (e.g. a parent
        // panel reflows without resizing this control), which OnSizeChanged would miss.
        if (IsDropDownOpen)
        {
            _popupBoundsDirty = true;
        }
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);

        // Cached popup can exist while closed (Parent == null) so it won't get Window broadcasts.
        if (_popup is FrameworkElement popupElement && popupElement.Parent == null)
        {
            popupElement.NotifyThemeChanged(oldTheme, newTheme);
        }
    }

    protected override void OnRender(IGraphicsContext context)
    {
        // Popup bounds only depend on: this control's own bounds (ArrangeContent/OnSizeChanged),
        // MaxDropDownHeight (OnMaxDropDownHeightChanged), the window's client size (ClientSizeChanged,
        // subscribed while open) and the popup content's own desired size. The first three are event-driven
        // via _popupBoundsDirty; the popup content's layout has no invalidation event, so IsMeasureDirty is
        // checked here as a cheap per-frame fallback. Subclasses draw their trigger, then call base.
        if (IsDropDownOpen && (_popupBoundsDirty || (_popup != null && _popup.IsMeasureDirty)))
        {
            _popupBoundsDirty = false;
            UpdatePopupBoundsCore();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (IsDropDownOpen)
        {
            _popupBoundsDirty = true;
        }
    }

    protected override void OnLostFocus()
    {
        base.OnLostFocus();

        if (!IsDropDownOpen)
        {
            return;
        }

        // If focus moved into the popup, FocusWithin stays true (via Window.TryGetPopupOwner chain).
        if (IsFocusWithin)
        {
            return;
        }

        IsDropDownOpen = false;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left || e.Handled)
        {
            return;
        }

        Focus();

        var bounds = Bounds;
        // Use full arranged bounds for hit-testing. The header can be measured smaller than the final layout
        // (e.g. stretch in a panel), but the whole button face should toggle.
        var headerRect = bounds;

        if (IsToggleHit(headerRect, e.Position))
        {
            IsDropDownOpen = !IsDropDownOpen;
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!IsEffectivelyEnabled || e.Handled)
        {
            return;
        }

        if (e.Key == Key.Space || e.Key == Key.Enter)
        {
            IsDropDownOpen = !IsDropDownOpen;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && IsDropDownOpen)
        {
            IsDropDownOpen = false;
            e.Handled = true;
        }
        else if (e.Key == Key.Down && !IsDropDownOpen)
        {
            IsDropDownOpen = true;
            e.Handled = true;
        }
    }

    private UIElement EnsurePopupContent()
    {
        if (_popup == null)
        {
            _popup = CreatePopupContent();
        }

        return _popup;
    }

    private void ShowPopupCore()
    {
        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        var popup = EnsurePopupContent();
        SyncPopupContent(popup);

        // Placement is measured inside ShowPopup, after the popup is rooted and its style resolves,
        // so CalculatePopupBounds sees the styled popup (correct border/fonts) rather than a pre-attach
        // one whose named style has not resolved yet.
        var popupBounds = window.ShowPopup(this, popup, w => CalculatePopupBounds(w, popup), PopupSizesToContent);
        _lastPopupBounds = popupBounds;
        _popupBoundsDirty = false;

        // Client size has no per-frame-cheap invalidation path (unlike this control's own bounds),
        // so subscribe for the duration the popup is open instead of polling it every frame.
        if (_popupBoundsWindow != null)
        {
            _popupBoundsWindow.ClientSizeChanged -= OnPopupBoundsWindowClientSizeChanged;
        }
        _popupBoundsWindow = window;
        window.ClientSizeChanged += OnPopupBoundsWindowClientSizeChanged;

        var focusTarget = GetPopupFocusTarget(popup);
        window.FocusManager.SetFocus(focusTarget);
    }

    private void OnPopupBoundsWindowClientSizeChanged(Size newClientSize)
    {
        _popupBoundsDirty = true;
    }

    private void ClosePopupCore()
    {
        var root = FindVisualRoot();
        if (root is not Window window)
        {
            _lastPopupBounds = null;
            return;
        }

        if (_popup != null)
        {
            window.ClosePopup(_popup);
        }

        _lastPopupBounds = null;
    }

    private void UpdatePopupBoundsCore()
    {
        if (!IsDropDownOpen || _popup == null)
        {
            return;
        }

        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        SyncPopupContent(_popup);

        var popupBounds = CalculatePopupBounds(window, _popup);
        if (_lastPopupBounds is Rect last && popupBounds.Equals(last))
        {
            return;
        }

        window.UpdatePopup(_popup, popupBounds);
        _lastPopupBounds = popupBounds;
    }

    void IPopupOwner.OnPopupClosed(UIElement popup, PopupCloseKind kind)
    {
        if (_popup == null || !ReferenceEquals(popup, _popup))
        {
            return;
        }

        // window.ClosePopup always reaches this callback (see PopupManager.CloseAndDetachEntry),
        // regardless of which code path triggered the close, so unsubscribing here alone is enough.
        // Use the cached window (not FindVisualRoot()) since a lifecycle close may run after this
        // control has already been detached from the window that owns the popup.
        if (_popupBoundsWindow != null)
        {
            _popupBoundsWindow.ClientSizeChanged -= OnPopupBoundsWindowClientSizeChanged;
            _popupBoundsWindow = null;
        }

        _closingPopup = true;
        try { IsDropDownOpen = false; }
        finally { _closingPopup = false; }
        _lastPopupBounds = null;
        InvalidateVisual();

        if (kind == PopupCloseKind.Lifecycle)
        {
            return;
        }

        if (kind == PopupCloseKind.UserInitiated)
        {
            // When the drop-down itself initiates closing (toggle, selection commit, etc.),
            // keep keyboard focus on the owner so navigation continues naturally.
            // For Policy closes, PopupManager.EnsureFocusNotInClosedPopup handles focus cleanup.
            if (FindVisualRoot() is Window window)
            {
                window.FocusManager.SetFocus(this);
            }
        }
    }

    protected override void OnDispose()
    {
        if (_popup != null)
        {
            // Ensure popup is detached from any Window.
            IsDropDownOpen = false;

            if (_popup is IDisposable d)
            {
                d.Dispose();
            }

            _popup = null;
        }

        base.OnDispose();
    }
}
