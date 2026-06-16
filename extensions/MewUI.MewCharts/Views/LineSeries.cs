using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;
using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A line series with a custom point-marker geometry, rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
/// <typeparam name="TVisual">The point-marker geometry type.</typeparam>
public class LineSeries<TModel, TVisual>
    : CoreLineSeries<TModel, TVisual, LabelGeometry, CubicBezierAreaGeometry, LineGeometry>
    where TVisual : BoundedDrawnGeometry, new()
{
    public LineSeries() : base(null) { }

    public LineSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public LineSeries(params TModel[] values) : base(values) { }
}

/// <summary>A line series rendered with the MewUI backend geometries.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class LineSeries<TModel>
    : CoreLineSeries<TModel, CircleGeometry, LabelGeometry, CubicBezierAreaGeometry, LineGeometry>
{
    public LineSeries() : base(null) { }

    public LineSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public LineSeries(params TModel[] values) : base(values) { }
}

/// <summary>A line series of <see cref="double"/> values.</summary>
public class LineSeries : LineSeries<double>
{
    public LineSeries() { }

    public LineSeries(params double[] values) : base(values) { }
}
