namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for controls that display a value within a range.
/// </summary>
public abstract class RangeBase : Control
{
    private double _value;

    /// <summary>
    /// Gets or sets the minimum value.
    /// </summary>
    public double Minimum
    {
        get;
        set
        {
            var sanitized = Sanitize(value);
            if (field.Equals(sanitized))
            {
                return;
            }

            field = sanitized;
            CoerceValueAfterRangeChange();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the maximum value.
    /// </summary>
    public double Maximum
    {
        get;
        set
        {
            var sanitized = Sanitize(value);
            if (field.Equals(sanitized))
            {
                return;
            }

            field = sanitized;
            CoerceValueAfterRangeChange();
            InvalidateVisual();
        }
    }

    /// <summary>
    /// Gets or sets the current value.
    /// </summary>
    public double Value
    {
        get => _value;
        set => SetValueCore(value, true);
    }

    /// <summary>
    /// Occurs when the value changes.
    /// </summary>
    public event Action<double>? ValueChanged;

    /// <summary>
    /// Sets the value from a binding source.
    /// </summary>
    /// <param name="value">The new value.</param>
    protected void SetValueFromSource(double value) => SetValueCore(value, false);

    /// <summary>
    /// Called when the value changes.
    /// </summary>
    /// <param name="value">The new value.</param>
    /// <param name="fromUser">Whether the change originated from user input.</param>
    protected virtual void OnValueChanged(double value, bool fromUser) { }

    /// <summary>
    /// Clamps a value to the valid range.
    /// </summary>
    /// <param name="value">The value to clamp.</param>
    /// <returns>The clamped value.</returns>
    protected double ClampToRange(double value)
    {
        value = Sanitize(value);
        double min = Math.Min(Minimum, Maximum);
        double max = Math.Max(Minimum, Maximum);
        return Math.Clamp(value, min, max);
    }

    /// <summary>
    /// Gets the value normalized to 0-1 range.
    /// </summary>
    /// <returns>The normalized value between 0 and 1.</returns>
    protected double GetNormalizedValue()
    {
        double min = Math.Min(Minimum, Maximum);
        double max = Math.Max(Minimum, Maximum);
        double range = max - min;
        if (range <= 0)
        {
            return 0;
        }

        return Math.Clamp((_value - min) / range, 0, 1);
    }

    private void SetValueCore(double value, bool fromUser)
    {
        double clamped = ClampToRange(value);
        if (_value.Equals(clamped))
        {
            return;
        }

        _value = clamped;
        OnValueChanged(_value, fromUser);
        ValueChanged?.Invoke(_value);
        InvalidateVisual();
    }

    private void CoerceValueAfterRangeChange()
    {
        double clamped = ClampToRange(_value);
        if (_value.Equals(clamped))
        {
            return;
        }

        _value = clamped;
        OnValueChanged(_value, false);
        ValueChanged?.Invoke(_value);
    }

    private static double Sanitize(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return value;
    }
}
