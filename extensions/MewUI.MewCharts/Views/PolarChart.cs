using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Observers;
using LiveChartsCore.Kernel.Sketches;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A chart control that plots series in a polar coordinate system.</summary>
public class PolarChart : ChartViewBase, IPolarChartView
{
    protected override Chart CreateCoreChart() => new PolarChartEngine(this, CoreCanvas);

    /// <summary>Gets the polar chart engine behind this view.</summary>
    public PolarChartEngine Core => (PolarChartEngine)CoreChart;

    /// <summary>Whether the plot is scaled and re-centered to fit the series bounds; the default is false.</summary>
    public static readonly MewProperty<bool> FitToBoundsProperty =
        MewProperty<bool>.Register<PolarChart>(nameof(FitToBounds), false,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    /// <summary>Gets or sets whether the plot is scaled and re-centered to fit the series bounds; the default is false.</summary>
    public bool FitToBounds { get => GetValue(FitToBoundsProperty); set => SetValue(FitToBoundsProperty, value); }

    /// <summary>The total circumference angle in degrees; the default is 360.</summary>
    public static readonly MewProperty<double> TotalAngleProperty =
        MewProperty<double>.Register<PolarChart>(nameof(TotalAngle), 360d,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    /// <summary>Gets or sets the total circumference angle in degrees; the default is 360.</summary>
    public double TotalAngle { get => GetValue(TotalAngleProperty); set => SetValue(TotalAngleProperty, value); }

    /// <summary>The inner radius of the plot; the default is 0.</summary>
    public static readonly MewProperty<double> InnerRadiusProperty =
        MewProperty<double>.Register<PolarChart>(nameof(InnerRadius), 0d,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    /// <summary>Gets or sets the inner radius of the plot; the default is 0.</summary>
    public double InnerRadius { get => GetValue(InnerRadiusProperty); set => SetValue(InnerRadiusProperty, value); }

    /// <summary>The angle in degrees at which the angle axis starts; the default is 0.</summary>
    public static readonly MewProperty<double> InitialRotationProperty =
        MewProperty<double>.Register<PolarChart>(nameof(InitialRotation), 0d,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    /// <summary>Gets or sets the angle in degrees at which the angle axis starts; the default is 0.</summary>
    public double InitialRotation { get => GetValue(InitialRotationProperty); set => SetValue(InitialRotationProperty, value); }

    /// <summary>The angle axes; empty (the default) uses one default <see cref="PolarAxis"/>.</summary>
    public static readonly MewProperty<IEnumerable<IPolarAxis>> AngleAxesProperty =
        MewProperty<IEnumerable<IPolarAxis>>.Register<PolarChart>(nameof(AngleAxes), Array.Empty<IPolarAxis>(),
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(AngleAxes), value));
    /// <summary>Gets or sets the angle axes; empty (the default) uses one default <see cref="PolarAxis"/>.</summary>
    public IEnumerable<IPolarAxis> AngleAxes { get => GetValue(AngleAxesProperty); set => SetValue(AngleAxesProperty, value); }

    /// <summary>The radius axes; empty (the default) uses one default <see cref="PolarAxis"/>.</summary>
    public static readonly MewProperty<IEnumerable<IPolarAxis>> RadiusAxesProperty =
        MewProperty<IEnumerable<IPolarAxis>>.Register<PolarChart>(nameof(RadiusAxes), Array.Empty<IPolarAxis>(),
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(RadiusAxes), value));
    /// <summary>Gets or sets the radius axes; empty (the default) uses one default <see cref="PolarAxis"/>.</summary>
    public IEnumerable<IPolarAxis> RadiusAxes { get => GetValue(RadiusAxesProperty); set => SetValue(RadiusAxesProperty, value); }

    /// <summary>Converts a point in chart coordinates to data values on the given angle and radius axes.</summary>
    public LvcPointD ScalePixelsToData(LvcPointD point, int angleAxisIndex = 0, int radiusAxisIndex = 0) =>
        Core.ScalePixelsToData(point, angleAxisIndex, radiusAxisIndex);

    /// <summary>Converts data values on the given angle and radius axes to a point in chart coordinates.</summary>
    public LvcPointD ScaleDataToPixels(LvcPointD point, int angleAxisIndex = 0, int radiusAxisIndex = 0) =>
        Core.ScaleDataToPixels(point, angleAxisIndex, radiusAxisIndex);

    protected override ChartObserver ConfigureObserver(ChartObserver observe) =>
        base.ConfigureObserver(observe)
            .Collection(nameof(AngleAxes), () => AngleAxes)
            .Collection(nameof(RadiusAxes), () => RadiusAxes);
}
