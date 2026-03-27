namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for toggle controls like checkboxes and radio buttons.
/// </summary>
public abstract partial class ToggleBase : ContentControl
{
    public static readonly MewProperty<bool> IsCheckedProperty =
        MewProperty<bool>.Register<ToggleBase>(nameof(IsChecked), false,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.BindsTwoWayByDefault,
            static (self, oldValue, newValue) => self.OnIsCheckedPropertyChanged(oldValue, newValue));

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
        {
            return state with { Flags = state.Flags | VisualStateFlags.Checked };
        }
        return state;
    }

    internal override void OnAccessKey()
    {
        Focus();
        ToggleFromKeyboard();
    }

    private void OnIsCheckedPropertyChanged(bool oldValue, bool newValue)
    {
        OnIsCheckedChanged(newValue);
        CheckedChanged?.Invoke(newValue);
    }

    /// <summary>
    /// Called when the checked state changes.
    /// </summary>
    /// <param name="value">The new checked state.</param>
    protected virtual void OnIsCheckedChanged(bool value)
    { }

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
