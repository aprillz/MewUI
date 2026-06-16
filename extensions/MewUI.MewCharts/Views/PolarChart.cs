using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel.Observers;
using LiveChartsCore.Kernel.Sketches;

namespace Aprillz.MewUI.MewCharts.Views;

/// <inheritdoc cref="IPolarChartView" />
public class PolarChart : ChartViewBase, IPolarChartView
{
    protected override Chart CreateCoreChart() => new PolarChartEngine(this, CoreCanvas);

    /// <inheritdoc cref="IPolarChartView.Core"/>
    public PolarChartEngine Core => (PolarChartEngine)CoreChart;

    public static readonly MewProperty<bool> FitToBoundsProperty =
        MewProperty<bool>.Register<PolarChart>(nameof(FitToBounds), false,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    public bool FitToBounds { get => GetValue(FitToBoundsProperty); set => SetValue(FitToBoundsProperty, value); }

    public static readonly MewProperty<double> TotalAngleProperty =
        MewProperty<double>.Register<PolarChart>(nameof(TotalAngle), 360d,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    public double TotalAngle { get => GetValue(TotalAngleProperty); set => SetValue(TotalAngleProperty, value); }

    public static readonly MewProperty<double> InnerRadiusProperty =
        MewProperty<double>.Register<PolarChart>(nameof(InnerRadius), 0d,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    public double InnerRadius { get => GetValue(InnerRadiusProperty); set => SetValue(InnerRadiusProperty, value); }

    public static readonly MewProperty<double> InitialRotationProperty =
        MewProperty<double>.Register<PolarChart>(nameof(InitialRotation), 0d,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    public double InitialRotation { get => GetValue(InitialRotationProperty); set => SetValue(InitialRotationProperty, value); }

    public static readonly MewProperty<IEnumerable<IPolarAxis>> AngleAxesProperty =
        MewProperty<IEnumerable<IPolarAxis>>.Register<PolarChart>(nameof(AngleAxes), Array.Empty<IPolarAxis>(),
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(AngleAxes), value));
    public IEnumerable<IPolarAxis> AngleAxes { get => GetValue(AngleAxesProperty); set => SetValue(AngleAxesProperty, value); }

    public static readonly MewProperty<IEnumerable<IPolarAxis>> RadiusAxesProperty =
        MewProperty<IEnumerable<IPolarAxis>>.Register<PolarChart>(nameof(RadiusAxes), Array.Empty<IPolarAxis>(),
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(RadiusAxes), value));
    public IEnumerable<IPolarAxis> RadiusAxes { get => GetValue(RadiusAxesProperty); set => SetValue(RadiusAxesProperty, value); }

    public LvcPointD ScalePixelsToData(LvcPointD point, int angleAxisIndex = 0, int radiusAxisIndex = 0) =>
        Core.ScalePixelsToData(point, angleAxisIndex, radiusAxisIndex);

    public LvcPointD ScaleDataToPixels(LvcPointD point, int angleAxisIndex = 0, int radiusAxisIndex = 0) =>
        Core.ScaleDataToPixels(point, angleAxisIndex, radiusAxisIndex);

    protected override ChartObserver ConfigureObserver(ChartObserver observe) =>
        base.ConfigureObserver(observe)
            .Collection(nameof(AngleAxes), () => AngleAxes)
            .Collection(nameof(RadiusAxes), () => RadiusAxes);
}
