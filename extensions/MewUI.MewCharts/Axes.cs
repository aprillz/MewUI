using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;

namespace Aprillz.MewUI.MewCharts;

/// <summary>A Cartesian axis whose values are <see cref="DateTime"/> ticks.</summary>
public class DateTimeAxis : Axis
{
    /// <summary>Initializes an axis whose unit width and minimum step are one <paramref name="unit"/> and whose labels come from <paramref name="formatter"/>.</summary>
    public DateTimeAxis(TimeSpan unit, Func<DateTime, string> formatter)
    {
        UnitWidth = unit.Ticks;
        Labeler = value => formatter(value.AsDate());
        MinStep = unit.Ticks;
    }
}

/// <summary>A Cartesian axis whose values are <see cref="TimeSpan"/> ticks.</summary>
public class TimeSpanAxis : Axis
{
    /// <summary>Initializes an axis whose unit width and minimum step are one <paramref name="unit"/> and whose labels come from <paramref name="formatter"/>.</summary>
    public TimeSpanAxis(TimeSpan unit, Func<TimeSpan, string> formatter)
    {
        UnitWidth = unit.Ticks;
        Labeler = value => formatter(value.AsTimeSpan());
        MinStep = unit.Ticks;
    }
}

/// <summary>A Cartesian axis with a logarithmic scale.</summary>
public class LogarithmicAxis : Axis
{
    /// <summary>Initializes an axis that scales values logarithmically with the given base.</summary>
    public LogarithmicAxis(double @base) => ((ICartesianAxis)this).SetLogBase(@base);
}
