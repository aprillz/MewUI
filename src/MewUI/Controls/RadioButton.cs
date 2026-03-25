using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// A radio button control with optional content label.
/// </summary>
public class RadioButton : ToggleBase
{
    private Window? _registeredWindow;
    private string? _registeredGroupName;
    private Element? _registeredParentScope;

    public static readonly MewProperty<string?> GroupNameProperty =
        MewProperty<string?>.Register<RadioButton>(nameof(GroupName), null,
            MewPropertyOptions.AffectsRender,
            static (self, oldValue, newValue) => self.OnGroupNameChanged(oldValue, newValue));

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
        get => GetValue(GroupNameProperty);
        set => SetValue(GroupNameProperty, value);
    }

    protected virtual void OnGroupNameChanged(string? oldValue, string? newValue)
    {
        if (IsChecked)
        {
            UnregisterFromGroup();
            RegisterToGroup();
        }
    }

    public RadioButton()
    {
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

    private const double BoxSize = 14;
    private const double SpacingValue = 6;

    protected override Size MeasureContent(Size availableSize)
    {
        EnsureGroupRegistered();

        double width = BoxSize + SpacingValue;
        double height = BoxSize;

        if (Content != null)
        {
            var contentAvailable = new Size(
                Math.Max(0, availableSize.Width - width - Padding.HorizontalThickness),
                double.PositiveInfinity);
            Content.Measure(contentAvailable);
            width += Content.DesiredSize.Width;
            height = Math.Max(height, Content.DesiredSize.Height);
        }

        return new Size(width, height).Inflate(Padding);
    }

    protected override void OnRender(IGraphicsContext context)
    {
        EnsureGroupRegistered();

        var bounds = Bounds;
        var contentBounds = bounds.Deflate(Padding);
        var state = CurrentVisualState;

        double boxY = contentBounds.Y + (contentBounds.Height - BoxSize) / 2;
        var circleRect = new Rect(contentBounds.X, boxY, BoxSize, BoxSize);

        var fill = GetValue(BackgroundProperty);
        context.FillEllipse(circleRect, fill);

        if (BorderThickness > 0)
        {
            var borderColor = GetValue(BorderBrushProperty);
            context.DrawEllipse(circleRect, borderColor, Math.Max(1, BorderThickness), strokeInset: true);
        }

        if (IsChecked)
        {
            var inner = circleRect.Inflate(-4, -4);
            var dot = state.IsEnabled ? Theme.Palette.Accent : Theme.Palette.DisabledAccent;
            context.FillEllipse(inner, dot);
        }

        if (Content != null)
        {
            var textBounds = new Rect(
                contentBounds.X + BoxSize + SpacingValue,
                contentBounds.Y,
                Math.Max(0, contentBounds.Width - BoxSize - SpacingValue),
                contentBounds.Height);
            Content.Arrange(textBounds);
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (!IsEffectivelyEnabled || e.Button != MouseButton.Left)
        {
            return;
        }

        SetPressed(true);
        Focus();

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.CaptureMouse(this);
        }

        e.Handled = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButton.Left || !IsPressed)
        {
            return;
        }

        SetPressed(false);

        var root = FindVisualRoot();
        if (root is Window window)
        {
            window.ReleaseMouseCapture();
        }

        if (IsEffectivelyEnabled && Bounds.Contains(e.Position))
        {
            IsChecked = true;
        }

        e.Handled = true;
    }

    protected override void OnDispose()
    {
        base.OnDispose();
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
    }
}
