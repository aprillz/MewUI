using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

internal static partial class Samples
{
    // StepLines/Basic
    internal static FrameworkElement StepLinesBasic() => new CartesianChart
    {
        Series = [new StepLineSeries<double>(2, 1, 3, 5, 3, 4, 6), new StepLineSeries<double>(4, 2, 5, 2, 4, 5, 3)],
    };

    // StepLines/Area: a step line with the default area fill.
    internal static FrameworkElement StepLinesArea() => new CartesianChart
    {
        Series = [new StepLineSeries<double>(-2, -1, 3, 5, 3, 4, 6)],
    };

    // StepLines/Properties: a step line with custom stroke/fill/geometry paints.
    internal static FrameworkElement StepLinesProperties() => new CartesianChart
    {
        Series = [new StepLineSeries<double>(-2, -1, 3, 5, 3, 4, 6)
        {
            GeometrySize = 20,
            Stroke = new SolidColorPaint(new Color(255, 0, 0, 0), 4),
            Fill = new SolidColorPaint(new Color(48, 0, 0, 0)),
            GeometryStroke = new SolidColorPaint(new Color(255, 0, 0, 0), 4),
            GeometryFill = new SolidColorPaint(new Color(48, 0, 0, 0)),
        }],
    };

    // StepLines/AutoUpdate: a step line whose collection is mutated on a timer.
    internal static FrameworkElement StepLinesAutoUpdate()
    {
        var values = new ObservableCollection<double> { 2, 5, 4, 2, 6, 5, 3 };
        var chart = new CartesianChart { Series = [new StepLineSeries<double>(values)] };
        var timer = new DispatcherTimer().IntervalMs(1500).OnTick(() =>
        {
            values.Add(Random.Next(0, 10));
            if (values.Count > 10) values.RemoveAt(0);
        });
        timer.Start();
        Timers.Add(timer);
        return chart;
    }
}
