using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Observers;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;

namespace Aprillz.MewUI.MewCharts.Views;

/// <inheritdoc cref="ICartesianChartView" />
public class CartesianChart : ChartViewBase, ICartesianChartView
{
    protected override Chart CreateCoreChart() => new CartesianChartEngine(this, CoreCanvas);

    /// <inheritdoc cref="ICartesianChartView.Core"/>
    public CartesianChartEngine Core => (CartesianChartEngine)CoreChart;

    public static readonly MewProperty<IEnumerable<ICartesianAxis>> XAxesProperty =
        MewProperty<IEnumerable<ICartesianAxis>>.Register<CartesianChart>(nameof(XAxes), Array.Empty<ICartesianAxis>(),
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(XAxes), value));

    public IEnumerable<ICartesianAxis> XAxes { get => GetValue(XAxesProperty); set => SetValue(XAxesProperty, value); }

    public static readonly MewProperty<IEnumerable<ICartesianAxis>> YAxesProperty =
        MewProperty<IEnumerable<ICartesianAxis>>.Register<CartesianChart>(nameof(YAxes), Array.Empty<ICartesianAxis>(),
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(YAxes), value));

    public IEnumerable<ICartesianAxis> YAxes { get => GetValue(YAxesProperty); set => SetValue(YAxesProperty, value); }

    public static readonly MewProperty<IEnumerable<IChartElement>> SectionsProperty =
        MewProperty<IEnumerable<IChartElement>>.Register<CartesianChart>(nameof(Sections), Array.Empty<IChartElement>(),
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(Sections), value));

    public IEnumerable<IChartElement> Sections { get => GetValue(SectionsProperty); set => SetValue(SectionsProperty, value); }

    public static readonly MewProperty<IChartElement?> DrawMarginFrameProperty =
        MewProperty<IChartElement?>.Register<CartesianChart>(nameof(DrawMarginFrame), null,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public IChartElement? DrawMarginFrame { get => GetValue(DrawMarginFrameProperty); set => SetValue(DrawMarginFrameProperty, value); }

    public static readonly MewProperty<ZoomAndPanMode> ZoomModeProperty =
        MewProperty<ZoomAndPanMode>.Register<CartesianChart>(nameof(ZoomMode), LiveCharts.DefaultSettings.ZoomMode,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public ZoomAndPanMode ZoomMode { get => GetValue(ZoomModeProperty); set => SetValue(ZoomModeProperty, value); }

    public static readonly MewProperty<double> ZoomingSpeedProperty =
        MewProperty<double>.Register<CartesianChart>(nameof(ZoomingSpeed), LiveCharts.DefaultSettings.ZoomSpeed,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public double ZoomingSpeed { get => GetValue(ZoomingSpeedProperty); set => SetValue(ZoomingSpeedProperty, value); }

    public static readonly MewProperty<FindingStrategy> FindingStrategyProperty =
        MewProperty<FindingStrategy>.Register<CartesianChart>(nameof(FindingStrategy), LiveCharts.DefaultSettings.FindingStrategy,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public FindingStrategy FindingStrategy { get => GetValue(FindingStrategyProperty); set => SetValue(FindingStrategyProperty, value); }

    public static readonly MewProperty<bool> MatchAxesScreenDataRatioProperty =
        MewProperty<bool>.Register<CartesianChart>(nameof(MatchAxesScreenDataRatio), false,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public bool MatchAxesScreenDataRatio { get => GetValue(MatchAxesScreenDataRatioProperty); set => SetValue(MatchAxesScreenDataRatioProperty, value); }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);

        // Only consume the wheel when zoom is enabled; otherwise let it bubble (e.g. to a
        // surrounding ScrollViewer) so charts don't trap scrolling.
        if (ZoomMode == ZoomAndPanMode.None) return;

        Core.Zoom(
            ZoomMode,
            ToChartPoint(e),
            e.Delta.Y > 0 ? ZoomDirection.ZoomIn : ZoomDirection.ZoomOut);
        e.Handled = true;
    }

    public LvcPointD ScalePixelsToData(LvcPointD point, int xAxisIndex = 0, int yAxisIndex = 0) =>
        Core.ScalePixelsToData(point, xAxisIndex, yAxisIndex);

    public LvcPointD ScaleDataToPixels(LvcPointD point, int xAxisIndex = 0, int yAxisIndex = 0) =>
        Core.ScaleDataToPixels(point, xAxisIndex, yAxisIndex);

    protected override ChartObserver ConfigureObserver(ChartObserver observe) =>
        base.ConfigureObserver(observe)
            .Collection(nameof(XAxes), () => XAxes)
            .Collection(nameof(YAxes), () => YAxes)
            .Collection(nameof(Sections), () => Sections);
}
