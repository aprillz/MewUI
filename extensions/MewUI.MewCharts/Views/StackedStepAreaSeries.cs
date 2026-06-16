using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A stacked step-area series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class StackedStepAreaSeries<TModel>
    : CoreStackedStepAreaSeries<TModel, CircleGeometry, LabelGeometry, StepLineAreaGeometry, LineGeometry>
{
    public StackedStepAreaSeries() : base(null) { }

    public StackedStepAreaSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public StackedStepAreaSeries(params TModel[] values) : base(values) { }
}

/// <summary>A stacked step-area series of <see cref="double"/> values.</summary>
public class StackedStepAreaSeries : StackedStepAreaSeries<double>
{
    public StackedStepAreaSeries() { }

    public StackedStepAreaSeries(params double[] values) : base(values) { }
}
