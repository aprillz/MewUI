using System.Collections.ObjectModel;

using Aprillz.MewUI;
using Aprillz.MewUI.Input;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.MewCharts.Drawing.Geometries;
using Aprillz.MewUI.MewCharts.Drawing.Layouts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;

using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Drawing.Layouts;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Drawing;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.Motion;

namespace GeneralSamples;

// Stands in for the upstream PointerPressedCommand, PointerMoveCommand and PointerReleasedCommand bindings.
sealed class PointerCartesianChart : CartesianChart
{
    public event Action<PointerCartesianChart, LvcPointD>? PointerPressed;
    public event Action<PointerCartesianChart, LvcPointD>? PointerMoved;
    public event Action<PointerCartesianChart, LvcPointD>? PointerReleased;

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        PointerPressed?.Invoke(this, ToPointerPosition(e));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        PointerMoved?.Invoke(this, ToPointerPosition(e));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        PointerReleased?.Invoke(this, ToPointerPosition(e));
    }

    private LvcPointD ToPointerPosition(MouseEventArgs e)
    {
        var position = e.GetPosition(this);
        return new LvcPointD(position.X, position.Y);
    }
}

// General/DrawOnCanvas: places the animated geometry on every update and resizes it on every press.
sealed class DrawOnCanvasViewModel
{
    private bool _isBig;
    private readonly MotionGeometry _geometry = new()
    {
        Diameter = 20,
        Fill = new SolidColorPaint(Color.Blue.WithAlpha(100)),
        Stroke = new SolidColorPaint(Color.Blue, 3),
    };

    public void ChartUpdated(IChartView chart)
    {
        var chartView = (ICartesianChartView)chart;

        var locationInChartValues = new LvcPointD(5, 5);
        var locationInPixels = chartView.ScaleDataToPixels(locationInChartValues);

        _geometry.X = (float)locationInPixels.X;
        _geometry.Y = (float)locationInPixels.Y;

        _geometry.Initialize(chart);
    }

    public void ChartPressed(IChartView chart)
    {
        _geometry.Diameter = _isBig ? 20 : 70;
        _isBig = !_isBig;
        chart.Invalidate();
    }
}

// The LiveCharts source generator does not run in this project, so Diameter's motion property is written by hand.
sealed class MotionGeometry : BoundedDrawnGeometry, IDrawnElement<MewDrawingContext>
{
    public static readonly PropertyDefinition DiameterProperty = new(
        propertyName: nameof(Diameter),
        propertyType: typeof(float),
        getter: geometry => ((MotionGeometry)geometry).Diameter,
        setter: (geometry, value) => ((MotionGeometry)geometry).Diameter = value is float diameter ? diameter : 0f,
        motionGetter: geometry => ((MotionGeometry)geometry)._diameterMotionProperty);

    public static new Dictionary<string, PropertyDefinition> PropertyDefinitions { get; } =
        Merge(new() { [nameof(Diameter)] = DiameterProperty }, BoundedDrawnGeometry.PropertyDefinitions);

    private readonly FloatMotionProperty _diameterMotionProperty = new();
    private bool _isInitialized;

    public float Diameter
    {
        get => _diameterMotionProperty.GetMovement(this);
        set => _diameterMotionProperty.SetMovement(value, this);
    }

    public void Draw(MewDrawingContext context)
    {
        // Upstream passes Diameter to SKCanvas.DrawCircle, which takes a radius.
        var radius = Diameter;
        context.DrawEllipse(new Rect(X - radius, Y - radius, radius * 2, radius * 2));
    }

    public void Initialize(IChartView chart)
    {
        if (_isInitialized) return;

        chart.CoreChart.Canvas.AddGeometry(this);

        var animation = new LiveChartsCore.Drawing.Animation(
            easingFunction: EasingFunctions.BounceOut,
            duration: TimeSpan.FromMilliseconds(800));

        this.Animate(animation);

        _isInitialized = true;
    }

    protected override Dictionary<string, PropertyDefinition> GetPropertyDefinitions() => PropertyDefinitions;
}

// General/MultiThreading: background tasks append points while holding the chart's SyncContext lock.
sealed class MultiThreadingViewModel
{
    private const int INITIAL_POINTS = 1500;
    private const int READ_TASKS = 10;
    private const int DELAY_MS = 1;

    private int _current;

    public MultiThreadingViewModel()
    {
        var items = new List<int>();
        for (var index = 0; index < INITIAL_POINTS; index++)
        {
            _current += Random.Shared.Next(-9, 10);
            items.Add(_current);
        }

        Values = [.. items];

        for (var index = 0; index < READ_TASKS; index++)
        {
            _ = Task.Run(ReadData);
        }
    }

    public ObservableCollection<int> Values { get; }

    public object Sync { get; } = new();

    public bool IsReading { get; set; } = true;

    private async Task ReadData()
    {
        await Task.Delay(1000);

        while (IsReading)
        {
            await Task.Delay(DELAY_MS);

            // Random.Shared because the tasks draw numbers concurrently outside the lock.
            _current = Interlocked.Add(ref _current, Random.Shared.Next(-9, 10));

            lock (Sync)
            {
                Values.Add(_current);
                Values.RemoveAt(0);
            }
        }
    }
}

// General/MultiThreading2: background tasks marshal every change to the UI thread instead of locking.
sealed class MultiThreading2ViewModel
{
    private const int INITIAL_POINTS = 1500;
    private const int READ_TASKS = 10;
    private const int DELAY_MS = 1;

    private readonly Random _random = new();
    private readonly Action<Action> _dispatcherService;
    private int _current;

    public MultiThreading2ViewModel(Action<Action> dispatcherService)
    {
        var items = new List<int>();
        for (var index = 0; index < INITIAL_POINTS; index++)
        {
            _current += _random.Next(-9, 10);
            items.Add(_current);
        }

        Values = [.. items];

        _dispatcherService = dispatcherService;

        for (var index = 0; index < READ_TASKS; index++)
        {
            _ = Task.Run(ReadData);
        }
    }

    public ObservableCollection<int> Values { get; }

    public bool IsReading { get; set; } = true;

    private async Task ReadData()
    {
        await Task.Delay(1000);

        while (IsReading)
        {
            await Task.Delay(DELAY_MS);

            _dispatcherService(() =>
            {
                _current += _random.Next(-9, 10);
                Values.Add(_current);
                Values.RemoveAt(0);
            });
        }
    }
}

// General/Scrollable: keeps the scroll bar thumb and the main chart's visible X range in sync.
sealed class ScrollableViewModel(ObservablePoint[] values, ICartesianAxis mainAxis, RectangularSection thumb)
{
    private const double DEFAULT_MIN = 0;
    private const double DEFAULT_MAX = 100;

    private bool _isDown;

    public void ChartUpdated(IChartView chart)
    {
        var cartesianChart = (CartesianChartEngine)chart.CoreChart;
        var axis = cartesianChart.XAxes.First();

        thumb.Xi = axis.MinLimit ?? DEFAULT_MIN;
        thumb.Xj = axis.MaxLimit ?? DEFAULT_MAX;
    }

    public void PointerDown(PointerCartesianChart chart, LvcPointD position) => _isDown = true;

    public void PointerMove(PointerCartesianChart chart, LvcPointD position)
    {
        if (!_isDown) return;

        var positionInData = chart.ScalePixelsToData(position);

        var currentRange = (thumb.Xj ?? DEFAULT_MAX) - (thumb.Xi ?? DEFAULT_MIN);

        var min = positionInData.X - currentRange / 2;
        var max = positionInData.X + currentRange / 2;

        if (min < values[0].X)
        {
            min = values[0].X ?? DEFAULT_MIN;
            max = min + currentRange;
        }
        if (max > values[^1].X)
        {
            max = values[^1].X ?? DEFAULT_MAX;
            min = max - currentRange;
        }

        thumb.Xi = min;
        thumb.Xj = max;

        mainAxis.MinLimit = min;
        mainAxis.MaxLimit = max;
    }

    public void PointerUp(PointerCartesianChart chart, LvcPointD position) => _isDown = false;
}

// SaveImage draws the current frame, so the PNG waits until the latest update has finished animating.
sealed class ChartImageExport
{
    private readonly ChartViewBase _chart;
    private long _lastUpdateFinishedAt = -1;

    public ChartImageExport(ChartViewBase chart, string path)
    {
        _chart = chart;
        Path = path;
        chart.UpdateFinished += _ => Interlocked.Exchange(ref _lastUpdateFinishedAt, Environment.TickCount64);
    }

    public string Path { get; }

    public bool IsSaved { get; private set; }

    public void TrySave()
    {
        if (IsSaved) return;

        var finishedAt = Interlocked.Read(ref _lastUpdateFinishedAt);
        if (finishedAt < 0 || _chart.Bounds.Width <= 0 || _chart.Bounds.Height <= 0) return;
        if (Environment.TickCount64 - finishedAt < _chart.CoreChart.ActualAnimationsSpeed.TotalMilliseconds) return;

        _chart.SaveImage(Path);
        IsSaved = true;
    }
}

// General/TemplatedTooltips: the plotted type, mapped to coordinates through LiveCharts.Configure.
sealed class GeometryPoint
{
    public double Value { get; set; }
    public string? Geometry { get; set; }
}

// General/TemplatedTooltips: a bouncing pop-up listing the miniature, the value as currency and the point's shape.
sealed class CustomTooltip : MewDefaultTooltip
{
    protected override void Initialize(Chart chart)
    {
        var theme = chart.GetTheme();

        Wedge = 5;

        Geometry.Fill = theme.TooltipBackgroundPaint;
        Geometry.Stroke = new SolidColorPaint(new Color(100, 180, 180, 180), 3);
        Geometry.Wedge = Wedge;
        Geometry.WedgeThickness = 2;

        this.Animate(
            new LiveChartsCore.Drawing.Animation(
                EasingFunctions.BounceOut,
                TimeSpan.FromMilliseconds(500)));
    }

    protected override Layout<MewDrawingContext> GetLayout(IEnumerable<ChartPoint> foundPoints, Chart chart)
    {
        var theme = chart.GetTheme();

        var layout = new StackLayout
        {
            Padding = new Padding(10),
            Orientation = ContainerOrientation.Vertical,
            HorizontalAlignment = Align.Middle,
            VerticalAlignment = Align.Middle,
        };

        foreach (var point in foundPoints)
        {
            var series = point.Context.Series;
            var geometryPoint = (GeometryPoint)point.Context.DataSource!;

            var miniature = (IDrawnElement<MewDrawingContext>)series.GetMiniatureGeometry(point);

            var label = new LabelGeometry
            {
                Text = point.Coordinate.PrimaryValue.ToString("C2"),
                Paint = theme.TooltipTextPaint,
                TextSize = 15,
                Padding = new Padding(8, 0),
                VerticalAlign = Align.Start,
                HorizontalAlign = Align.Start,
            };

            var customContent = new SvgGeometry
            {
                Fill = theme.TooltipTextPaint,
                Width = 30,
                Height = 30,
                SVGPath = geometryPoint.Geometry,
            };

            var row = new StackLayout
            {
                Orientation = ContainerOrientation.Horizontal,
                Padding = new Padding(0, 4),
                VerticalAlignment = Align.Middle,
                HorizontalAlignment = Align.Middle,
                Children = { miniature, label, customContent },
            };

            layout.Children.Add(row);
        }

        return layout;
    }
}

// General/TemplatedLegends: a vertical list of LegendItem rows.
sealed class CustomLegend : MewDefaultLegend
{
    protected override Layout<MewDrawingContext> GetLayout(Chart chart)
    {
        var theme = chart.GetTheme();

        var stackLayout = new StackLayout
        {
            Orientation = ContainerOrientation.Vertical,
            Padding = new Padding(15, 4),
            HorizontalAlignment = Align.Start,
            VerticalAlignment = Align.Middle,
        };

        foreach (var series in chart.Series.Where(candidate => candidate.IsVisibleAtLegend))
            stackLayout.Children.Add(new LegendItem(series, theme.TooltipTextPaint));

        return stackLayout;
    }
}

// General/TemplatedLegends: a 40 px tall series miniature followed by the series name.
sealed class LegendItem : StackLayout
{
    public LegendItem(ISeries series, LiveChartsCore.Painting.Paint? textPaint)
    {
        Orientation = ContainerOrientation.Horizontal;
        Padding = new Padding(12, 6);
        VerticalAlignment = Align.Middle;
        HorizontalAlignment = Align.Middle;
        Opacity = series.IsVisible ? 1 : 0.5f;

        var miniature = (IDrawnElement<MewDrawingContext>)series.GetMiniatureGeometry(null);
        if (miniature is BoundedDrawnGeometry bounded)
            bounded.Height = 40;

        Children =
        [
            miniature,
            new LabelGeometry
            {
                Text = series.Name ?? "?",
                TextSize = 20,
                Paint = textPaint,
                Padding = new Padding(8, 2, 0, 2),
                VerticalAlign = Align.Start,
                HorizontalAlign = Align.Start,
            },
        ];
    }
}

// General/TooltipHoverArea: replaces each column's hover area with a CustomHoverArea of the same bounds.
sealed class CustomColumnSeries<TModel> : ColumnSeries<TModel>
{
    public CustomColumnSeries(params TModel[] values) : base(values) { }

    protected override void OnPointMeasured(ChartPoint point)
    {
        var rectangle = (RoundedRectangleGeometry)point.Context.Visual!;

        var left = GetTargetValue(rectangle, RoundedRectangleGeometry.XProperty);
        var top = GetTargetValue(rectangle, RoundedRectangleGeometry.YProperty);
        var width = GetTargetValue(rectangle, RoundedRectangleGeometry.WidthProperty);
        var height = GetTargetValue(rectangle, RoundedRectangleGeometry.HeightProperty);

        var customHoverArea = new CustomHoverArea
        {
            X = left,
            Y = top,
            Width = width,
            Height = height,
            SuggestedTooltipLocation = new LvcPoint(left + width * 0.5f, top),
        };

        point.Context.HoverArea = customHoverArea;
    }

    private static float GetTargetValue(Animatable animatable, PropertyDefinition propertyDefinition)
    {
        // The target value is where the running animation ends, not the frame currently drawn.
        var motionProperty = (MotionProperty<float>)propertyDefinition.GetMotion(animatable)!;
        return motionProperty.ToValue;
    }
}

// General/TooltipHoverArea: reports a hit only when the pointer is inside the column, whatever the strategy.
sealed class CustomHoverArea : RectangleHoverArea
{
    public override bool IsPointerOver(LvcPoint pointerLocation, FindingStrategy strategy)
    {
        var isInsideXAxis = X <= pointerLocation.X && X + Width >= pointerLocation.X;
        var isInsideYAxis = Y <= pointerLocation.Y && Y + Height >= pointerLocation.Y;

        return isInsideXAxis && isInsideYAxis;
    }
}
