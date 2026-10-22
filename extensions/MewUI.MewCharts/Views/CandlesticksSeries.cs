using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A candlestick (financial OHLC) series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type (e.g. FinancialPoint).</typeparam>
public class CandlesticksSeries<TModel>
    : CoreFinancialSeries<TModel, CandlestickGeometry, LabelGeometry, CircleGeometry>
{
    /// <summary>Initializes a series with no values.</summary>
    public CandlesticksSeries() : base(null) { }

    /// <summary>Initializes a series that plots the given collection; null plots nothing.</summary>
    public CandlesticksSeries(IReadOnlyCollection<TModel>? values) : base(values) { }
}
