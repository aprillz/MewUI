using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A polar line series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class PolarLineSeries<TModel>
    : CorePolarLineSeries<TModel, CircleGeometry, LabelGeometry, CubicBezierAreaGeometry, LineGeometry>
{
    public PolarLineSeries() : base(null) { }

    public PolarLineSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public PolarLineSeries(params TModel[] values) : base(values) { }
}

/// <summary>A polar line series of <see cref="double"/> values.</summary>
public class PolarLineSeries : PolarLineSeries<double>
{
    public PolarLineSeries() { }

    public PolarLineSeries(params double[] values) : base(values) { }
}
