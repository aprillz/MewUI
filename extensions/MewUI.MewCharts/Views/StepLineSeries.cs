using Aprillz.MewUI.MewCharts.Drawing.Geometries;

using LiveChartsCore;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>A step-line series rendered with the MewUI backend.</summary>
/// <typeparam name="TModel">The model type of the plotted values.</typeparam>
public class StepLineSeries<TModel>
    : CoreStepLineSeries<TModel, CircleGeometry, LabelGeometry, StepLineAreaGeometry, LineGeometry>
{
    /// <summary>Initializes a series with no values.</summary>
    public StepLineSeries() : base(null) { }

    /// <summary>Initializes a series that plots the given collection; null plots nothing.</summary>
    public StepLineSeries(IReadOnlyCollection<TModel>? values) : base(values) { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public StepLineSeries(params TModel[] values) : base(values) { }
}

/// <summary>A step-line series of <see cref="double"/> values.</summary>
public class StepLineSeries : StepLineSeries<double>
{
    /// <summary>Initializes a series with no values.</summary>
    public StepLineSeries() { }

    /// <summary>Initializes a series that plots the given values.</summary>
    public StepLineSeries(params double[] values) : base(values) { }
}
