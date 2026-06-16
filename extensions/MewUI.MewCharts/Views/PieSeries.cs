using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A pie/doughnut series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class PieSeries<TModel>
    : CorePieSeries<TModel, DoughnutGeometry, LabelGeometry, CircleGeometry>
{
    public PieSeries() : base(null, false, false) { }

    public PieSeries(IReadOnlyCollection<TModel>? values) : base(values, false, false) { }

    public PieSeries(params TModel[] values) : base(values, false, false) { }

    /// <summary>Gauge constructor (used by <see cref="GaugeGenerator"/>).</summary>
    public PieSeries(bool isGauge, bool isGaugeFill) : base(null, isGauge, isGaugeFill) { }
}

/// <summary>A pie series of <see cref="double"/> values.</summary>
public class PieSeries : PieSeries<double>
{
    public PieSeries() { }

    public PieSeries(params double[] values) : base(values) { }
}
