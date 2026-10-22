using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A stacked area series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class StackedAreaSeries<TModel>
    : CoreStackedAreaSeries<TModel, CircleGeometry, LabelGeometry, CubicBezierAreaGeometry, LineGeometry>
{
    /// <summary>Initializes a series with no values.</summary>
    public StackedAreaSeries() : base(null) { }

    /// <summary>Initializes a series that plots the given collection; null plots nothing.</summary>
    public StackedAreaSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public StackedAreaSeries(params TModel[] values) : base(values) { }
}

/// <summary>A stacked area series of <see cref="double"/> values.</summary>
public class StackedAreaSeries : StackedAreaSeries<double>
{
    /// <summary>Initializes a series with no values.</summary>
    public StackedAreaSeries() { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public StackedAreaSeries(params double[] values) : base(values) { }
}
