using Aprillz.MewUI.Controls.Text;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for controls that render a header with a right-side drop-down button
/// and show a popup when opened (e.g. ComboBox, DatePicker, ColorPicker).
/// </summary>
public abstract class DropDownBase : Control, IPopupOwner
{
    private bool _isDropDownOpen;
    private UIElement? _popup;
    private Rect? _lastPopupBounds;
    private bool _restoreFocusAfterPopupClose;

    /// <summary>
    /// Gets or sets whether the popup is open.
    /// </summary>
    public bool IsDropDownOpen
    {
        get => _isDropDownOpen;
        set
        {
            if (_isDropDownOpen == value)
            {
                return;
            }

            _isDropDownOpen = value;
            if (_isDropDownOpen)
            {
                ShowPopupCore();
            }
            else
            {
                ClosePopupCore();
            }

            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the maximum height of the popup.
    /// </summary>
    public double MaxDropDownHeight
    {
        get;
        set
        {
            if (SetDouble(ref field, value))
            {
                if (IsDropDownOpen)
                {
                    UpdatePopupBoundsCore();
                }
            }
        }
    } = 240;

    /// <summary>
    /// Gets whether the control can receive keyboard focus.
    /// </summary>
    public override bool Focusable => true;

    /// <summary>
    /// Gets or sets the arrow (chevron) color for the current frame.
    /// Derived controls can update this inside <see cref="RenderHeaderContent"/>.
    /// </summary>
    protected Color ArrowForeground { get; set; }

    /// <summary>
    /// Gets the width (in DIP) reserved for the arrow button area.
    /// </summary>
    protected virtual double ArrowAreaWidth => 22;

    /// <summary>
    /// Gets the corner radius used for the header border.
    /// </summary>
    protected virtual double CornerRadiusDip => Theme.Metrics.ControlCornerRadius;

    /// <summary>
    /// Gets the default minimum height.
    /// </summary>
    protected override double DefaultMinHeight => Theme.Metrics.BaseControlHeight;

    /// <summary>
    /// Gets the default border color.
    /// </summary>
    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    /// <summary>
    /// Creates the popup content (cached and reused).
    /// </summary>
    protected abstract UIElement CreatePopupContent();

    /// <summary>
    /// Updates the popup content before showing/updating bounds (e.g. sync selection).
    /// </summary>
    protected virtual void SyncPopupContent(UIElement popup) { }

    /// <summary>
    /// Measures the header (excluding margin).
    /// </summary>
    protected abstract Size MeasureHeader(Size availableSize);

    /// <summary>
    /// Renders the header content (text/content area). The arrow is rendered by the base.
    /// </summary>
    protected abstract void RenderHeaderContent(IGraphicsContext context, Rect headerRect, Rect innerHeaderRect);

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

        var client = window.ClientSize;
        double x = bounds.X;

        // Clamp horizontally to client area.
        if (x + width > client.Width)
        {
            x = Math.Max(0, client.Width - width);
        }

        if (x < 0)
        {
            x = 0;
        }

        double maxHeight = Math.Max(0, MaxDropDownHeight);
        if (maxHeight <= 0)
        {
            maxHeight = Math.Max(0, client.Height);
        }

        // Avoid infinite height to keep scrollable content stable.
        popup.Measure(new Size(width, maxHeight));
        double desiredHeight = Math.Min(Math.Max(0, popup.DesiredSize.Height), maxHeight);

        double belowY = bounds.Y + ResolveHeaderHeight();
        double availableBelow = Math.Max(0, client.Height - belowY);
        double availableAbove = Math.Max(0, bounds.Y);

        bool preferBelow = availableBelow >= availableAbove;

        double height;
        double y;

        if (preferBelow)
        {
            if (availableBelow > 0 || availableAbove <= 0)
            {
                y = belowY;
                height = Math.Min(desiredHeight, availableBelow);
            }
            else
            {
                height = Math.Min(desiredHeight, availableAbove);
                y = bounds.Y - height;
            }
        }
        else
        {
            if (availableAbove > 0 || availableBelow <= 0)
            {
                height = Math.Min(desiredHeight, availableAbove);
                y = bounds.Y - height;
            }
            else
            {
                y = belowY;
                height = Math.Min(desiredHeight, availableBelow);
            }
        }

        return new Rect(x, y, width, height);
    }

    /// <summary>
    /// Requests focus to be restored to the owner when the popup closes.
    /// If not requested, focus is cleared (when focus was inside the popup).
    /// </summary>
    protected void RequestRestoreFocusAfterPopupClose() => _restoreFocusAfterPopupClose = true;

    protected override Size MeasureContent(Size availableSize) => MeasureHeader(availableSize);

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
        var bounds = GetSnappedBorderBounds(Bounds);
        var borderInset = GetBorderVisualInset();
        double radius = CornerRadiusDip;

        var bg = IsEnabled ? Background : Theme.Palette.ButtonDisabledBackground;

        var state = GetVisualState(isPressed: false, isActive: IsDropDownOpen);

        Color baseBorder = IsEnabled ? BorderBrush : Theme.Palette.ControlBorder;
        var borderColor = PickAccentBorder(Theme, baseBorder, state, hoverMix: 0.6);

        DrawBackgroundAndBorder(context, bounds, bg, borderColor, radius);

        var headerHeight = ResolveHeaderHeight();
        var headerRect = new Rect(bounds.X, bounds.Y, bounds.Width, headerHeight);
        var innerHeaderRect = headerRect.Deflate(new Thickness(borderInset));

        ArrowForeground = IsEnabled ? Foreground : Theme.Palette.DisabledText;
        RenderHeaderContent(context, headerRect, innerHeaderRect);

        DrawArrow(context, headerRect, ArrowForeground, IsDropDownOpen);

        if (IsDropDownOpen)
        {
            UpdatePopupBoundsCore();
        }
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (IsDropDownOpen)
        {
            UpdatePopupBoundsCore();
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

        if (!IsEnabled || e.Button != MouseButton.Left || e.Handled)
        {
            return;
        }

        Focus();

        var bounds = Bounds;
        double headerHeight = ResolveHeaderHeight();
        var headerRect = new Rect(bounds.X, bounds.Y, bounds.Width, headerHeight);

        if (IsToggleHit(headerRect, e.Position))
        {
            IsDropDownOpen = !IsDropDownOpen;
            e.Handled = true;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        if (!IsEnabled || e.Handled)
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

    protected double ResolveHeaderHeight()
    {
        if (!double.IsNaN(Height) && Height > 0)
        {
            return Height;
        }

        var min = MinHeight > 0 ? MinHeight : 0;
        return Math.Max(Math.Max(24, FontSize + Padding.VerticalThickness + 8), min);
    }

    private void DrawArrow(IGraphicsContext context, Rect headerRect, Color color, bool isUp)
    {
        double centerX = headerRect.Right - ArrowAreaWidth / 2;
        double centerY = headerRect.Y + headerRect.Height / 2;

        ChevronGlyph.Draw(
            context,
            new Point(centerX, centerY),
            size: 4,
            color,
            isUp ? ChevronDirection.Up : ChevronDirection.Down);
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

        var popupBounds = CalculatePopupBounds(window, popup);
        window.ShowPopup(this, popup, popupBounds);
        _lastPopupBounds = popupBounds;

        var focusTarget = GetPopupFocusTarget(popup);
        window.FocusManager.SetFocus(focusTarget);
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

    void IPopupOwner.OnPopupClosed(UIElement popup)
    {
        if (_popup == null || !ReferenceEquals(popup, _popup))
        {
            return;
        }

        _isDropDownOpen = false;
        _lastPopupBounds = null;
        InvalidateVisual();

        var root = FindVisualRoot();
        if (root is Window window)
        {
            var focused = window.FocusManager.FocusedElement;
            if (focused != null && (ReferenceEquals(focused, popup) || IsInSubtreeOf(focused, popup)))
            {
                if (_restoreFocusAfterPopupClose)
                {
                    window.FocusManager.SetFocus(this);
                }
                else
                {
                    window.FocusManager.ClearFocus();
                }
            }
        }

        _restoreFocusAfterPopupClose = false;
    }

    private static bool IsInSubtreeOf(UIElement element, UIElement root)
    {
        for (Element? current = element; current != null; current = current.Parent)
        {
            if (ReferenceEquals(current, root))
            {
                return true;
            }
        }

        return false;
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
