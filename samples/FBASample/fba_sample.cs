#:sdk Microsoft.NET.Sdk
#:property OutputType=WinExe
#:property TargetFramework=net10.0
#:property PublishAot=true
#:property TrimMode=full
#:property IlcOptimizationPreference=Size

#:property InvariantGlobalization=true

#:property DebugType=none
#:property StripSymbols=true

#:package Aprillz.MewUI@0.8.5

using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

var stopwatch = Stopwatch.StartNew();

var vm = new DemoViewModel();

Application.DefaultGraphicsBackend =
    OperatingSystem.IsWindows() ? GraphicsBackend.Direct2D : GraphicsBackend.OpenGL;

double loadedMs = -1;
var metricsText = new ObservableValue<string>("Metrics:");

Window window;
Button enabledButton = null!;
Accent currentAccent = Theme.DefaultAccent;
Theme theme = Theme.Light;

var root = new Window()
    .Ref(out window)
    .Title("Aprillz.MewUI Demo")
    .Resizable(744, 740)
    .Padding(10)
    .OnLoaded(() =>
    {
        loadedMs = stopwatch.Elapsed.TotalMilliseconds;
    })
    .Content(
        new DockPanel()
            .LastChildFill()
            .Margin(20)
            .Children(
                HeaderSection().DockTop(),

                Buttons().DockBottom(),

                BindSamples().DockRight(),

                NormalControls()
            )
    )
    .OnFirstFrameRendered(WriteMetric);

Application.Run(root);

Element HeaderSection() => new StackPanel()
    .Vertical()
    .Spacing(6)
    .Margin(0, 0, 0, 12)
    .Children(
        new Label()
            .Text("Aprillz.MewUI Demo")
            .FontSize(20)
            .Bold(),

        new Label()
            .BindText(metricsText)
            .FontSize(11)
    );

FrameworkElement AccentPicker() => new StackPanel()
    .Vertical()
    .Spacing(6)
    .Children(
        new Label()
            .Text("Accent")
            .Bold(),

        new WrapPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(6)
            .ItemWidth(28)
            .ItemHeight(28)
            .Children(BuiltInAccent.Accents.Select(AccentSwatch).ToArray())
    );

Button AccentSwatch(Accent accent) =>
    new Button()
        .Content(string.Empty)
        .Background(theme.GetAccentColor(accent))
        .OnClick(() => ApplyAccent(accent));

Element Buttons() => new StackPanel()
    .Horizontal()
    .Spacing(10)
    .Right()
    .Children(
        new Button()
            .Content("OK")
            .Width(80)
            .OnClick(() => MessageBox.Show(window.Handle, "OK clicked", "Aprillz.MewUI Demo", MessageBoxButtons.Ok, MessageBoxIcon.Information)),

        new Button()
            .Content("Cancel")
            .Width(80)
            .OnClick(() => Application.Quit())
    );

Element NormalControls() => new StackPanel()
    .Children(
        new StackPanel()
            .Horizontal()
            .Spacing(8)
            .Margin(0, 16, 0, 12)
            .Children(
                new Button()
                    .Content("Toggle Theme")
                    .OnClick(() =>
                    {
                        var nextBase = theme.Name == Theme.Dark.Name ? Theme.Light : Theme.Dark;
                        Application.Current.Theme = theme = nextBase.WithAccent(currentAccent);
                    }),

                new Label()
                    .Text("Theme: Light")
                    .Apply(l => window.ThemeChanged += (_, newTheme) =>
                    {
                        l.Text($"Theme: {newTheme.Name}");
                    })
                    .CenterVertical()
            ),

        AccentPicker()
            .Margin(0, 0, 0, 12),

        new Grid()
            .Columns("Auto, *")
            .Margin(0, 0, 0, 10)
            .Children(
                new Label()
                    .Text("Your name:")
                    .Column(0)
                    .Margin(0, 0, 10, 0)
                    .CenterVertical(),

                new TextBox()
                    .Placeholder("Type your name")
                    .Column(1)
            ),

        new Grid()
            .Columns("Auto, *")
            .Margin(0, 0, 0, 10)
            .Children(
                new Label()
                    .Text("Notes:")
                    .Column(0)
                    .Margin(0, 0, 10, 0)
                    .CenterVertical(),

                new MultiLineTextBox()
                    .Column(1)
                    .Height(80)
                    .Placeholder("Type multi-line text (wheel scroll + thin scrollbar)")
                    .Text("Line 1\nLine 2\nLine 3\nLine 4\nLine 5\nLine 6\nLine 7")
            ),

        new StackPanel()
            .Vertical()
            .Spacing(10)
            .Margin(0, 10, 0, 10)
            .Children(
                new Label()
                    .Text("Buttons")
                    .Bold(),

                new StackPanel()
                    .Horizontal()
                    .Spacing(10)
                    .Children(
                        new Button()
                            .Content("Click!")
                            .OnClick(() => new Window()
                                .Fixed(400, 600)
                                .Title("New Window")
                                .Content(
                                    BindSamples()
                                        .Margin(12)
                                )
                                .Show()),

                        new Button()
                            .Content("Disabled")
                            .Apply(b => b.IsEnabled = false),

                    new Button()
                        .Content("Async/Await")
                        .OnClick(async () =>
                        {
                            vm.AsyncStatus.Value = "Async: running...";
                            await Task.Delay(750);
                            vm.AsyncStatus.Value = $"Async: done @ {DateTime.Now:HH:mm:ss}";
                        })
                    ),

                new Label()
                    .BindText(vm.AsyncStatus)
                    .Margin(0, 0, 0, 6),

                new Label()
                    .Text("Options")
                    .Bold(),

                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new CheckBox()
                            .Text("Enable feature"),

                        new StackPanel()
                            .Horizontal()
                            .Spacing(12)
                            .Children(
                                new Label().Text("GroupName: group1").CenterVertical(),
                                new RadioButton()
                                    .Text("A")
                                    .GroupName("group1")
                                    .IsChecked(true),
                                new RadioButton()
                                    .Text("B")
                                    .GroupName("group1")
                            ),

                        new StackPanel()
                            .Horizontal()
                            .Spacing(12)
                            .Children(
                                new Label().Text("GroupName: group2").CenterVertical(),
                                new RadioButton()
                                    .Text("C")
                                    .GroupName("group2")
                                    .IsChecked(true),
                                new RadioButton()
                                    .Text("D")
                                    .GroupName("group2")
                            ),

                        new StackPanel()
                            .Horizontal()
                            .Spacing(12)
                            .Children(
                                new Label().Text("GroupName: (parent-scope)").CenterVertical(),
                                new RadioButton()
                                    .Text("X")
                                    .IsChecked(true),
                                new RadioButton()
                                    .Text("Y")
                            )
                    ),

            new ListBox()
                .Items("First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth")
                .SelectedIndex(1)
                .Height(76),

            new DockPanel()
                .Children(
                    new Label()
                        .CenterVertical()
                        .Text("ComboBox"),

                    new ComboBox()
                        .Margin(16, 0, 0, 0)
                        .Items("Alpha", "Beta", "Gamma", "Delta")
                        .SelectedIndex(1)
                        .Placeholder("Select...")
                )
        ));

FrameworkElement BindSamples()
{
    var selectionItemCount = new ObservableValue<int>(4);

    return new StackPanel()
        .Vertical()
        .Margin(20, 0, 0, 0)
        .Children(
            new Label()
                .Text("Binding Demo")
                .Bold(),

            new Grid()
                .Rows("Auto,Auto,Auto,Auto,*")
                .Columns("100,*")
                .Spacing(8)
                .AutoIndexing()
                .Children(
                new Label()
                    .BindText(vm.Percent, v => $"Percent ({Math.Round(v):0}%)")
                    .Bold(),

                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new Slider()
                            .Minimum(0)
                            .Maximum(100)
                            .BindValue(vm.Percent),

                        new ProgressBar()
                            .Minimum(0)
                            .Maximum(100)
                            .BindValue(vm.Percent)
                    ),

                new Label()
                    .Text("Name")
                    .Bold(),

                new UniformGrid()
                    .Columns(2)
                    .Children(
                        new TextBox()
                            .Width(100)
                            .BindText(vm.Name),

                        new Label()
                            .Margin(10, 0, 0, 0)
                            .BindText(vm.Name)
                            .CenterVertical()
                    ),

                new Label()
                    .Text("Enabled")
                    .Bold(),

                new StackPanel()
                    .Horizontal()
                    .Spacing(10)
                    .Children(
                        new CheckBox()
                            .Text("Enabled")
                            .Apply(cb =>
                            {
                                cb.CheckedChanged += v =>
                                {
                                    vm.EnabledButtonText.Value = (v ?? false) ? "Enabled action" : "Disabled action";
                                    enabledButton?.IsEnabled = (v ?? false);
                                };
                            })
                            .BindIsChecked(vm.IsEnabled),

                        new Button()
                            .Ref(out enabledButton)
                            .BindContent(vm.EnabledButtonText)
                            .OnClick(() => MessageBox.Show(window.Handle, "Enabled button clicked", "Aprillz.MewUI Demo", MessageBoxButtons.Ok, MessageBoxIcon.Information))
                            .Apply(b => b.IsEnabled = vm.IsEnabled.Value)
                    ),

                new Label()
                    .Text("Selection")
                    .Bold(),

                new StackPanel()
                    .Vertical()
                    .Spacing(10)
                    .Children(
                        new ListBox()
                            .Ref(out var selectionListBox)
                            .Height(120)
                            .Items("Alpha", "Beta", "Gamma", "Delta")
                            .BindSelectedIndex(vm.SelectedIndex),

                        new StackPanel()
                            .Vertical()
                            .Spacing(6)
                            .Children(
                                new Label()
                                    .BindText(vm.SelectedIndex, i => $@"SelectedIndex = {i}{Environment.NewLine}Item = {selectionListBox.SelectedItem ?? string.Empty}"),

                                new Button()
                                    .Content("Add 40,000 ")
                                    .OnClick(() =>
                                    {
                                        const int repeat = 10_000;
                                        var items = selectionListBox.Items;

                                        if (items is List<string> list)
                                            list.EnsureCapacity(list.Count + repeat * 4);

                                        for (int i = 0; i < repeat; i++)
                                        {
                                            items.Add("Alpha");
                                            items.Add("Beta");
                                            items.Add("Gamma");
                                            items.Add("Delta");
                                        }

                                        selectionListBox.InvalidateMeasure();
                                        selectionItemCount.Value = items.Count;
                                    }),

                                new Label()
                                    .BindText(selectionItemCount, c => $"Items: {c:N0}")
                            )
                    )
            )
    );
}

void ApplyAccent(Accent accent)
{
    currentAccent = accent;
    Application.Current.Theme = theme = theme.WithAccent(accent);
}

void WriteMetric()
{
    var firstFrameMs = stopwatch.Elapsed.TotalMilliseconds;
    using var p = Process.GetCurrentProcess();
    p.Refresh();

    double wsMb = p.WorkingSet64 / (1024.0 * 1024.0);
    double pmMb = p.PrivateMemorySize64 / (1024.0 * 1024.0);

    var loadedText = loadedMs >= 0 ? $"{loadedMs:0} ms" : "n/a";
    metricsText.Value = $"Metrics ({Application.DefaultGraphicsBackend}): Loaded {loadedText}, FirstFrame {firstFrameMs:0} ms, WS {wsMb:0.0} MB, Private {pmMb:0.0} MB";
}

class DemoViewModel
{
    public ObservableValue<double> Percent { get; } = new(25, v => Math.Clamp(v, 0, 100));

    public ObservableValue<string> Name { get; } = new("Net Core");

    public ObservableValue<bool> IsEnabled { get; } = new(true);

    public ObservableValue<string> EnabledButtonText { get; } = new("Enabled action");

    public ObservableValue<int> SelectedIndex { get; } = new(1, v => Math.Max(-1, v));

    public ObservableValue<string> AsyncStatus { get; } = new("Async: idle");
}

public static class Extensions
{
    public static T Apply<T>(this T obj, Action<T> action)
    {
        action(obj);
        return obj;
    }
}
