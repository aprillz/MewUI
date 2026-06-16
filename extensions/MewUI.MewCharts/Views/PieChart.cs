using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;

namespace Aprillz.MewUI.MewCharts.Views;

/// <inheritdoc cref="IPieChartView" />
public class PieChart : ChartViewBase, IPieChartView
{
    protected override Chart CreateCoreChart() => new PieChartEngine(this, CoreCanvas);

    /// <inheritdoc cref="IPieChartView.Core"/>
    public PieChartEngine Core => (PieChartEngine)CoreChart;

    public static readonly MewProperty<double> InitialRotationProperty =
        MewProperty<double>.Register<PieChart>(nameof(InitialRotation), 0d,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    public double InitialRotation { get => GetValue(InitialRotationProperty); set => SetValue(InitialRotationProperty, value); }

    public static readonly MewProperty<double> MaxAngleProperty =
        MewProperty<double>.Register<PieChart>(nameof(MaxAngle), 360d,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    public double MaxAngle { get => GetValue(MaxAngleProperty); set => SetValue(MaxAngleProperty, value); }

    public static readonly MewProperty<double> MinValueProperty =
        MewProperty<double>.Register<PieChart>(nameof(MinValue), 0d,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    public double MinValue { get => GetValue(MinValueProperty); set => SetValue(MinValueProperty, value); }

    public static readonly MewProperty<double> MaxValueProperty =
        MewProperty<double>.Register<PieChart>(nameof(MaxValue), double.NaN,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    public double MaxValue { get => GetValue(MaxValueProperty); set => SetValue(MaxValueProperty, value); }

    public static readonly MewProperty<bool> IsClockwiseProperty =
        MewProperty<bool>.Register<PieChart>(nameof(IsClockwise), true,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    public bool IsClockwise { get => GetValue(IsClockwiseProperty); set => SetValue(IsClockwiseProperty, value); }
}
