using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.VisualStates;

// Backing point for the logarithmic axis sample; mapped into log space via LiveCharts.Configure.
sealed class LogarithmicPoint(double x, double y)
{
    public double X { get; set; } = x;
    public double Y { get; set; } = y;
}

// User-defined type for the UserDefinedTypes sample; mapped via LiveCharts.Configure.
sealed class City(string name, double population)
{
    public string Name { get; set; } = name;
    public double Population { get; set; } = population;
}

// Raw sales row for Pies/Nested (country > zone > salesperson hierarchy).
sealed class SalesRecord(string country, string zone, string name, double value)
{
    public string Country { get; set; } = country;
    public string Zone { get; set; } = zone;
    public string Name { get; set; } = name;
    public double Value { get; set; } = value;
}

// One nested-pie series: the 3-element Values array places the arc on a concentric ring
// (index 0 = inner/people, 1 = middle/zones, 2 = outer/countries); only one slot is set.
sealed class NestedPieData(string name, double?[] values, string color)
{
    public string Name { get; set; } = name;
    public double?[] Values { get; set; } = values;
    public string Color { get; set; } = color;
    public bool IsTotal => Name is "Brazil" or "Colombia" or "Mexico";
    public Func<ChartPoint, string> Formatter { get; } = point =>
        $"{name}{Environment.NewLine}{point.StackedValue!.Share:P2}";
}

// Backing point for Lines/CustomPoints: carries a per-point marker rotation.
sealed class RotatedPoint(double value, double rotation)
{
    public double Value { get; set; } = value;
    public double Rotation { get; set; } = rotation;
}

// Line series that rotates each point's marker by the data point's Rotation when it is measured.
sealed class RotatingLineSeries : LineSeries<RotatedPoint>
{
    public RotatingLineSeries(IReadOnlyCollection<RotatedPoint> values) : base(values) { }

    protected override void OnPointMeasured(ChartPoint point)
    {
        base.OnPointMeasured(point);
        if (point.Context.DataSource is RotatedPoint data && point.Context.Visual is not null)
            point.Context.Visual.RotateTransform = (float)data.Rotation;
    }
}

// Scatter chart that adds a data point wherever the user clicks (Events/AddPointOnClick).
sealed class ClickToAddChart : CartesianChart
{
    public ObservableCollection<ObservablePoint> Points { get; } = [new(0, 5), new(3, 8), new(7, 9)];

    public ClickToAddChart() => Series = [new ScatterSeries<ObservablePoint>(Points)];

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var pos = e.GetPosition(this);
        var data = ScalePixelsToData(new LvcPointD(pos.X, pos.Y));
        Points.Add(new ObservablePoint(data.X, data.Y));
    }
}

// Column series whose bars turn red (a "Danger" visual state) when their value exceeds 5.
sealed class ConditionalColumnSeries : ColumnSeries<ObservableValue>
{
    public ConditionalColumnSeries(IReadOnlyCollection<ObservableValue> values) : base(values)
    {
        ShowDataLabels = true;
        DataLabelsSize = 15;
        DataLabelsPaint = new SolidColorPaint(new Color(255, 70, 70, 70));
        this.HasState("Danger", [(nameof(IDrawnElement.Fill), new SolidColorPaint(new Color(255, 244, 67, 54)))]);
    }

    protected override void OnPointMeasured(ChartPoint point)
    {
        base.OnPointMeasured(point);
        if (point.Context.DataSource is not ObservableValue observable) return;
        var states = point.Context.Series.VisualStates;
        if (observable.Value > 5) states.SetState("Danger", point.Context.Visual);
        else states.ClearState("Danger", point.Context.Visual);
    }
}

// Column series that staggers each bar's grow-in animation by its index (Bars/DelayedAnimation).
sealed class DelayedColumnSeries : ColumnSeries<float>
{
    private readonly int _count;

    public DelayedColumnSeries(IReadOnlyCollection<float> values) : base(values) => _count = values.Count;

    protected override void OnPointMeasured(ChartPoint point)
    {
        base.OnPointMeasured(point);
        var index = point.Context.Entity.MetaData!.EntityIndex;
        var delay = index / (float)Math.Max(1, _count);
        var animation = new Animation(t => DelayedEase(t, delay), TimeSpan.FromSeconds(0.5));
        point.Context.Visual?.SetTransition(animation);
    }

    private static float DelayedEase(float t, float delay)
    {
        if (t <= delay) return 0f;
        var remapped = (t - delay) / (1f - delay);
        return EasingFunctions.ExponentialOut(Math.Clamp(remapped, 0f, 1f));
    }
}

// Lower "scrollbar" chart for General/Scrollable: dragging centers the thumb on the pointer and
// pans the linked main X axis to the same window.
sealed class ScrollbarChart : CartesianChart
{
    private readonly Axis _mainX;
    private readonly RectangularSection _thumb;
    private readonly double _dataMin;
    private readonly double _dataMax;
    private bool _isDown;

    public ScrollbarChart(Axis mainX, RectangularSection thumb, double dataMin, double dataMax)
    {
        _mainX = mainX;
        _thumb = thumb;
        _dataMin = dataMin;
        _dataMax = dataMax;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        _isDown = true;
        UpdateFromPointer(e);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_isDown) UpdateFromPointer(e);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        _isDown = false;
    }

    private void UpdateFromPointer(MouseEventArgs e)
    {
        var pos = e.GetPosition(this);
        var data = ScalePixelsToData(new LvcPointD(pos.X, pos.Y));
        var range = (_thumb.Xj ?? _dataMax) - (_thumb.Xi ?? _dataMin);
        var min = data.X - range / 2;
        var max = data.X + range / 2;
        if (min < _dataMin) { min = _dataMin; max = min + range; }
        if (max > _dataMax) { max = _dataMax; min = max - range; }
        _thumb.Xi = min;
        _thumb.Xj = max;
        _mainX.MinLimit = min;
        _mainX.MaxLimit = max;
    }
}
