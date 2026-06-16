using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;

namespace Aprillz.MewUI.MewCharts;

/// <summary>A DateTime axis.</summary>
public class DateTimeAxis : Axis
{
    public DateTimeAxis(TimeSpan unit, Func<DateTime, string> formatter)
    {
        UnitWidth = unit.Ticks;
        Labeler = value => formatter(value.AsDate());
        MinStep = unit.Ticks;
    }
}

/// <summary>A TimeSpan axis.</summary>
public class TimeSpanAxis : Axis
{
    public TimeSpanAxis(TimeSpan unit, Func<TimeSpan, string> formatter)
    {
        UnitWidth = unit.Ticks;
        Labeler = value => formatter(value.AsTimeSpan());
        MinStep = unit.Ticks;
    }
}

/// <summary>A logarithmic axis.</summary>
public class LogarithmicAxis : Axis
{
    public LogarithmicAxis(double @base) => ((ICartesianAxis)this).SetLogBase(@base);
}
