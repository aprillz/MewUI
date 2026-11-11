using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.VisualStates;

// Backing point for the logarithmic axis sample; mapped into log space via LiveCharts.Configure.
sealed class LogarithmicPoint(double x, double y)
{
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
}

// User-defined type for the UserDefinedTypes sample; mapped via LiveCharts.Configure.
sealed class City(string name, double population)
{
    public string Name { get; set; } = name;
    public double Population { get; set; } = population;
}

// Raw sales row for Pies/Nested (country > zone > salesperson hierarchy).
sealed class SalesRecord(string country, string zone, string name, double value)
{
    public string Country { get; set; } = country;
    public string Zone { get; set; } = zone;
    public string Name { get; set; } = name;
    public double Value { get; set; } = value;
}

// One nested-pie series: the 3-element Values array places the arc on a concentric ring
// (index 0 = inner/people, 1 = middle/zones, 2 = outer/countries); only one slot is set.
sealed class NestedPieData(string name, double?[] values, string color)
{
    public string Name { get; set; } = name;
    public double?[] Values { get; set; } = values;
    public string Color { get; set; } = color;
    public bool IsTotal => Name is "Brazil" or "Colombia" or "Mexico";
    public Func<ChartPoint, string> Formatter { get; } = point =>
        $"{name}{Environment.NewLine}{point.StackedValue!.Share:P2}";
}
