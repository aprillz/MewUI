using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A row (horizontal bar) series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class RowSeries<TModel>
    : CoreRowSeries<TModel, RoundedRectangleGeometry, LabelGeometry, LineGeometry>
{
    /// <summary>Initializes a series with no values.</summary>
    public RowSeries() : base(null) { }

    /// <summary>Initializes a series that plots the given collection; null plots nothing.</summary>
    public RowSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public RowSeries(params TModel[] values) : base(values) { }
}

/// <summary>A row series of <see cref="double"/> values.</summary>
public class RowSeries : RowSeries<double>
{
    /// <summary>Initializes a series with no values.</summary>
    public RowSeries() { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public RowSeries(params double[] values) : base(values) { }
}
