using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts.Drawing;
using Aprillz.MewUI.Rendering;

using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Kernel.Events;
using LiveChartsCore.Kernel.Observers;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.Motion;
using LiveChartsCore.Painting;
using LiveChartsCore.VisualElements;

namespace Aprillz.MewUI.MewCharts.Views;

/// <summary>
/// Base MewUI chart control. Hosts a <see cref="CoreMotionCanvas"/> and renders it through a
/// <see cref="MewDrawingContext"/> each frame. Implements the LiveCharts <see cref="IChartView"/>
/// surface; the SkiaSharp view shared code, ported to MewUI.
/// </summary>
public abstract class ChartViewBase : Control, IChartView
{
    private ChartObserver? _observer;
    private bool _loaded;
    private double _lastRenderWidth = -1;
    private double _lastRenderHeight = -1;

    static ChartViewBase() => LiveChartsMewUI.EnsureInitialized();

    protected ChartViewBase() => InitializeChartControl();

    /// <inheritdoc cref="IDrawnView.CoreCanvas"/>
    public CoreMotionCanvas CoreCanvas { get; } = new();

    /// <inheritdoc cref="IChartView.CoreChart"/>
    public Chart CoreChart { get; private set; } = null!;

    // Background / FontFamily / FontSize / Foreground are inherited from Control (MewProperty,
    // so they participate in MewUI styling and value inheritance from the parent).

    // ----- IDrawnView -----
    LvcSize IDrawnView.ControlSize => new((float)ActualWidth, (float)ActualHeight);

    // ----- IChartView basics -----
    bool IChartView.DesignerMode => false;

    bool IChartView.IsDarkMode => false;

    LvcColor IChartView.BackColor => new(Background.A, Background.R, Background.G, Background.B);

    // Bindable view state (DependencyProperty-equivalent MewProperties), matching the WPF/Avalonia
    // view surface so these can be bound/styled like the rest of the chart properties.
    public static readonly MewProperty<LiveChartsCore.Themes.Theme?> ChartThemeProperty =
        MewProperty<LiveChartsCore.Themes.Theme?>.Register<ChartViewBase>(nameof(ChartTheme), null,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public LiveChartsCore.Themes.Theme? ChartTheme { get => GetValue(ChartThemeProperty); set => SetValue(ChartThemeProperty, value); }

    public static readonly MewProperty<IChartTooltip?> TooltipProperty =
        MewProperty<IChartTooltip?>.Register<ChartViewBase>(nameof(Tooltip), null);

    public IChartTooltip? Tooltip { get => GetValue(TooltipProperty); set => SetValue(TooltipProperty, value); }

    public static readonly MewProperty<IChartLegend?> LegendProperty =
        MewProperty<IChartLegend?>.Register<ChartViewBase>(nameof(Legend), null,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public IChartLegend? Legend { get => GetValue(LegendProperty); set => SetValue(LegendProperty, value); }

    public static readonly MewProperty<TimeSpan> UpdaterThrottlerProperty =
        MewProperty<TimeSpan>.Register<ChartViewBase>(nameof(UpdaterThrottler), LiveCharts.DefaultSettings.UpdateThrottlingTimeout);

    public TimeSpan UpdaterThrottler { get => GetValue(UpdaterThrottlerProperty); set => SetValue(UpdaterThrottlerProperty, value); }

    public static readonly MewProperty<bool> AutoUpdateEnabledProperty =
        MewProperty<bool>.Register<ChartViewBase>(nameof(AutoUpdateEnabled), true);

    public bool AutoUpdateEnabled { get => GetValue(AutoUpdateEnabledProperty); set => SetValue(AutoUpdateEnabledProperty, value); }

    // SyncContext stays a plain property: it needs a per-instance default lock object, which a
    // shared MewProperty default value cannot provide.
    public object SyncContext { get => field ??= new(); set { field = value; CoreCanvas.Sync = value; CoreChart?.Update(); } }

    // Bindable MewProperty surface (the LiveCharts UIProperty equivalents).
    public static readonly MewProperty<IEnumerable<ISeries>> SeriesProperty =
        MewProperty<IEnumerable<ISeries>>.Register<ChartViewBase>(nameof(Series), Array.Empty<ISeries>(),
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(Series), value, isSeries: true));

    public IEnumerable<ISeries> Series { get => GetValue(SeriesProperty); set => SetValue(SeriesProperty, value); }

    public static readonly MewProperty<IEnumerable<IChartElement>> VisualElementsProperty =
        MewProperty<IEnumerable<IChartElement>>.Register<ChartViewBase>(nameof(VisualElements), Array.Empty<IChartElement>(),
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(VisualElements), value));

    public IEnumerable<IChartElement> VisualElements { get => GetValue(VisualElementsProperty); set => SetValue(VisualElementsProperty, value); }

    public static readonly MewProperty<IChartElement?> TitleProperty =
        MewProperty<IChartElement?>.Register<ChartViewBase>(nameof(Title), null,
            changed: (owner, _, value) => owner.OnObservedChanged(nameof(Title), value));

    public IChartElement? Title { get => GetValue(TitleProperty); set => SetValue(TitleProperty, value); }

    public static readonly MewProperty<Margin?> DrawMarginProperty =
        MewProperty<Margin?>.Register<ChartViewBase>(nameof(DrawMargin), null,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public Margin? DrawMargin { get => GetValue(DrawMarginProperty); set => SetValue(DrawMarginProperty, value); }

    public static readonly MewProperty<LegendPosition> LegendPositionProperty =
        MewProperty<LegendPosition>.Register<ChartViewBase>(nameof(LegendPosition), LiveCharts.DefaultSettings.LegendPosition,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public LegendPosition LegendPosition { get => GetValue(LegendPositionProperty); set => SetValue(LegendPositionProperty, value); }

    public static readonly MewProperty<TooltipPosition> TooltipPositionProperty =
        MewProperty<TooltipPosition>.Register<ChartViewBase>(nameof(TooltipPosition), LiveCharts.DefaultSettings.TooltipPosition,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public TooltipPosition TooltipPosition { get => GetValue(TooltipPositionProperty); set => SetValue(TooltipPositionProperty, value); }

    public static readonly MewProperty<Paint?> LegendTextPaintProperty =
        MewProperty<Paint?>.Register<ChartViewBase>(nameof(LegendTextPaint), LiveCharts.DefaultSettings.LegendTextPaint,
            changed: OnTextPaintChanged);

    public Paint? LegendTextPaint { get => GetValue(LegendTextPaintProperty); set => SetValue(LegendTextPaintProperty, value); }

    public static readonly MewProperty<Paint?> LegendBackgroundPaintProperty =
        MewProperty<Paint?>.Register<ChartViewBase>(nameof(LegendBackgroundPaint), LiveCharts.DefaultSettings.LegendBackgroundPaint,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public Paint? LegendBackgroundPaint { get => GetValue(LegendBackgroundPaintProperty); set => SetValue(LegendBackgroundPaintProperty, value); }

    public static readonly MewProperty<double> LegendTextSizeProperty =
        MewProperty<double>.Register<ChartViewBase>(nameof(LegendTextSize), LiveCharts.DefaultSettings.LegendTextSize,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public double LegendTextSize { get => GetValue(LegendTextSizeProperty); set => SetValue(LegendTextSizeProperty, value); }

    public static readonly MewProperty<Paint?> TooltipTextPaintProperty =
        MewProperty<Paint?>.Register<ChartViewBase>(nameof(TooltipTextPaint), LiveCharts.DefaultSettings.TooltipTextPaint,
            changed: OnTextPaintChanged);

    public Paint? TooltipTextPaint { get => GetValue(TooltipTextPaintProperty); set => SetValue(TooltipTextPaintProperty, value); }

    public static readonly MewProperty<Paint?> TooltipBackgroundPaintProperty =
        MewProperty<Paint?>.Register<ChartViewBase>(nameof(TooltipBackgroundPaint), LiveCharts.DefaultSettings.TooltipBackgroundPaint,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public Paint? TooltipBackgroundPaint { get => GetValue(TooltipBackgroundPaintProperty); set => SetValue(TooltipBackgroundPaintProperty, value); }

    public static readonly MewProperty<double> TooltipTextSizeProperty =
        MewProperty<double>.Register<ChartViewBase>(nameof(TooltipTextSize), LiveCharts.DefaultSettings.TooltipTextSize,
            changed: (owner, _, _) => owner.CoreChart?.Update());

    public double TooltipTextSize { get => GetValue(TooltipTextSizeProperty); set => SetValue(TooltipTextSizeProperty, value); }

    public static readonly MewProperty<TimeSpan> AnimationsSpeedProperty =
        MewProperty<TimeSpan>.Register<ChartViewBase>(nameof(AnimationsSpeed), LiveCharts.DefaultSettings.AnimationsSpeed);

    public TimeSpan AnimationsSpeed { get => GetValue(AnimationsSpeedProperty); set => SetValue(AnimationsSpeedProperty, value); }

    public static readonly MewProperty<Func<float, float>?> EasingFunctionProperty =
        MewProperty<Func<float, float>?>.Register<ChartViewBase>(nameof(EasingFunction), LiveCharts.DefaultSettings.EasingFunction);

    public Func<float, float>? EasingFunction { get => GetValue(EasingFunctionProperty); set => SetValue(EasingFunctionProperty, value); }

    private static void OnTextPaintChanged(ChartViewBase owner, Paint? oldValue, Paint? newValue)
    {
        if (newValue is not null) newValue.PaintStyle = PaintStyle.Text;
        owner.CoreChart?.Update();
    }

    // ----- events -----
    public event ChartEventHandler? Measuring;

    public event ChartEventHandler? UpdateStarted;

    public event ChartEventHandler? UpdateFinished;

    public event ChartPointsHandler? DataPointerDown;

    public event ChartPointHoverHandler? HoveredPointsChanged;

    [Obsolete("Use DataPointerDown.")]
    public event ChartPointHandler? ChartPointPointerDown;

    public event VisualElementsHandler? VisualElementsPointerDown;

    protected abstract Chart CreateCoreChart();

    private void InitializeChartControl()
    {
        CoreChart = CreateCoreChart();
        CoreChart.Measuring += _ => Measuring?.Invoke(this);
        CoreChart.UpdateStarted += _ => UpdateStarted?.Invoke(this);
        CoreChart.UpdateFinished += _ => UpdateFinished?.Invoke(this);

        _observer = new ChartObserver(ConfigureObserver, () => CoreChart?.Update());
    }

    protected virtual ChartObserver ConfigureObserver(ChartObserver observe) =>
        observe
            .Collection(nameof(Series), () => Series)
            .Collection(nameof(VisualElements), () => VisualElements)
            .Property(nameof(Title), () => Title!);

    protected void OnObservedChanged(string propertyName, object? value, bool isSeries = false)
    {
        if (_observer is null || !_loaded) return;
        _observer[propertyName].Observer.Initialize(value!);
        CoreChart?.Update();
        if (isSeries) CoreChart?.ResetNextSeriesId();
    }

    // ----- IChartView methods -----
    public IEnumerable<ChartPoint> GetPointsAt(
        LvcPointD point, FindingStrategy strategy = FindingStrategy.Automatic, FindPointFor findPointFor = FindPointFor.HoverEvent)
    {
        if (strategy == FindingStrategy.Automatic) strategy = CoreChart.Series.GetFindingStrategy();
        return CoreChart.Series.SelectMany(series => series.FindHitPoints(CoreChart, new(point), strategy, findPointFor));
    }

    public IEnumerable<IChartElement> GetVisualsAt(LvcPointD point) =>
        CoreChart.VisualElements.SelectMany(visual => ((VisualElement)visual).IsHitBy(CoreChart, new(point)));

    void IChartView.OnDataPointerDown(IEnumerable<ChartPoint> points, LvcPoint pointer)
    {
        DataPointerDown?.Invoke(this, points);
#pragma warning disable CS0618
        ChartPointPointerDown?.Invoke(this, points.FindClosestTo(pointer));
#pragma warning restore CS0618
    }

    void IChartView.OnHoveredPointsChanged(IEnumerable<ChartPoint>? newItems, IEnumerable<ChartPoint>? oldItems) =>
        HoveredPointsChanged?.Invoke(this, newItems, oldItems);

    void IChartView.OnVisualElementPointerDown(IEnumerable<IInteractable> visualElements, LvcPoint pointer) =>
        VisualElementsPointerDown?.Invoke(this, new VisualElementsEventArgs(CoreChart, visualElements, pointer));

    void IChartView.InvokeOnUIThread(Action action)
    {
        if (Application.IsRunning && Application.Current.Dispatcher is { } dispatcher)
            dispatcher.BeginInvoke(action);
        else
            action();
    }

    void IChartView.Invalidate() => CoreCanvas.Invalidate();

    // ----- MewUI element integration -----
    // No intrinsic size: the chart fills whatever the parent arranges (Stretch), and explicit
    // Width/Height still apply. Matches the SkiaCanvasView idiom.
    protected override Size MeasureContent(Size availableSize) => Size.Empty;

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        if (_loaded) CoreChart?.Update();
    }

    /// <summary>
    /// Pointer position in the chart's coordinate space. Both <see cref="MouseEventArgs.GetPosition"/>
    /// and the chart's <c>ControlSize</c> are in MewUI's DIP coordinates, so they map 1:1.
    /// </summary>
    protected LvcPoint ToChartPoint(MouseEventArgs e)
    {
        var point = e.GetPosition(this);
        return new LvcPoint((float)point.X, (float)point.Y);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        CoreChart?.InvokePointerMove(ToChartPoint(e));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        CoreChart?.InvokePointerDown(ToChartPoint(e), e.RightButton);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        CoreChart?.InvokePointerUp(ToChartPoint(e), e.RightButton);
    }

    protected override void OnMouseLeave()
    {
        base.OnMouseLeave();
        CoreChart?.InvokePointerLeft();
    }

    protected override void OnVisualRootChanged(Element? oldRoot, Element? newRoot)
    {
        base.OnVisualRootChanged(oldRoot, newRoot);

        if (newRoot is Window && !_loaded)
        {
            MewChartsText.EnsureInitialized(GetGraphicsFactory());
            CoreCanvas.Invalidated += OnCanvasInvalidated;
            _loaded = true;
            _observer?.Initialize();
            CoreChart.Load();
            CoreChart.Update();
        }
        else if (newRoot is null && _loaded)
        {
            CoreCanvas.Invalidated -= OnCanvasInvalidated;
            _observer?.Dispose();
            CoreChart.Unload();
            _loaded = false;
        }
    }

    private void OnCanvasInvalidated(CoreMotionCanvas canvas)
    {
        if (Application.IsRunning && Application.Current.Dispatcher is { } dispatcher && !dispatcher.IsOnUIThread)
            dispatcher.BeginInvoke(InvalidateVisual);
        else
            InvalidateVisual();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);

        // Chart text follows MewUI's inherited font: family directly, and the (inherited) FontSize
        // scales the theme's per-role sizes. The theme is authored against a 16-px reference (axis
        // labels), so FontSize/16 makes the primary text render at the requested FontSize.
        MewChartsText.FontFamily = FontFamily;
        MewChartsText.FontScale = FontSize > 0 ? FontSize / 16.0 : 1;

        // The render context is a shared (window) coordinate space, so offset to this element's
        // origin and clip to its bounds before letting the chart draw in local (0,0)-based coords.
        context.Save();
        context.Translate(Bounds.X, Bounds.Y);
        var localBounds = new Rect(0, 0, Bounds.Width, Bounds.Height);
        //context.IntersectClip(localBounds);

        if (Background.A > 0) context.FillRectangle(localBounds, Background);

        // Off-screen elements in a ScrollViewer may be resized without OnSizeChanged firing; when
        // they scroll back into view, re-measure if the size changed since the last frame so the
        // chart layout matches the current bounds instead of a stale size.
        if (_loaded && (Bounds.Width != _lastRenderWidth || Bounds.Height != _lastRenderHeight))
        {
            _lastRenderWidth = Bounds.Width;
            _lastRenderHeight = Bounds.Height;
            CoreChart?.Update();
        }

        var drawingContext = new MewDrawingContext(CoreCanvas, context, Background, GetGraphicsFactory())
        {
            DrawArea = localBounds,
        };
        CoreCanvas.DrawFrame(drawingContext);

        context.Restore();
        if (!CoreCanvas.IsValid) InvalidateVisual();
    }

    protected override void OnDispose()
    {
        CoreCanvas.Invalidated -= OnCanvasInvalidated;
        base.OnDispose();
    }
}
