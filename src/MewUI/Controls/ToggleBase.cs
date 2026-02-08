namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for toggle controls like checkboxes and radio buttons.
/// </summary>
public abstract partial class ToggleBase : Control
{
    private bool _isChecked;
    private bool _updatingFromSource;

    /// <summary>
    /// Gets or sets the text label.
    /// </summary>
    public string Text
    {
        get;
        set
        {
            field = value ?? string.Empty;
            InvalidateMeasure();
            InvalidateVisual();
        }
    } = string.Empty;

    /// <summary>
    /// Gets or sets the checked state.
    /// </summary>
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value)
            {
                return;
            }

            SetIsCheckedCore(value, true);
        }
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
    /// Gets the default border brush color.
    /// </summary>
    protected override Color DefaultBorderBrush => Theme.Palette.ControlBorder;

    /// <summary>
    /// Initializes a new instance of the ToggleBase class.
    /// </summary>
    protected ToggleBase()
    {
        Background = Color.Transparent;
        BorderThickness = 1;
    }

    /// <summary>
    /// Sets the checked state from a binding source.
    /// </summary>
    /// <param name="value">The new checked state.</param>
    protected void SetIsCheckedFromSource(bool value) => SetIsCheckedCore(value, false);

    /// <summary>
    /// Called when the checked state changes.
    /// </summary>
    /// <param name="value">The new checked state.</param>
    protected virtual void OnIsCheckedChanged(bool value) { }

    /// <summary>
    /// Sets the checked state internally.
    /// </summary>
    /// <param name="value">The new checked state.</param>
    /// <param name="fromInput">Whether the change originated from user input.</param>
    private void SetIsCheckedCore(bool value, bool fromInput)
    {
        _isChecked = value;
        OnIsCheckedChanged(value);
        CheckedChanged?.Invoke(value);

        if (fromInput && !_updatingFromSource)
        {
            if (TryGetBinding(CheckedBindingSlot, out ValueBinding<bool> checkedBinding))
            {
                checkedBinding.Set(value);
            }
        }

        InvalidateVisual();
    }

    /// <summary>
    /// Sets a two-way binding for the IsChecked property.
    /// </summary>
    /// <param name="get">Function to get the current value.</param>
    /// <param name="set">Action to set the value.</param>
    /// <param name="subscribe">Optional action to subscribe to change notifications.</param>
    /// <param name="unsubscribe">Optional action to unsubscribe from change notifications.</param>
    public void SetIsCheckedBinding(
        Func<bool> get,
        Action<bool> set,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        SetIsCheckedBindingCore(get, set, subscribe, unsubscribe);
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);

        if (!IsEnabled)
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
