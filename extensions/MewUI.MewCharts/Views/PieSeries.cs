using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A pie/doughnut series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class PieSeries<TModel>
    : CorePieSeries<TModel, DoughnutGeometry, LabelGeometry, CircleGeometry>
{
    /// <summary>Initializes a series with no values.</summary>
    public PieSeries() : base(null, false, false) { }

    /// <summary>Initializes a series that plots the given collection; null plots nothing.</summary>
    public PieSeries(IReadOnlyCollection<TModel>? values) : base(values, false, false) { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public PieSeries(params TModel[] values) : base(values, false, false) { }

    /// <summary>Initializes an empty series flagged as a gauge or gauge-fill series, for <see cref="GaugeGenerator"/>.</summary>
    public PieSeries(bool isGauge, bool isGaugeFill) : base(null, isGauge, isGaugeFill) { }
}

/// <summary>A pie series of <see cref="double"/> values.</summary>
public class PieSeries : PieSeries<double>
{
    /// <summary>Initializes a series with no values.</summary>
    public PieSeries() { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public PieSeries(params double[] values) : base(values) { }
}
