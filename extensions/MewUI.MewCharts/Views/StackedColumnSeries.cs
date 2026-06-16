using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A stacked column series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class StackedColumnSeries<TModel>
    : CoreStackedColumnSeries<TModel, RoundedRectangleGeometry, LabelGeometry, LineGeometry>
{
    public StackedColumnSeries() : base(null) { }

    public StackedColumnSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public StackedColumnSeries(params TModel[] values) : base(values) { }
}

/// <summary>A stacked column series of <see cref="double"/> values.</summary>
public class StackedColumnSeries : StackedColumnSeries<double>
{
    public StackedColumnSeries() { }

    public StackedColumnSeries(params double[] values) : base(values) { }
}
