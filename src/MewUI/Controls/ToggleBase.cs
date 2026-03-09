using Aprillz.MewUI.Styling;

namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for toggle controls like checkboxes and radio buttons.
/// </summary>
public abstract partial class ToggleBase : Control
{
    public static readonly MewProperty<string> TextProperty =
        MewProperty<string>.Register<ToggleBase>(nameof(Text), string.Empty, MewPropertyOptions.AffectsLayout);

    public static readonly MewProperty<bool> IsCheckedProperty =
        MewProperty<bool>.Register<ToggleBase>(nameof(IsChecked), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, _) =>
            {
                    self.OnIsCheckedChanged(self.IsChecked);
                self.CheckedChanged?.Invoke(self.IsChecked);
            });

    /// <summary>
    /// Gets or sets the text label.
    /// </summary>
    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value ?? string.Empty);
    }

    /// <summary>
    /// Gets or sets the checked state.
    /// </summary>
    public bool IsChecked
    {
        get => GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    /// <summary>
    /// Occurs when the checked state changes.
    /// </summary>
    public event Action<bool>? CheckedChanged;

    /// <summary>
    /// Gets whether the control can receive keyboard focus.
    /// </summary>
    public override bool Focusable => true;

    /// <summary>
    /// Initializes a new instance of the ToggleBase class.
    /// </summary>
    protected ToggleBase()
    {
    }

    protected override VisualState ComputeVisualState()
    {
        var state = base.ComputeVisualState();
        if (IsChecked)
            return state with { Flags = state.Flags | VisualStateFlags.Checked };
        return state;
    }

    /// <summary>
    /// Called when the checked state changes.
    /// </summary>
    /// <param name="value">The new checked state.</param>
    protected virtual void OnIsCheckedChanged(bool value) { }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (!IsEffectivelyEnabled)
        {
            return;
        }

        if (e.Key == Key.Space)
        {
            ToggleFromKeyboard();
            e.Handled = true;
        }
    }

    protected virtual void ToggleFromKeyboard()
    {
        IsChecked = !IsChecked;
    }

    protected override void OnDispose()
    {
        base.OnDispose();
    }
}
