using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A candlestick (financial OHLC) series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type (e.g. FinancialPoint).</typeparam>
public class CandlesticksSeries<TModel>
    : CoreFinancialSeries<TModel, CandlestickGeometry, LabelGeometry, CircleGeometry>
{
    public CandlesticksSeries() : base(null) { }

    public CandlesticksSeries(IReadOnlyCollection<TModel>? values) : base(values) { }
}
