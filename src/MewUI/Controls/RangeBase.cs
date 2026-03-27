namespace Aprillz.MewUI.Controls;

/// <summary>
/// Base class for controls that display a value within a range.
/// </summary>
public abstract class RangeBase : Control
{
    public static readonly MewProperty<double> ValueProperty =
        MewProperty<double>.Register<RangeBase>(nameof(Value), 0.0,
            MewPropertyOptions.AffectsRender | MewPropertyOptions.BindsTwoWayByDefault,
            static (self, _, _) => self.OnValuePropertyChanged(),
            static (self, value) => self.ClampToRange(value));

    public static readonly MewProperty<double> MinimumProperty =
        MewProperty<double>.Register<RangeBase>(nameof(Minimum), 0.0,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.OnMinimumChanged(),
            static (_, value) => Sanitize(value));

    public static readonly MewProperty<double> MaximumProperty =
        MewProperty<double>.Register<RangeBase>(nameof(Maximum), 1.0,
            MewPropertyOptions.AffectsRender,
            static (self, _, _) => self.OnMaximumChanged(),
            static (_, value) => Sanitize(value));

    /// <summary>
    /// Gets or sets the minimum value.
    /// </summary>
    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    /// <summary>
    /// Gets or sets the maximum value.
    /// </summary>
    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>
    /// Gets or sets the current value.
    /// </summary>
    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>
    /// Occurs when the value changes.
    /// </summary>
    public event Action<double>? ValueChanged;

    private void OnValuePropertyChanged()
    {
        OnValueChanged(Value);
        ValueChanged?.Invoke(Value);
    }

    private void OnMinimumChanged() => CoerceValue(ValueProperty);

    private void OnMaximumChanged() => CoerceValue(ValueProperty);

    /// <summary>
    /// Called when the value changes.
    /// </summary>
    /// <param name="value">The new value.</param>
    protected virtual void OnValueChanged(double value)
    { }

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

        return Math.Clamp((Value - min) / range, 0, 1);
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
