using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A polar line series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class PolarLineSeries<TModel>
    : CorePolarLineSeries<TModel, CircleGeometry, LabelGeometry, CubicBezierAreaGeometry, LineGeometry>
{
    /// <summary>Initializes a series with no values.</summary>
    public PolarLineSeries() : base(null) { }

    /// <summary>Initializes a series that plots the given collection; null plots nothing.</summary>
    public PolarLineSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public PolarLineSeries(params TModel[] values) : base(values) { }
}

/// <summary>A polar line series of <see cref="double"/> values.</summary>
public class PolarLineSeries : PolarLineSeries<double>
{
    /// <summary>Initializes a series with no values.</summary>
    public PolarLineSeries() { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public PolarLineSeries(params double[] values) : base(values) { }
}
