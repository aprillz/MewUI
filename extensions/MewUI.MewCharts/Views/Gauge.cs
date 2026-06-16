using System.Collections.ObjectModel;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>Gauge build options.</summary>
public enum GaugeOptions { None, Solid, Angular }

/// <summary>An item in a gauge built by <see cref="GaugeGenerator"/>.</summary>
public class GaugeItem
{
    public static double Background { get; } = double.MaxValue;

    public GaugeItem(double value, Action<PieSeries<ObservableValue>>? builder = null)
    {
        Value = new ObservableValue(value);
        Builder = builder;
        if (value == Background) IsFillSeriesBuilder = true;
    }

    public ObservableValue Value { get; set; }
    public Action<PieSeries<ObservableValue>>? Builder { get; set; }
    public bool IsFillSeriesBuilder { get; internal set; }
}

/// <summary>
/// Builds gauge series (solid radial gauges) as pie series, ported from the LiveCharts
/// SkiaSharp GaugeGenerator. Reuses the <c>PieChartEngine</c> to render gauges.
/// </summary>
public static class GaugeGenerator
{
    public static PieSeries<ObservableValue>[] BuildSolidGauge(params GaugeItem[] items)
    {
        if (!items.Any(x => x.IsFillSeriesBuilder))
            items = [.. items, new GaugeItem(GaugeItem.Background)];
        return Build(GaugeOptions.Solid, items);
    }

    public static PieSeries<ObservableValue>[] BuildAngularGaugeSections(params GaugeItem[] items)
    {
        if (!items.Any(x => x.IsFillSeriesBuilder))
            items = [.. items, new GaugeItem(GaugeItem.Background)];
        return Build(GaugeOptions.Angular, items);
    }

    private static PieSeries<ObservableValue>[] Build(GaugeOptions options, params GaugeItem[] items)
    {
        List<GaugeItem> seriesRules = [];
        List<GaugeItem> backgroundRules = [];

        foreach (var item in items)
        {
            if (item.IsFillSeriesBuilder) backgroundRules.Add(item);
            else seriesRules.Add(item);
        }

        var count = seriesRules.Count;
        var i = 0;
        var series = seriesRules.Select(item =>
        {
            var builder = item.Builder;
            return AsSeries(item.Value, builder, i++, count, options);
        }).ToArray();

        var fillSeriesValues = new List<ObservableValue>();
        while (fillSeriesValues.Count < items.Length - 1) fillSeriesValues.Add(new ObservableValue(0));

        var backgroundSeries = new PieSeries<ObservableValue>(true, true)
        {
            ZIndex = -1,
            IsFillSeries = true,
            IsVisibleAtLegend = false,
            Values = fillSeriesValues,
        };

        if (options == GaugeOptions.Angular)
        {
            backgroundSeries.HoverPushout = 0;
            backgroundSeries.IsHoverable = false;
            backgroundSeries.DataLabelsPaint = null;
        }

        foreach (var rule in backgroundRules)
            rule.Builder?.Invoke(backgroundSeries);

        return [.. series, backgroundSeries];
    }

    private static PieSeries<ObservableValue> AsSeries(
        ObservableValue instance, Action<PieSeries<ObservableValue>>? builder, int i, int count, GaugeOptions options)
    {
        var series = new PieSeries<ObservableValue>();
        ((IInternalSeries)series).SeriesProperties |= SeriesProperties.Gauge;

        ObservableCollection<ObservableValue> values;
        if (options == GaugeOptions.Solid)
        {
            // Pad with null (not zero-value) placeholders so only the real value draws a slice/label;
            // zero-value entries would render spurious "0" data labels and zero-sweep slices.
            values = new ObservableCollection<ObservableValue>();
            while (values.Count < count - 1) values.Add(null!);
            values.Insert(i, instance);
        }
        else
        {
            values = [instance];
            if (options == GaugeOptions.Angular)
            {
                series.HoverPushout = 0;
                series.IsHoverable = false;
                series.DataLabelsPaint = null;
                series.AnimationsSpeed = TimeSpan.FromSeconds(0);
                series.IsRelativeToMinValue = true;
            }
        }

        series.Values = values;
        series.HoverPushout = 0;
        builder?.Invoke(series);

        return series;
    }
}
