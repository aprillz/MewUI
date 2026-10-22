using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A box-and-whisker series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type (e.g. BoxValue).</typeparam>
public class BoxSeries<TModel>
    : CoreBoxSeries<TModel, BoxGeometry, LabelGeometry, CircleGeometry>
{
    /// <summary>Initializes a series with no values.</summary>
    public BoxSeries() : base(null) { }

    /// <summary>Initializes a series that plots the given collection; null plots nothing.</summary>
    public BoxSeries(IReadOnlyCollection<TModel>? values) : base(values) { }
}
