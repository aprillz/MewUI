using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Observers;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A chart control that plots series in a Cartesian coordinate system.</summary>
public class CartesianChart : ChartViewBase, ICartesianChartView
{
    protected override Chart CreateCoreChart() => new CartesianChartEngine(this, CoreCanvas);

    /// <summary>Gets the Cartesian chart engine behind this view.</summary>
    public CartesianChartEngine Core => (CartesianChartEngine)CoreChart;

    /// <summary>The X axes; empty (the default) uses one default <see cref="Axis"/>.</summary>
    public static readonly MewProperty<IEnumerable<ICartesianAxis>> XAxesProperty =
        MewProperty<IEnumerable<ICartesianAxis>>.Register<CartesianChart>(nameof(XAxes), Array.Empty<ICartesianAxis>(),
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(XAxes), value));

    /// <summary>Gets or sets the X axes; empty (the default) uses one default <see cref="Axis"/>.</summary>
    public IEnumerable<ICartesianAxis> XAxes { get => GetValue(XAxesProperty); set => SetValue(XAxesProperty, value); }

    /// <summary>The Y axes; empty (the default) uses one default <see cref="Axis"/>.</summary>
    public static readonly MewProperty<IEnumerable<ICartesianAxis>> YAxesProperty =
        MewProperty<IEnumerable<ICartesianAxis>>.Register<CartesianChart>(nameof(YAxes), Array.Empty<ICartesianAxis>(),
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(YAxes), value));

    /// <summary>Gets or sets the Y axes; empty (the default) uses one default <see cref="Axis"/>.</summary>
    public IEnumerable<ICartesianAxis> YAxes { get => GetValue(YAxesProperty); set => SetValue(YAxesProperty, value); }

    /// <summary>The sections (highlight bands) drawn over the plot area; the default is empty.</summary>
    public static readonly MewProperty<IEnumerable<IChartElement>> SectionsProperty =
        MewProperty<IEnumerable<IChartElement>>.Register<CartesianChart>(nameof(Sections), Array.Empty<IChartElement>(),
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(Sections), value));

    /// <summary>Gets or sets the sections (highlight bands) drawn over the plot area; the default is empty.</summary>
    public IEnumerable<IChartElement> Sections { get => GetValue(SectionsProperty); set => SetValue(SectionsProperty, value); }

    /// <summary>The frame drawn around the plot area; null (the default) draws no frame.</summary>
    public static readonly MewProperty<IChartElement?> DrawMarginFrameProperty =
        MewProperty<IChartElement?>.Register<CartesianChart>(nameof(DrawMarginFrame), null,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    /// <summary>Gets or sets the frame drawn around the plot area; null (the default) draws no frame.</summary>
    public IChartElement? DrawMarginFrame { get => GetValue(DrawMarginFrameProperty); set => SetValue(DrawMarginFrameProperty, value); }

    /// <summary>The zoom and pan mode for the mouse wheel and drag; defaults to <see cref="LiveChartsSettings.ZoomMode"/>.</summary>
    public static readonly MewProperty<ZoomAndPanMode> ZoomModeProperty =
        MewProperty<ZoomAndPanMode>.Register<CartesianChart>(nameof(ZoomMode), LiveCharts.DefaultSettings.ZoomMode,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    /// <summary>Gets or sets the zoom and pan mode for the mouse wheel and drag; defaults to <see cref="LiveChartsSettings.ZoomMode"/>.</summary>
    public ZoomAndPanMode ZoomMode { get => GetValue(ZoomModeProperty); set => SetValue(ZoomModeProperty, value); }

    /// <summary>The zooming speed from 0 (slowest) to 1 (fastest); defaults to <see cref="LiveChartsSettings.ZoomSpeed"/>.</summary>
    public static readonly MewProperty<double> ZoomingSpeedProperty =
        MewProperty<double>.Register<CartesianChart>(nameof(ZoomingSpeed), LiveCharts.DefaultSettings.ZoomSpeed,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    /// <summary>Gets or sets the zooming speed from 0 (slowest) to 1 (fastest); defaults to <see cref="LiveChartsSettings.ZoomSpeed"/>.</summary>
    public double ZoomingSpeed { get => GetValue(ZoomingSpeedProperty); set => SetValue(ZoomingSpeedProperty, value); }

    /// <summary>The strategy that selects the points hit by the pointer; defaults to <see cref="LiveChartsSettings.FindingStrategy"/>.</summary>
    public static readonly MewProperty<FindingStrategy> FindingStrategyProperty =
        MewProperty<FindingStrategy>.Register<CartesianChart>(nameof(FindingStrategy), LiveCharts.DefaultSettings.FindingStrategy,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    /// <summary>Gets or sets the strategy that selects the points hit by the pointer; defaults to <see cref="LiveChartsSettings.FindingStrategy"/>.</summary>
    public FindingStrategy FindingStrategy { get => GetValue(FindingStrategyProperty); set => SetValue(FindingStrategyProperty, value); }

    /// <summary>Whether the X and Y axes use the same scale of pixels per data unit; the default is false.</summary>
    public static readonly MewProperty<bool> MatchAxesScreenDataRatioProperty =
        MewProperty<bool>.Register<CartesianChart>(nameof(MatchAxesScreenDataRatio), false,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    /// <summary>Gets or sets whether the X and Y axes use the same scale of pixels per data unit; the default is false.</summary>
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

    /// <summary>Converts a point in chart coordinates to data values on the given X and Y axes.</summary>
    public LvcPointD ScalePixelsToData(LvcPointD point, int xAxisIndex = 0, int yAxisIndex = 0) =>
        Core.ScalePixelsToData(point, xAxisIndex, yAxisIndex);

    /// <summary>Converts data values on the given X and Y axes to a point in chart coordinates.</summary>
    public LvcPointD ScaleDataToPixels(LvcPointD point, int xAxisIndex = 0, int yAxisIndex = 0) =>
        Core.ScaleDataToPixels(point, xAxisIndex, yAxisIndex);

    protected override ChartObserver ConfigureObserver(ChartObserver observe) =>
        base.ConfigureObserver(observe)
            .Collection(nameof(XAxes), () => XAxes)
            .Collection(nameof(YAxes), () => YAxes)
            .Collection(nameof(Sections), () => Sections);
}
