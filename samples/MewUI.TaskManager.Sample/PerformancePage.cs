using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Text;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;

namespace Aprillz.MewUI.TaskManager.Sample;

internal sealed class PerformancePage : UserControl
{
    private const int HISTORY_SECONDS = 60;

    private readonly Dictionary<string, ResourceView> _views = [];
    private readonly List<string> _order = [];
    private readonly StackPanel _cards = new StackPanel().Vertical().Spacing(4);
    private readonly ContentControl _detailHost = new();
    private string _selectedId = "cpu";

    // CPU-only views: the logical processor grid and the kernel time lines.
    private readonly ObservableCollection<ObservablePoint> _kernelHistory = [];
    private readonly List<ObservableCollection<ObservablePoint>> _logicalCpuHistories = [];
    private readonly List<ObservableCollection<ObservablePoint>> _logicalKernelHistories = [];
    private readonly List<TaskManagerChart> _cpuCharts = [];

    public PerformancePage(MonitorController monitor)
    {
        double now = MonotonicSeconds();
        Seed(_kernelHistory, now);
        for (int processor = 0; processor < Math.Max(1, Environment.ProcessorCount); processor++)
        {
            var history = new ObservableCollection<ObservablePoint>();
            var kernelHistory = new ObservableCollection<ObservablePoint>();
            Seed(history, now);
            Seed(kernelHistory, now);
            _logicalCpuHistories.Add(history);
            _logicalKernelHistories.Add(kernelHistory);
        }
        monitor.Updated += Update;
        Build();
    }

    /// <summary>The resource shown now, for the page's copy command.</summary>
    public ResourceSample? SelectedSample => _views.GetValueOrDefault(_selectedId)?.Latest;

    protected override Element OnBuild()
    {
        var copy = new Command("taskmanager.performance.copy", "Copy");
        Commands.Register(copy, CopySelected);
        var more = TaskManagerView.MoreButton(new ContextMenu()
            .Item(copy)
            .Separator()
            .Item(TaskManagerView.OpenSystemMonitorCommand(this)));

        return new Grid()
            .Rows("Auto, *")
            .Children(
                TaskManagerView.PageHeader("Performance", TaskManagerView.RunNewTaskButton(), more),
                new Grid()
                    .Row(1)
                    .Columns("300, *")
                    .Children(
                        new ScrollViewer().AutoVerticalScroll().NoHorizontalScroll().Padding(12).Content(_cards),
                        new ScrollViewer().Column(1).AutoVerticalScroll().NoHorizontalScroll().Content(_detailHost.Margin(24, 22, 28, 24))));
    }

    private void CopySelected()
    {
        if (SelectedSample is not ResourceSample sample) return;
        var text = new StringBuilder();
        text.AppendLine(sample.Title);
        if (sample.Heading.Length > 0) text.AppendLine(sample.Heading);
        text.AppendLine();
        foreach (var metric in sample.Metrics.Concat(sample.Properties)) text.AppendLine($"{metric.Label}\t{metric.Value}");
        Application.Current.PlatformServices.Clipboard?.TrySetText(text.ToString());
    }

    private void Update(IReadOnlyList<ProcessSample> _, PerformanceSample sample)
    {
        double now = MonotonicSeconds();
        Append(_kernelHistory, now, sample.KernelPercent);
        for (int i = 0; i < _logicalCpuHistories.Count; i++)
        {
            Append(_logicalCpuHistories[i], now, i < sample.LogicalProcessorPercents.Count ? sample.LogicalProcessorPercents[i] : 0);
            Append(_logicalKernelHistories[i], now, i < sample.LogicalProcessorKernelPercents.Count ? sample.LogicalProcessorKernelPercents[i] : 0);
        }

        var ids = sample.Resources.Select(resource => resource.Id).ToList();
        bool orderChanged = !ids.SequenceEqual(_order);
        foreach (var resource in sample.Resources)
        {
            if (!_views.TryGetValue(resource.Id, out var view))
            {
                view = new ResourceView(resource, now, this);
                _views[resource.Id] = view;
            }
            view.Update(resource, now);
        }

        if (orderChanged)
        {
            foreach (var gone in _views.Keys.Where(id => !ids.Contains(id)).ToArray()) _views.Remove(gone);
            _order.Clear();
            _order.AddRange(ids);
            _cards.Clear();
            foreach (var id in ids) _cards.Add(_views[id].Card);
            if (!_views.ContainsKey(_selectedId)) _selectedId = ids.FirstOrDefault() ?? "cpu";
            Select(_selectedId);
        }
    }

    private void Select(string id)
    {
        _selectedId = id;
        if (_views.TryGetValue(id, out var view)) _detailHost.Content = view.Detail;
        foreach (var entry in _views.Values) entry.SetSelected(entry.Id == id);
    }

    private FrameworkElement CpuChartArea(ObservableCollection<ObservablePoint> overall, ResourceView view)
    {
        var overallChart = CreateCpuChart(overall, _kernelHistory, compact: false).MinHeight(320);
        FrameworkElement? logicalCharts = null;
        var chartHost = new ContentControl { Content = overallChart };
        var graphMode = new ComboBox()
            .Width(180)
            .Items(["Overall utilization", "Logical processors"])
            .SelectedIndex(0)
            .OnSelectionChanged(value => chartHost.Content = (string?)value == "Logical processors"
                ? logicalCharts ??= LogicalProcessorCharts()
                : overallChart);
        var kernelTimes = new CheckBox()
            .Content("Show kernel times")
            .OnCheckedChanged(value =>
            {
                foreach (var chart in _cpuCharts) chart.SetKernelVisible(value == true);
            });

        return new Grid()
            .Rows("Auto, *")
            .Children(
                new DockPanel().Margin(0, 12, 0, 4).Children(
                    new StackPanel().DockRight().Horizontal().Spacing(12).Children(kernelTimes, graphMode),
                    new TextBlock().BindText(view.ChartLabel).CenterVertical()),
                chartHost.Row(1));
    }

    private FrameworkElement LogicalProcessorCharts()
    {
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(_logicalCpuHistories.Count * 1.6)));
        var grid = new UniformGrid().Columns(columns).Spacing(6);
        for (int i = 0; i < _logicalCpuHistories.Count; i++)
            grid.Add(CreateCpuChart(_logicalCpuHistories[i], _logicalKernelHistories[i], compact: true).MinHeight(56));
        return new Border().MinHeight(320).Child(grid);
    }

    private TaskManagerChart CreateCpuChart(
        ObservableCollection<ObservablePoint> values,
        ObservableCollection<ObservablePoint> kernelValues,
        bool compact)
    {
        var chart = new TaskManagerChart(values, ResourceColors.For(ResourceKind.Cpu), compact, kernelValues: kernelValues);
        _cpuCharts.Add(chart);
        return chart;
    }

    private static void Seed(ObservableCollection<ObservablePoint> values, double now)
    {
        for (int i = 0; i < HISTORY_SECONDS; i++) values.Add(new ObservablePoint(now - (HISTORY_SECONDS - 1 - i), 0));
    }

    private static void Append(ObservableCollection<ObservablePoint> values, double timestamp, double value)
    {
        double cutoff = timestamp - HISTORY_SECONDS;
        // Keep one point immediately before the visible window. The line segment is then clipped at
        // the -60 second boundary instead of starting one refresh interval inside the chart.
        while (values.Count > 1 && values[1].X is double nextX && nextX <= cutoff) values.RemoveAt(0);
        values.Add(new ObservablePoint(timestamp, value));
    }

    private static double MonotonicSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    /// <summary>A resource's card in the list and its detail page, kept in step with its samples.</summary>
    private sealed class ResourceView
    {
        private readonly PerformancePage _page;
        private readonly Color _color;
        private readonly ObservableCollection<ObservablePoint> _primary = [];
        private readonly ObservableCollection<ObservablePoint> _secondary = [];
        private readonly ObservableCollection<ObservablePoint> _secondPrimary = [];
        private readonly ObservableCollection<ObservablePoint> _secondSecondary = [];
        private readonly ObservableValue<string> _title = new(string.Empty);
        private readonly ObservableValue<string> _subtitle = new(string.Empty);
        private readonly ObservableValue<string> _summary = new(string.Empty);
        private readonly ObservableValue<string> _heading = new(string.Empty);
        private readonly ObservableValue<string> _chartMax = new("100%");
        private readonly ObservableValue<string> _secondLabel = new(string.Empty);
        private readonly ObservableValue<string> _secondMax = new(string.Empty);
        private readonly UniformGrid _metrics = new UniformGrid().Columns(3).Spacing(22);
        private readonly Grid _properties = new Grid().Columns("Auto, Auto");
        private readonly List<ObservableValue<string>> _metricValues = [];
        private readonly List<ObservableValue<string>> _propertyValues = [];
        private readonly CompositionBar? _composition;
        private readonly TextBlock? _compositionLegend;
        private readonly TaskManagerChart? _chart;
        private readonly TaskManagerChart? _secondChart;
        private FrameworkElement? _detail;
        private string[] _metricLabels = [];
        private string[] _propertyLabels = [];
        private bool _selected;

        public ResourceView(ResourceSample first, double now, PerformancePage page)
        {
            _page = page;
            Id = first.Id;
            Kind = first.Kind;
            _color = ResourceColors.For(first.Kind);
            Latest = first;
            Seed(_primary, now);
            if (first.Chart.IsRate) Seed(_secondary, now);
            if (first.SecondChart != null)
            {
                Seed(_secondPrimary, now);
                Seed(_secondSecondary, now);
            }

            if (first.Kind != ResourceKind.Cpu)
            {
                _chart = new TaskManagerChart(_primary, _color, compact: false, secondaryValues: first.Chart.IsRate ? _secondary : null);
                if (first.SecondChart != null)
                    _secondChart = new TaskManagerChart(_secondPrimary, _color, compact: false, secondaryValues: _secondSecondary);
            }
            if (first.Composition != null)
            {
                _composition = new CompositionBar(_color);
                _compositionLegend = new TextBlock()
                    .TextWrapping(TextWrapping.Wrap)
                    .WithTheme((theme, text) => text.Foreground(theme.Palette.DisabledText));
            }

            Card = BuildCard();
        }

        public string Id { get; }

        public ResourceKind Kind { get; }

        public ResourceSample Latest { get; private set; }

        public Border Card { get; }

        public ObservableValue<string> ChartLabel { get; } = new(string.Empty);

        public FrameworkElement Detail => _detail ??= BuildDetail();

        public void SetSelected(bool selected)
        {
            _selected = selected;
            if (Application.IsRunning) ApplySelection(Application.Current.Theme, Card);
        }

        public void Update(ResourceSample sample, double now)
        {
            Latest = sample;
            _title.Value = sample.Title;
            _subtitle.Value = sample.Subtitle;
            _summary.Value = sample.Summary;
            _heading.Value = sample.Heading;
            ChartLabel.Value = sample.Chart.Label;

            Append(_primary, now, sample.Chart.Primary);
            if (sample.Chart.IsRate)
            {
                Append(_secondary, now, sample.Chart.Secondary ?? 0);
                _chartMax.Value = Rescale(_chart, _primary, _secondary);
            }
            if (sample.SecondChart is ChartSample second)
            {
                _secondLabel.Value = second.Label;
                Append(_secondPrimary, now, second.Primary);
                Append(_secondSecondary, now, second.Secondary ?? 0);
                _secondMax.Value = Rescale(_secondChart, _secondPrimary, _secondSecondary);
            }

            SyncMetrics(_metrics, ref _metricLabels, _metricValues, sample.Metrics, large: true);
            SyncMetrics(_properties, ref _propertyLabels, _propertyValues, sample.Properties, large: false);

            if (_composition != null && sample.Composition != null)
            {
                _composition.SetParts(sample.Composition, sample.CompositionTotal);
                _compositionLegend!.Text = string.Join("   ", sample.Composition.Select(part => $"{part.Label} {Format.Bytes(part.Bytes)}"));
            }
        }

        /// <summary>Scales a rate chart to the largest value in view and returns the label for its top.</summary>
        private string Rescale(TaskManagerChart? chart, ObservableCollection<ObservablePoint> first, ObservableCollection<ObservablePoint> second)
        {
            double peak = first.Concat(second).Select(point => point.Y ?? 0).DefaultIfEmpty(0).Max();
            // A floor keeps an idle device from scaling noise up to the full height.
            double floor = Kind == ResourceKind.Network ? 100_000 / 8.0 : 1024 * 1024;
            double top = NiceCeiling(Math.Max(peak, floor));
            chart?.SetMaximum(top);
            return Kind == ResourceKind.Network ? Format.BitRate(top) : Format.ByteRate(top);
        }

        private static double NiceCeiling(double value)
        {
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(value)));
            foreach (double step in new[] { 1d, 2, 5, 10 })
            {
                if (step * magnitude >= value) return step * magnitude;
            }
            return 10 * magnitude;
        }

        private static void SyncMetrics(Panel host, ref string[] labels, List<ObservableValue<string>> values, IReadOnlyList<Metric> metrics, bool large)
        {
            var next = metrics.Select(metric => metric.Label).ToArray();
            if (next.SequenceEqual(labels))
            {
                for (int index = 0; index < metrics.Count; index++) values[index].Value = metrics[index].Value;
                return;
            }

            // A label that comes or goes (a GPU engine that starts working) rebuilds the list.
            labels = next;
            values.Clear();
            host.Clear();
            if (host is Grid grid)
            {
                grid.RowDefinitions.Clear();
                for (int row = 0; row < metrics.Count; row++) grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            }

            for (int index = 0; index < metrics.Count; index++)
            {
                var value = new ObservableValue<string>(metrics[index].Value);
                values.Add(value);
                if (large)
                {
                    host.Add(new StackPanel().Vertical().Spacing(2).Children(
                        new TextBlock().Text(metrics[index].Label).WithTheme((theme, text) => text.Foreground(theme.Palette.DisabledText)),
                        new TextBlock().BindText(value).FontSize(ThemeFontSize.Large).LineBoxTrim(LineBoxTrim.CapAndBaseline)));
                }
                else
                {
                    host.Add(new TextBlock().Text(metrics[index].Label + ":").Row(index).Margin(0, 2, 24, 2)
                        .WithTheme((theme, text) => text.Foreground(theme.Palette.DisabledText)));
                    host.Add(new TextBlock().BindText(value).Row(index).Column(1).Margin(0, 2));
                }
            }
        }

        private Border BuildCard()
        {
            var card = new Border()
                .Height(104)
                .Child(
                    new Button()
                        .StyleName("flat-button")
                        .Padding(10)
                        .OnClick(() => _page.Select(Id))
                        .Content(
                            new Grid()
                                .Columns("105, *")
                                .Children(
                                    new TaskManagerChart(_primary, _color, compact: true, secondaryValues: Latest.Chart.IsRate ? _secondary : null)
                                        .Margin(0, 4, 10, 4),
                                    new StackPanel()
                                        .Column(1)
                                        .Vertical()
                                        .CenterVertical()
                                        .Spacing(3)
                                        .Children(
                                            new TextBlock().BindText(_title).FontSize(ThemeFontSize.Medium).SemiBold(),
                                            new TextBlock().BindText(_subtitle).TextTrimming(TextTrimming.CharacterEllipsis)
                                                .WithTheme((theme, text) => text.Foreground(theme.Palette.DisabledText)),
                                            new TextBlock().BindText(_summary)))));
            // WithTheme calls back right away, before the constructor stores Card, so the border is passed in.
            card.WithTheme((theme, border) => ApplySelection(theme, border));
            return card;
        }

        private void ApplySelection(Theme theme, Border card) => card
            .BorderBrush(_selected ? theme.Palette.ControlBorder : Color.Transparent)
            .Background(_selected ? theme.Palette.SelectionBackground.WithAlpha(90) : Color.Transparent);

        private FrameworkElement BuildDetail()
        {
            var rows = new StackPanel().Vertical();
            rows.Add(new DockPanel().Children(
                new TextBlock().BindText(_heading).DockRight().FontSize(ThemeFontSize.Medium).CenterVertical().TextTrimming(TextTrimming.CharacterEllipsis),
                new TextBlock().BindText(_title).FontSize(40).LineBoxTrim(LineBoxTrim.CapAndBaseline)));

            if (Kind == ResourceKind.Cpu)
            {
                rows.Add(_page.CpuChartArea(_primary, this));
            }
            else
            {
                rows.Add(new DockPanel().Margin(0, 12, 0, 4).Children(
                    new TextBlock().DockRight().BindText(_chartMax),
                    new TextBlock().BindText(ChartLabel)));
                rows.Add(_chart!.MinHeight(_secondChart != null ? 220 : 320));
            }
            rows.Add(TimeAxis());
            if (Latest.Chart.IsRate) rows.Add(Legend(Latest.Chart));

            if (_secondChart != null)
            {
                rows.Add(new DockPanel().Margin(0, 16, 0, 4).Children(
                    new TextBlock().DockRight().BindText(_secondMax),
                    new TextBlock().BindText(_secondLabel)));
                rows.Add(_secondChart.MinHeight(110));
                rows.Add(TimeAxis());
                if (Latest.SecondChart is ChartSample second) rows.Add(Legend(second));
            }

            if (_composition != null)
            {
                rows.Add(new TextBlock().Text("Memory composition").Margin(0, 16, 0, 4));
                rows.Add(_composition.Height(40));
                rows.Add(_compositionLegend!.Margin(0, 4, 0, 0));
            }

            rows.Add(new Grid()
                .Columns("*, Auto")
                .Margin(0, 18, 0, 0)
                .Children(
                    _metrics,
                    _properties.Column(1).Margin(24, 0, 0, 0)));
            return rows;
        }

        private static FrameworkElement TimeAxis() => new DockPanel().Margin(0, 4, 0, 0).Children(
            new TextBlock().DockRight().Text("0"),
            new TextBlock().Text("60 seconds"));

        private FrameworkElement Legend(ChartSample chart) => new StackPanel().Horizontal().Spacing(18).Margin(0, 6, 0, 0).Children(
            LegendEntry(chart.PrimaryName ?? string.Empty, dashed: false),
            LegendEntry(chart.SecondaryName ?? string.Empty, dashed: true));

        private FrameworkElement LegendEntry(string label, bool dashed) => new StackPanel().Horizontal().Spacing(6).Children(
            new LegendLine(_color, dashed).Width(22).Height(12).CenterVertical(),
            new TextBlock().Text(label).CenterVertical());
    }
}

internal static class ResourceColors
{
    public static Color For(ResourceKind kind) => kind switch
    {
        ResourceKind.Cpu => Color.FromRgb(17, 125, 153),
        ResourceKind.Memory => Color.FromRgb(139, 18, 174),
        ResourceKind.Disk => Color.FromRgb(77, 166, 12),
        ResourceKind.Network => Color.FromRgb(167, 79, 1),
        _ => Color.FromRgb(172, 57, 49),
    };
}

/// <summary>A horizontal bar split into the shares of a whole, each shaded by its own alpha.</summary>
internal sealed class CompositionBar : FrameworkElement
{
    private readonly Color _color;
    private IReadOnlyList<CompositionPart> _parts = [];
    private long _total = 1;

    public CompositionBar(Color color) => _color = color;

    public void SetParts(IReadOnlyList<CompositionPart> parts, long total)
    {
        _parts = parts;
        _total = Math.Max(1, total);
        this.ToolTip(string.Join(Environment.NewLine, parts.Select(part => $"{part.Label}: {Format.Bytes(part.Bytes)}")));
        InvalidateVisual();
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        double x = bounds.X;
        for (int index = 0; index < _parts.Count; index++)
        {
            double width = bounds.Width * Math.Max(0, _parts[index].Bytes) / _total;
            if (width <= 0) continue;
            var segment = new Rect(x, bounds.Y, Math.Min(width, bounds.Right - x), bounds.Height);
            if (_parts[index].Alpha > 0) context.FillRectangle(segment, _color.WithAlpha(_parts[index].Alpha));
            x += width;
            if (index < _parts.Count - 1 && x < bounds.Right) context.DrawLine(new Point(x, bounds.Y), new Point(x, bounds.Bottom), _color, 1);
        }
        context.DrawRectangle(bounds, _color, 1);
    }
}

/// <summary>A short line in a series' color, solid or dashed, for a chart legend.</summary>
internal sealed class LegendLine : FrameworkElement
{
    private readonly Color _color;
    private readonly bool _dashed;

    public LegendLine(Color color, bool dashed)
    {
        _color = color;
        _dashed = dashed;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        var bounds = Bounds;
        double y = bounds.Y + bounds.Height / 2;
        if (!_dashed)
        {
            context.DrawLine(new Point(bounds.X, y), new Point(bounds.Right, y), _color, 2);
            return;
        }

        for (double x = bounds.X; x < bounds.Right; x += 6)
            context.DrawLine(new Point(x, y), new Point(Math.Min(x + 3, bounds.Right), y), _color, 2);
    }
}

internal sealed class TaskManagerChart : CartesianChart
{
    private readonly ObservableCollection<ObservablePoint> _values;
    private readonly Color _accent;
    private readonly bool _compact;
    private readonly LineSeries<ObservablePoint> _series;
    private readonly LineSeries<ObservablePoint>? _kernelSeries;
    private readonly LineSeries<ObservablePoint>? _secondarySeries;
    private readonly Axis _xAxis;
    private readonly Axis _yAxis;

    public TaskManagerChart(
        ObservableCollection<ObservablePoint> values,
        Color accent,
        bool compact,
        ObservableCollection<ObservablePoint>? kernelValues = null,
        ObservableCollection<ObservablePoint>? secondaryValues = null)
    {
        _values = values;
        _accent = accent;
        _compact = compact;
        _series = new LineSeries<ObservablePoint>(_values)
        {
            GeometrySize = 0,
            LineSmoothness = 0,
        };
        if (kernelValues != null)
        {
            _kernelSeries = new LineSeries<ObservablePoint>(kernelValues)
            {
                GeometrySize = 0,
                LineSmoothness = 0,
                Fill = null,
                IsVisible = false,
            };
            kernelValues.CollectionChanged += OnValuesChanged;
        }
        if (secondaryValues != null)
        {
            _secondarySeries = new LineSeries<ObservablePoint>(secondaryValues)
            {
                GeometrySize = 0,
                LineSmoothness = 0,
                Fill = null,
            };
            secondaryValues.CollectionChanged += OnValuesChanged;
        }
        double rightEdge = _values.LastOrDefault()?.X ?? Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        _xAxis = new Axis
        {
            MinLimit = rightEdge - 60,
            MaxLimit = rightEdge,
            MinStep = compact ? 15 : 10,
            ForceStepToMin = true,
            LabelsPaint = null,
            TicksPaint = null,
        };
        _yAxis = new Axis
        {
            MinLimit = 0,
            MaxLimit = 100,
            MinStep = 20,
            ForceStepToMin = true,
            LabelsPaint = null,
            TicksPaint = null,
        };

        var series = new List<LiveChartsCore.ISeries> { _series };
        if (_kernelSeries != null) series.Add(_kernelSeries);
        if (_secondarySeries != null) series.Add(_secondarySeries);
        Series = series;
        XAxes = [_xAxis];
        YAxes = [_yAxis];
        TooltipPosition = TooltipPosition.Hidden;
        LegendPosition = LegendPosition.Hidden;
        CornerRadius = 0;
        AnimationsSpeed = TimeSpan.Zero;
        UpdaterThrottler = TimeSpan.Zero;
        _values.CollectionChanged += OnValuesChanged;
        ApplyTheme(Theme);
    }

    protected override void OnThemeChanged(Theme oldTheme, Theme newTheme)
    {
        base.OnThemeChanged(oldTheme, newTheme);
        ApplyTheme(newTheme);
    }

    private void ApplyTheme(Theme theme)
    {
        float onePixel = (float)(96d / Math.Max(1u, GetDpi()));
        var grid = theme.IsDark ? Color.FromRgb(67, 70, 73) : Color.FromRgb(224, 226, 228);
        var border = Color.FromRgb(118, 121, 124);
        _series.Stroke = new SolidColorPaint(_accent, _compact ? 1 : 1.5f);
        _series.Fill = new SolidColorPaint(_accent.WithAlpha(theme.IsDark ? (byte)45 : (byte)38));
        _series.GeometryFill = null;
        _series.GeometryStroke = null;
        if (_kernelSeries != null)
        {
            _kernelSeries.Stroke = new SolidColorPaint(Color.FromRgb(210, 45, 45), onePixel);
            _kernelSeries.Fill = null;
            _kernelSeries.GeometryFill = null;
            _kernelSeries.GeometryStroke = null;
        }
        if (_secondarySeries != null)
        {
            _secondarySeries.Stroke = new SolidColorPaint(_accent, _compact ? 1 : 1.5f) { DashArray = [3, 2] };
            _secondarySeries.Fill = null;
            _secondarySeries.GeometryFill = null;
            _secondarySeries.GeometryStroke = null;
        }
        _xAxis.SeparatorsPaint = new SolidColorPaint(grid, onePixel) { PixelSnap = true };
        _yAxis.SeparatorsPaint = new SolidColorPaint(grid, onePixel) { PixelSnap = true };
        DrawMarginFrame = new DrawMarginFrame
        {
            PixelSnap = true,
            Stroke = new SolidColorPaint(border, onePixel),
        };
        Background = theme.Palette.WindowBackground;
        CoreChart?.Update();
    }

    public void SetKernelVisible(bool visible)
    {
        if (_kernelSeries == null) return;
        _kernelSeries.IsVisible = visible;
        CoreChart?.Update();
        InvalidateVisual();
    }

    /// <summary>Sets the value at the top of the chart; a rate chart follows the largest value in view.</summary>
    public void SetMaximum(double maximum)
    {
        if (_yAxis.MaxLimit == maximum) return;
        _yAxis.MaxLimit = maximum;
        _yAxis.MinStep = maximum / 5;
        CoreChart?.Update();
        InvalidateVisual();
    }

    protected override void OnDpiChanged(uint oldDpi, uint newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        ApplyTheme(Theme);
    }

    private void OnValuesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_values.LastOrDefault()?.X is not double rightEdge) return;
        _xAxis.MinLimit = rightEdge - 60;
        _xAxis.MaxLimit = rightEdge;
        CoreChart.Update();
        InvalidateVisual();
    }

    protected override void OnDispose()
    {
        _values.CollectionChanged -= OnValuesChanged;
        if (_kernelSeries?.Values is INotifyCollectionChanged kernelValues)
            kernelValues.CollectionChanged -= OnValuesChanged;
        if (_secondarySeries?.Values is INotifyCollectionChanged secondaryValues)
            secondaryValues.CollectionChanged -= OnValuesChanged;
        base.OnDispose();
    }
}
