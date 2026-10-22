using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A heat-map series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type (e.g. WeightedPoint).</typeparam>
public class HeatSeries<TModel>
    : CoreHeatSeries<TModel, ColoredRectangleGeometry, LabelGeometry>
{
    /// <summary>Initializes a series with no values.</summary>
    public HeatSeries() : base(null) { }

    /// <summary>Initializes a series that plots the given collection; null plots nothing.</summary>
    public HeatSeries(IReadOnlyCollection<TModel>? values) : base(values) { }
}
