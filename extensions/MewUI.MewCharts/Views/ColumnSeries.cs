using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;
using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A column series with a custom bar geometry, rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
/// <typeparam name="TVisual">The bar geometry type.</typeparam>
public class ColumnSeries<TModel, TVisual>
    : CoreColumnSeries<TModel, TVisual, LabelGeometry, LineGeometry>
    where TVisual : BoundedDrawnGeometry, new()
{
    public ColumnSeries() : base(null) { }

    public ColumnSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public ColumnSeries(params TModel[] values) : base(values) { }
}

/// <summary>A column (vertical bar) series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class ColumnSeries<TModel>
    : CoreColumnSeries<TModel, RoundedRectangleGeometry, LabelGeometry, LineGeometry>
{
    public ColumnSeries() : base(null) { }

    public ColumnSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public ColumnSeries(params TModel[] values) : base(values) { }
}

/// <summary>A column series of <see cref="double"/> values.</summary>
public class ColumnSeries : ColumnSeries<double>
{
    public ColumnSeries() { }

    public ColumnSeries(params double[] values) : base(values) { }
}
