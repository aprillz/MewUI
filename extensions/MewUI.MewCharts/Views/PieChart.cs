using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A chart control that plots pie, doughnut and gauge series.</summary>
public class PieChart : ChartViewBase, IPieChartView
{
    protected override Chart CreateCoreChart() => new PieChartEngine(this, CoreCanvas);

    /// <summary>Gets the pie chart engine behind this view.</summary>
    public PieChartEngine Core => (PieChartEngine)CoreChart;

    /// <summary>The angle in degrees at which the first slice starts; the default is 0.</summary>
    public static readonly MewProperty<double> InitialRotationProperty =
        MewProperty<double>.Register<PieChart>(nameof(InitialRotation), 0d,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    /// <summary>Gets or sets the angle in degrees at which the first slice starts; the default is 0.</summary>
    public double InitialRotation { get => GetValue(InitialRotationProperty); set => SetValue(InitialRotationProperty, value); }

    /// <summary>The total angle in degrees the slices span; the default is 360.</summary>
    public static readonly MewProperty<double> MaxAngleProperty =
        MewProperty<double>.Register<PieChart>(nameof(MaxAngle), 360d,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    /// <summary>Gets or sets the total angle in degrees the slices span; the default is 360.</summary>
    public double MaxAngle { get => GetValue(MaxAngleProperty); set => SetValue(MaxAngleProperty, value); }

    /// <summary>The minimum value a slice can represent, used by gauges; the default is 0.</summary>
    public static readonly MewProperty<double> MinValueProperty =
        MewProperty<double>.Register<PieChart>(nameof(MinValue), 0d,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    /// <summary>Gets or sets the minimum value a slice can represent, used by gauges; the default is 0.</summary>
    public double MinValue { get => GetValue(MinValueProperty); set => SetValue(MinValueProperty, value); }

    /// <summary>The maximum value a slice can represent, used by gauges; NaN (the default) uses the sum of all values.</summary>
    public static readonly MewProperty<double> MaxValueProperty =
        MewProperty<double>.Register<PieChart>(nameof(MaxValue), double.NaN,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    /// <summary>Gets or sets the maximum value a slice can represent, used by gauges; NaN (the default) uses the sum of all values.</summary>
    public double MaxValue { get => GetValue(MaxValueProperty); set => SetValue(MaxValueProperty, value); }

    /// <summary>Whether the slices are laid out clockwise; the default is true.</summary>
    public static readonly MewProperty<bool> IsClockwiseProperty =
        MewProperty<bool>.Register<PieChart>(nameof(IsClockwise), true,
            changed: (owner, _, _) => owner.CoreChart?.Update());
    /// <summary>Gets or sets whether the slices are laid out clockwise; the default is true.</summary>
    public bool IsClockwise { get => GetValue(IsClockwiseProperty); set => SetValue(IsClockwiseProperty, value); }
}
