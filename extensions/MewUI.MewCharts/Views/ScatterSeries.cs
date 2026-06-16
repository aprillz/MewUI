using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;
using LiveChartsCore.Drawing;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A scatter series with a custom point-marker geometry, rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
/// <typeparam name="TVisual">The point-marker geometry type.</typeparam>
public class ScatterSeries<TModel, TVisual>
    : CoreScatterSeries<TModel, TVisual, LabelGeometry, LineGeometry>
    where TVisual : BoundedDrawnGeometry, new()
{
    public ScatterSeries() : base(null) { }

    public ScatterSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public ScatterSeries(params TModel[] values) : base(values) { }
}

/// <summary>A scatter series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class ScatterSeries<TModel>
    : CoreScatterSeries<TModel, CircleGeometry, LabelGeometry, LineGeometry>
{
    public ScatterSeries() : base(null) { }

    public ScatterSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public ScatterSeries(params TModel[] values) : base(values) { }
}

/// <summary>A scatter series of <see cref="double"/> values.</summary>
public class ScatterSeries : ScatterSeries<double>
{
    public ScatterSeries() { }

    public ScatterSeries(params double[] values) : base(values) { }
}
