using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A stacked row (horizontal bar) series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class StackedRowSeries<TModel>
    : CoreStackedRowSeries<TModel, RoundedRectangleGeometry, LabelGeometry, LineGeometry>
{
    /// <summary>Initializes a series with no values.</summary>
    public StackedRowSeries() : base(null) { }

    /// <summary>Initializes a series that plots the given collection; null plots nothing.</summary>
    public StackedRowSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public StackedRowSeries(params TModel[] values) : base(values) { }
}

/// <summary>A stacked row series of <see cref="double"/> values.</summary>
public class StackedRowSeries : StackedRowSeries<double>
{
    /// <summary>Initializes a series with no values.</summary>
    public StackedRowSeries() { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public StackedRowSeries(params double[] values) : base(values) { }
}
