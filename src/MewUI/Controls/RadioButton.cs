using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A radio button control with optional text label.
/// </summary>
public class RadioButton : ToggleBase
{
    private bool _isPressed;
    private Window? _registeredWindow;
    private string? _registeredGroupName;
    private Element? _registeredParentScope;
    private TextMeasureCache _textMeasureCache;

    /// <summary>
    /// Ensures the radio button is registered with its group if checked.
    /// </summary>
    internal void EnsureGroupRegistered()
    {
        if (!IsChecked)
        {
            return;
        }

        RegisterToGroup();
    }

    /// <summary>
    /// Gets or sets the group name for mutual exclusion.
    /// </summary>
    public string? GroupName
    {
        get;
        set
        {
            if (field == value)
            {
                return;
            }

            field = value;
            InvalidateVisual();

            if (IsChecked)
            {
                UnregisterFromGroup();
                RegisterToGroup();
            }
        }
    }

    public RadioButton()
    {
        BorderThickness = 1;
        Padding = new Thickness(2);
    }

    protected override void OnIsCheckedChanged(bool value)
    {
        if (value)
        {
            RegisterToGroup();
        }
        else
        {
            UnregisterFromGroup();
        }
    }

    protected override void ToggleFromKeyboard()
    {
        IsChecked = true;
    }

    protected override void OnParentChanged()
    {
        base.OnParentChanged();

        // IsChecked can be set before the control is attached to a Window (e.g. in markup),
        // which means group registration would have been skipped. Re-register once the parent chain changes.
        if (IsChecked)
        {
            UnregisterFromGroup();
            RegisterToGroup();
        }
    }

    private void RegisterToGroup()
    {
        var root = FindVisualRoot();
        if (root is not Window window)
        {
            return;
        }

        string? group = string.IsNullOrWhiteSpace(GroupName) ? null : GroupName;
        var parentScope = group == null ? Parent : null;
        if (group == null && parentScope == null)
        {
            return;
        }

        if (_registeredWindow == window &&
            string.Equals(_registeredGroupName, group, StringComparison.Ordinal) &&
            _registeredParentScope == parentScope)
        {
            return;
        }

        UnregisterFromGroup();

        window.RadioGroupChecked(this, group, parentScope);
        _registeredWindow = window;
        _registeredGroupName = group;
        _registeredParentScope = parentScope;
    }

    private void UnregisterFromGroup()
    {
        var window = _registeredWindow;
        if (window == null)
        {
            return;
        }

        window.RadioGroupUnchecked(this, _registeredGroupName, _registeredParentScope);
        _registeredWindow = null;
        _registeredGroupName = null;
        _registeredParentScope = null;
    }

    protected override Size MeasureContent(Size availableSize)
    {
        // IsChecked may be set before this control is attached to a Window. In that case,
        // initial group registration is skipped. Retrying here makes group exclusivity reliable
        // without requiring global lifecycle hooks.
        EnsureGroupRegistered();

        const double boxSize = 14;
        const double spacing = 6;

        double width = boxSize + spacing;
        double height = boxSize;

        if (!string.IsNullOrEmpty(Text))
        {
            var factory = GetGraphicsFactory();
            var font = GetFont(factory);
            var size = _textMeasureCache.Measure(factory, GetDpi(), font, Text, TextWrapping.NoWrap, 0);
            width += size.Width;
            height = Math.Max(height, size.Height);
        }

        return new Size(width, height).Inflate(Padding);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        EnsureGroupRegistered();

        
        var bounds = Bounds;
        var contentBounds = bounds.Deflate(Padding);
        var state = GetVisualState(_isPressed, _isPressed);

        const double boxSize = 14;
        const double spacing = 6;

        double boxY = contentBounds.Y + (contentBounds.Height - boxSize) / 2;
        var circleRect = new Rect(contentBounds.X, boxY, boxSize, boxSize);

        var fill = state.IsEnabled ? Theme.Palette.ControlBackground : Theme.Palette.DisabledControlBackground;
        context.FillEllipse(circleRect, fill);

        var borderColor = PickAccentBorder(Theme, BorderBrush, state, 0.6);
        context.DrawEllipse(circleRect, borderColor, Math.Max(1, BorderThickness));

        if (IsChecked)
        {
            var inner = circleRect.Inflate(-4, -4);
            var dot = state.IsEnabled ? Theme.Palette.Accent : Theme.Palette.DisabledAccent;
            context.FillEllipse(inner, dot);
        }

        if (!string.IsNullOrEmpty(Text))
        {
            var textColor = state.IsEnabled ? Foreground : Theme.Palette.DisabledText;
            var textBounds = new Rect(contentBounds.X + boxSize + spacing, contentBounds.Y, contentBounds.Width - boxSize - spacing, contentBounds.Height);
            var font = GetFont();
            context.DrawText(Text, textBounds, font, textColor, TextAlignment.Left, TextAlignment.Center, TextWrapping.NoWrap);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        _isPressed = true;
        Focus();

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left || !_isPressed)
        {
            return;
        }

        _isPressed = false;

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }

        if (IsEnabled && Bounds.Contains(e.Position))
        {
            IsChecked = true;
        }

        InvalidateVisual();
        e.Handled = true;
    }

    protected override void OnDispose()
    {
        base.OnDispose();
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        _textMeasureCache.Invalidate();
    }
}
