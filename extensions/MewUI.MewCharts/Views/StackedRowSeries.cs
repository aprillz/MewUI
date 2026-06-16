using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A stacked row (horizontal bar) series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class StackedRowSeries<TModel>
    : CoreStackedRowSeries<TModel, RoundedRectangleGeometry, LabelGeometry, LineGeometry>
{
    public StackedRowSeries() : base(null) { }

    public StackedRowSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public StackedRowSeries(params TModel[] values) : base(values) { }
}

/// <summary>A stacked row series of <see cref="double"/> values.</summary>
public class StackedRowSeries : StackedRowSeries<double>
{
    public StackedRowSeries() { }

    public StackedRowSeries(params double[] values) : base(values) { }
}
