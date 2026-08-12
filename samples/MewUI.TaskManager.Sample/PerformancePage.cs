using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewCharts;
using Aprillz.MewUI.MewCharts.Painting;
using Aprillz.MewUI.MewCharts.Views;
using Aprillz.MewUI.Text;

using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;

namespace Aprillz.MewUI.TaskManager.Sample;

internal sealed class PerformancePage : UserControl
{
    private static readonly Color CpuColor = Color.FromRgb(17, 125, 153);
    private static readonly Color MemoryColor = Color.FromRgb(91, 93, 214);

    private readonly ObservableCollection<ObservablePoint> _cpuHistory = [];
    private readonly ObservableCollection<ObservablePoint> _kernelHistory = [];
    private readonly ObservableCollection<ObservablePoint> _memoryHistory = [];
    private readonly ObservableCollection<ObservablePoint> _diskHistory = [];
    private readonly ObservableCollection<ObservablePoint> _networkHistory = [];
    private readonly List<ObservableCollection<ObservablePoint>> _logicalCpuHistories = [];
    private readonly List<ObservableCollection<ObservablePoint>> _logicalKernelHistories = [];
    private readonly List<TaskManagerChart> _cpuCharts = [];
    private readonly ObservableValue<string> _cpuValue = new("0%");
    private readonly ObservableValue<string> _memoryValue = new("0 / 0 GB (0%)");
    private readonly ObservableValue<string> _speedValue = new("Not available");
    private readonly ObservableValue<string> _processValue = new("0");
    private readonly ObservableValue<string> _threadValue = new("0");
    private readonly ObservableValue<string> _logicalProcessorValue = new(Environment.ProcessorCount.ToString());
    private readonly ObservableValue<string> _uptimeValue = new("0:00:00:00");
    private readonly ObservableValue<string> _memoryUsedValue = new("0 GB");
    private readonly ObservableValue<string> _memoryAvailableValue = new("0 GB");
    private readonly ObservableValue<string> _memoryTotalValue = new("0 GB");
    private readonly ObservableValue<string> _notAvailableValue = new("Not available");

    public PerformancePage(MonitorController monitor)
    {
        double now = MonotonicSeconds();
        for (int i = 0; i < 60; i++)
        {
            double timestamp = now - (59 - i);
            _cpuHistory.Add(new ObservablePoint(timestamp, 0));
            _kernelHistory.Add(new ObservablePoint(timestamp, 0));
            _memoryHistory.Add(new ObservablePoint(timestamp, 0));
            _diskHistory.Add(new ObservablePoint(timestamp, 0));
            _networkHistory.Add(new ObservablePoint(timestamp, 0));
        }
        for (int processor = 0; processor < Math.Max(1, Environment.ProcessorCount); processor++)
        {
            var history = new ObservableCollection<ObservablePoint>();
            var kernelHistory = new ObservableCollection<ObservablePoint>();
            for (int i = 0; i < 60; i++) history.Add(new ObservablePoint(now - (59 - i), 0));
            for (int i = 0; i < 60; i++) kernelHistory.Add(new ObservablePoint(now - (59 - i), 0));
            _logicalCpuHistories.Add(history);
            _logicalKernelHistories.Add(kernelHistory);
        }
        monitor.Updated += Update;
        Build();
    }

    protected override Element OnBuild()
    {
        var detailHost = new ContentControl();
        var selected = PerformanceResource.Cpu;
        var cards = new List<(PerformanceResource Resource, Border Card)>();
        var details = new Dictionary<PerformanceResource, FrameworkElement>
        {
            [PerformanceResource.Cpu] = CpuDetail(
                Metric("Utilization", _cpuValue),
                Metric("Speed", _speedValue),
                Metric("Processes", _processValue),
                Metric("Threads", _threadValue),
                Metric("Logical processors", _logicalProcessorValue),
                Metric("Up time", _uptimeValue)),
            [PerformanceResource.Memory] = Detail(
                "Memory",
                _memoryTotalValue,
                "Memory usage",
                _memoryTotalValue,
                _memoryHistory,
                MemoryColor,
                Metric("In use", _memoryUsedValue),
                Metric("Available", _memoryAvailableValue),
                Metric("Total", _memoryTotalValue)),
            [PerformanceResource.Disk] = Detail(
                "Disk 0",
                "Not available",
                "Active time",
                "100%",
                _diskHistory,
                Color.FromRgb(78, 143, 37),
                Metric("Status", _notAvailableValue)),
            [PerformanceResource.Network] = Detail(
                "Network",
                "Not available",
                "Throughput",
                "100%",
                _networkHistory,
                Color.FromRgb(161, 63, 201),
                Metric("Status", _notAvailableValue)),
        };

        void Select(PerformanceResource resource)
        {
            selected = resource;
            detailHost.Content = details[resource];
            if (!Application.IsRunning) return;
            foreach (var entry in cards) ApplySelection(entry.Card, entry.Resource == selected, Application.Current.Theme);
        }

        Border Card(
            PerformanceResource resource,
            string title,
            ObservableValue<string> value,
            ObservableCollection<ObservablePoint> history,
            Color color)
        {
            var card = ResourceCard(title, value, history, color, () => Select(resource));
            card.WithTheme((theme, border) => ApplySelection(border, resource == selected, theme));
            cards.Add((resource, card));
            return card;
        }

        var resources = new StackPanel()
            .Vertical()
            .Spacing(4)
            .Children(
                Card(PerformanceResource.Cpu, "CPU", _cpuValue, _cpuHistory, CpuColor),
                Card(PerformanceResource.Memory, "Memory", _memoryValue, _memoryHistory, MemoryColor),
                Card(PerformanceResource.Disk, "Disk 0", _notAvailableValue, _diskHistory, Color.FromRgb(78, 143, 37)),
                Card(PerformanceResource.Network, "Network", _notAvailableValue, _networkHistory, Color.FromRgb(161, 63, 201)));

        detailHost.Content = details[selected];

        return new Grid()
            .Rows("Auto, *")
            .Children(
                new Border()
                    .Padding(28, 22, 28, 18)
                    .BorderThickness(new Thickness(0, 0, 0, 1))
                    .Child(new TextBlock().Text("Performance").FontSize(ThemeFontSize.Medium).SemiBold()),
                new Grid()
                    .Row(1)
                    .Columns("300, *")
                    .Children(
                        new ScrollViewer().AutoVerticalScroll().Padding(12).Content(resources),
                        detailHost.Column(1).Margin(24, 22, 28, 24)));
    }

    private static Border ResourceCard(
        string title,
        ObservableValue<string> value,
        ObservableCollection<ObservablePoint> history,
        Color color,
        Action select) =>
        new Border()
            .Height(104)
            .BorderThickness(1)
            .Child(
                new Button()
                    .StyleName("flat-button")
                    .Padding(10)
                    .OnClick(select)
                    .Content(
                        new Grid()
                            .Columns("105, *")
                            .Children(
                                new TaskManagerChart(history, color, compact: true).Margin(0, 4, 10, 4),
                                new StackPanel()
                                    .Column(1)
                                    .Vertical()
                                    .CenterVertical()
                                    .Spacing(3)
                                    .Children(
                                        new TextBlock().Text(title).FontSize(ThemeFontSize.Medium).SemiBold(),
                                        new TextBlock().BindText(value)))));

    private static void ApplySelection(Border card, bool selected, Theme theme) => card
        .BorderBrush(selected ? theme.Palette.ControlBorder : Color.Transparent)
        .Background(selected ? theme.Palette.SelectionBackground.WithAlpha(90) : Color.Transparent);

    private FrameworkElement CpuDetail(params FrameworkElement[] metrics)
    {
        var overallChart = CpuChartFrame(_cpuHistory, _kernelHistory, compact: false);
        var logicalCharts = LogicalProcessorCharts();
        var chartHost = new ContentControl { Content = overallChart };
        var graphMode = new ComboBox()
            .Width(180)
            .Items(["Overall utilization", "Logical processors"])
            .SelectedIndex(0)
            .OnSelectionChanged(value => chartHost.Content = (string?)value == "Logical processors"
                ? logicalCharts
                : overallChart);
        var kernelTimes = new CheckBox()
            .Content("Show kernel times")
            .OnCheckedChanged(value =>
            {
                foreach (var chart in _cpuCharts) chart.SetKernelVisible(value == true);
            });

        return new Grid()
            .Rows("Auto, Auto, *, Auto")
            .Children(
                new DockPanel().Children(
                    new StackPanel().DockRight().Horizontal().Spacing(12).CenterVertical().Children(
                        kernelTimes,
                        graphMode,
                        new TextBlock().Text(Environment.MachineName).FontSize(ThemeFontSize.Medium).CenterVertical()),
                    new TextBlock().Text("CPU").FontSize(40).LineBoxTrim(LineBoxTrim.CapAndBaseline)),
                new DockPanel().Row(1).Margin(0, 12, 0, 4).Children(
                    new TextBlock().DockRight().Text("100%"),
                    new TextBlock().Text("% Utilization")),
                chartHost.Row(2),
                new Grid().Row(3).Rows("Auto, Auto").Margin(0, 4, 0, 0).Children(
                    new DockPanel().Children(
                        new TextBlock().DockRight().Text("0"),
                        new TextBlock().Text("60 seconds")),
                    new UniformGrid().Row(1).Columns(3).Spacing(22).Margin(0, 18, 0, 0).Children(metrics)));
    }

    private FrameworkElement LogicalProcessorCharts()
    {
        int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(_logicalCpuHistories.Count * 1.6)));
        var grid = new UniformGrid().Columns(columns).Spacing(6);
        for (int i = 0; i < _logicalCpuHistories.Count; i++)
            grid.Add(CreateCpuChart(
                _logicalCpuHistories[i], _logicalKernelHistories[i], compact: true).MinHeight(56));
        return new Border().MinHeight(320).Child(grid);
    }

    private FrameworkElement CpuChartFrame(
        ObservableCollection<ObservablePoint> values,
        ObservableCollection<ObservablePoint> kernelValues,
        bool compact) =>
        CreateCpuChart(values, kernelValues, compact).MinHeight(compact ? 56 : 320);

    private TaskManagerChart CreateCpuChart(
        ObservableCollection<ObservablePoint> values,
        ObservableCollection<ObservablePoint> kernelValues,
        bool compact)
    {
        var chart = new TaskManagerChart(values, CpuColor, compact, kernelValues);
        _cpuCharts.Add(chart);
        return chart;
    }

    private static FrameworkElement Detail(
        string title,
        string subtitle,
        string graphLabel,
        string maximumLabel,
        ObservableCollection<ObservablePoint> values,
        Color color,
        params FrameworkElement[] metrics) =>
        Detail(title, new TextBlock().Text(subtitle), graphLabel, new TextBlock().Text(maximumLabel), values, color, metrics);

    private static FrameworkElement Detail(
        string title,
        ObservableValue<string> subtitle,
        string graphLabel,
        ObservableValue<string> maximumLabel,
        ObservableCollection<ObservablePoint> values,
        Color color,
        params FrameworkElement[] metrics) =>
        Detail(title, new TextBlock().BindText(subtitle), graphLabel, new TextBlock().BindText(maximumLabel), values, color, metrics);

    private static FrameworkElement Detail(
        string title,
        TextBlock subtitle,
        string graphLabel,
        TextBlock maximumLabel,
        ObservableCollection<ObservablePoint> values,
        Color color,
        params FrameworkElement[] metrics) =>
        new Grid()
            .Rows("Auto, Auto, *, Auto")
            .Children(
                new DockPanel().Children(
                    subtitle.DockRight().FontSize(ThemeFontSize.Medium).CenterVertical(),
                    new TextBlock().Text(title).FontSize(40).LineBoxTrim(LineBoxTrim.CapAndBaseline)),
                new DockPanel().Row(1).Margin(0, 12, 0, 4).Children(
                    maximumLabel.DockRight(),
                    new TextBlock().Text(graphLabel)),
                ChartFrame(values, color).Row(2),
                new Grid().Row(3).Rows("Auto, Auto").Margin(0, 4, 0, 0).Children(
                    new DockPanel().Children(
                        new TextBlock().DockRight().Text("0"),
                        new TextBlock().Text("60 seconds")),
                    new UniformGrid().Row(1).Columns(3).Spacing(22).Margin(0, 18, 0, 0).Children(metrics)));

    private static FrameworkElement ChartFrame(ObservableCollection<ObservablePoint> values, Color color) =>
        new TaskManagerChart(values, color, compact: false).MinHeight(320);

    private static FrameworkElement Metric(string title, ObservableValue<string> value) =>
        new StackPanel().Vertical().Spacing(2).Children(
            new TextBlock().Text(title).WithTheme((theme, text) => text.Foreground(theme.Palette.DisabledText)),
            new TextBlock().BindText(value).FontSize(ThemeFontSize.Large).LineBoxTrim(LineBoxTrim.CapAndBaseline));

    private void Update(IReadOnlyList<ProcessSample> _, PerformanceSample sample)
    {
        double now = MonotonicSeconds();
        Append(_cpuHistory, now, sample.CpuPercent);
        Append(_kernelHistory, now, sample.KernelPercent);
        for (int i = 0; i < _logicalCpuHistories.Count; i++)
        {
            double value = i < sample.LogicalProcessorPercents.Count ? sample.LogicalProcessorPercents[i] : 0;
            Append(_logicalCpuHistories[i], now, value);
            double kernel = i < sample.LogicalProcessorKernelPercents.Count
                ? sample.LogicalProcessorKernelPercents[i]
                : 0;
            Append(_logicalKernelHistories[i], now, kernel);
        }
        Append(_memoryHistory, now, sample.MemoryPercent);
        _cpuValue.Value = $"{sample.CpuPercent:0}%";
        _memoryValue.Value = $"{FormatGigabytes(sample.UsedMemoryBytes)}/{FormatGigabytes(sample.TotalMemoryBytes)} GB ({sample.MemoryPercent:0}%)";
        _processValue.Value = sample.ProcessCount.ToString();
        _threadValue.Value = sample.ThreadCount.ToString();
        _logicalProcessorValue.Value = (sample.LogicalProcessorPercents.Count > 0
            ? sample.LogicalProcessorPercents.Count
            : Environment.ProcessorCount).ToString();
        _uptimeValue.Value = $"{(int)sample.Uptime.TotalDays}:{sample.Uptime:hh\\:mm\\:ss}";
        _memoryUsedValue.Value = $"{FormatGigabytes(sample.UsedMemoryBytes)} GB";
        _memoryAvailableValue.Value = $"{FormatGigabytes(sample.TotalMemoryBytes - sample.UsedMemoryBytes)} GB";
        _memoryTotalValue.Value = $"{FormatGigabytes(sample.TotalMemoryBytes)} GB";
    }

    private static void Append(ObservableCollection<ObservablePoint> values, double timestamp, double value)
    {
        double cutoff = timestamp - 60;
        // Keep one point immediately before the visible window. The line segment is then clipped at
        // the -60 second boundary instead of starting one refresh interval inside the chart.
        while (values.Count > 1 && values[1].X is double nextX && nextX <= cutoff) values.RemoveAt(0);
        values.Add(new ObservablePoint(timestamp, value));
    }

    private static string FormatGigabytes(long bytes) => (bytes / (1024d * 1024 * 1024)).ToString("0.0");

    private static double MonotonicSeconds() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private enum PerformanceResource
    {
        Cpu,
        Memory,
        Disk,
        Network,
    }
}

internal sealed class TaskManagerChart : CartesianChart
{
    private readonly ObservableCollection<ObservablePoint> _values;
    private readonly Color _accent;
    private readonly bool _compact;
    private readonly LineSeries<ObservablePoint> _series;
    private readonly LineSeries<ObservablePoint>? _kernelSeries;
    private readonly Axis _xAxis;
    private readonly Axis _yAxis;

    public TaskManagerChart(
        ObservableCollection<ObservablePoint> values,
        Color accent,
        bool compact,
        ObservableCollection<ObservablePoint>? kernelValues = null)
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

        Series = _kernelSeries == null ? [_series] : [_series, _kernelSeries];
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
        base.OnDispose();
    }
}
