using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A stacked area series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class StackedAreaSeries<TModel>
    : CoreStackedAreaSeries<TModel, CircleGeometry, LabelGeometry, CubicBezierAreaGeometry, LineGeometry>
{
    public StackedAreaSeries() : base(null) { }

    public StackedAreaSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public StackedAreaSeries(params TModel[] values) : base(values) { }
}

/// <summary>A stacked area series of <see cref="double"/> values.</summary>
public class StackedAreaSeries : StackedAreaSeries<double>
{
    public StackedAreaSeries() { }

    public StackedAreaSeries(params double[] values) : base(values) { }
}
