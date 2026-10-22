using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A stacked column series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class StackedColumnSeries<TModel>
    : CoreStackedColumnSeries<TModel, RoundedRectangleGeometry, LabelGeometry, LineGeometry>
{
    /// <summary>Initializes a series with no values.</summary>
    public StackedColumnSeries() : base(null) { }

    /// <summary>Initializes a series that plots the given collection; null plots nothing.</summary>
    public StackedColumnSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public StackedColumnSeries(params TModel[] values) : base(values) { }
}

/// <summary>A stacked column series of <see cref="double"/> values.</summary>
public class StackedColumnSeries : StackedColumnSeries<double>
{
    /// <summary>Initializes a series with no values.</summary>
    public StackedColumnSeries() { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public StackedColumnSeries(params double[] values) : base(values) { }
}
