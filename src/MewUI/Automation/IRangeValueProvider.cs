namespace Aprillz.MewUI.Automation;

/// <summary>A control whose value is a number within a range, such as a slider.</summary>
public interface IRangeValueProvider
{
    double Value { get; }

    double Minimum { get; }

    double Maximum { get; }

    double SmallChange { get; }

    double LargeChange { get; }

    bool IsReadOnly { get; }

    /// <summary>Sets the value; throws when the control is read-only or disabled.</summary>
    void SetValue(double value);
}
