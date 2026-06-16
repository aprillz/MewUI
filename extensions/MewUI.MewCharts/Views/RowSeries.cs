using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A row (horizontal bar) series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class RowSeries<TModel>
    : CoreRowSeries<TModel, RoundedRectangleGeometry, LabelGeometry, LineGeometry>
{
    public RowSeries() : base(null) { }

    public RowSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public RowSeries(params TModel[] values) : base(values) { }
}

/// <summary>A row series of <see cref="double"/> values.</summary>
public class RowSeries : RowSeries<double>
{
    public RowSeries() { }

    public RowSeries(params double[] values) : base(values) { }
}
