using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A step-line series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class StepLineSeries<TModel>
    : CoreStepLineSeries<TModel, CircleGeometry, LabelGeometry, StepLineAreaGeometry, LineGeometry>
{
    public StepLineSeries() : base(null) { }

    public StepLineSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    public StepLineSeries(params TModel[] values) : base(values) { }
}

/// <summary>A step-line series of <see cref="double"/> values.</summary>
public class StepLineSeries : StepLineSeries<double>
{
    public StepLineSeries() { }

    public StepLineSeries(params double[] values) : base(values) { }
}
