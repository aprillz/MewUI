namespace Aprillz.MewUI.Controls;

public abstract class ToggleBase : Control
{
    private bool _isChecked;
    private ValueBinding<bool>? _checkedBinding;
    private bool _updatingFromSource;

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

    public event Action<bool>? CheckedChanged;

    public override bool Focusable => true;

    protected override Color DefaultBorderBrush => Theme.Current.Palette.ControlBorder;

    protected ToggleBase()
    {
        Background = Color.Transparent;
        BorderThickness = 1;
    }

    protected void SetIsCheckedFromSource(bool value) => SetIsCheckedCore(value, false);

    protected virtual void OnIsCheckedChanged(bool value) { }

    private void SetIsCheckedCore(bool value, bool fromInput)
    {
        _isChecked = value;
        OnIsCheckedChanged(value);
        CheckedChanged?.Invoke(value);

        if (fromInput && !_updatingFromSource)
        {
            _checkedBinding?.Set(value);
        }

        InvalidateVisual();
    }

    public void SetIsCheckedBinding(
        Func<bool> get,
        Action<bool> set,
        Action<Action>? subscribe = null,
        Action<Action>? unsubscribe = null)
    {
        ArgumentNullException.ThrowIfNull(get);
        ArgumentNullException.ThrowIfNull(set);

        _checkedBinding?.Dispose();
        _checkedBinding = new ValueBinding<bool>(
            get,
            set,
            subscribe,
            unsubscribe,
            () =>
            {
                _updatingFromSource = true;
                try { SetIsCheckedFromSource(get()); }
                finally { _updatingFromSource = false; }
            });

        _updatingFromSource = true;
        try { SetIsCheckedFromSource(get()); }
        finally { _updatingFromSource = false; }
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
        _checkedBinding?.Dispose();
        _checkedBinding = null;
        base.OnDispose();
    }
}
