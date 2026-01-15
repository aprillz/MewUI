namespace Aprillz.MewUI.Controls;

public abstract class RangeBase : Control
{
    private double _minimum;
    private double _maximum;
    private double _value;

    public double Minimum
    {
        get => _minimum;
        set
        {
            _minimum = Sanitize(value);
            CoerceValueAfterRangeChange();
            InvalidateVisual();
        }
    }

    public double Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Sanitize(value);
            CoerceValueAfterRangeChange();
            InvalidateVisual();
        }
    }

    public double Value
    {
        get => _value;
        set => SetValueCore(value, fromUser: true);
    }

    public event Action<double>? ValueChanged;

    protected void SetValueFromSource(double value) => SetValueCore(value, fromUser: false);

    protected virtual void OnValueChanged(double value, bool fromUser) { }

    protected double ClampToRange(double value)
    {
        value = Sanitize(value);
        double min = Math.Min(Minimum, Maximum);
        double max = Math.Max(Minimum, Maximum);
        return Math.Clamp(value, min, max);
    }

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
        OnValueChanged(_value, fromUser: false);
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
