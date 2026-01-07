using System.Diagnostics;

using Aprillz.MewUI.Binding;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Core;
using Aprillz.MewUI.Elements;
using Aprillz.MewUI.Markup;
using Aprillz.MewUI.Panels;
using Aprillz.MewUI.Primitives;
using Aprillz.MewUI.Rendering;

static void Log(string message)
{
    try
    {
        Console.WriteLine($"[{DateTime.Now:HH:mm:ss.fff}] {message}");
    }
    catch
    {
        // ignore (no console attached)
    }
}

var stopwatch = Stopwatch.StartNew();

var vm = new DemoViewModel();

var isBench = Environment.GetCommandLineArgs().Any(a => a.Equals("--bench", StringComparison.OrdinalIgnoreCase));
var useGdi = Environment.GetCommandLineArgs().Any(a => a.Equals("--gdi", StringComparison.OrdinalIgnoreCase));
var useOpenGl = Environment.GetCommandLineArgs().Any(a => a.Equals("--gl", StringComparison.OrdinalIgnoreCase));
var isSmoke = Environment.GetCommandLineArgs().Any(a => a.Equals("--smoke", StringComparison.OrdinalIgnoreCase));

Application.DefaultGraphicsBackend =
    useGdi ? GraphicsBackend.Gdi :
    useOpenGl ? GraphicsBackend.OpenGL :
    OperatingSystem.IsWindows() ? GraphicsBackend.Direct2D : GraphicsBackend.OpenGL;
//Application.DefaultGraphicsBackend = GraphicsBackend.OpenGL;

Application.UiUnhandledException += (_, e) =>
{
    Log($"UI exception: {e.Exception.GetType().Name}: {e.Exception.Message}");
    Log(e.Exception.ToString());
    e.Handled = true;
};

Log($"Args: {string.Join(' ', Environment.GetCommandLineArgs())}");
Log($"Backend: {Application.DefaultGraphicsBackend}");
Log($"Bench: {isBench}, Smoke: {isSmoke}");
double loadedMs = -1;
var metricsText = new ObservableValue<string>("Metrics:");

Window window;
Button enabledButton = null!;
var accentSwatches = new List<(Color color, Button button)>();
var currentAccent = Theme.Current.Accent;

var root = new Window()
    .Ref(out window)
    .Title("Aprillz.MewUI Demo")
    .Size(620, 560)
    .Padding(10)
    .OnLoaded(() =>
    {
        loadedMs = stopwatch.Elapsed.TotalMilliseconds;
        UpdateAccentSwatches();
    })
    .Content(
        new DockPanel()
            .LastChildFill()
            .Margin(20)
            .Children(
                HeaderSection()
                    .DockTop(),

                Buttons()
                    .DockBottom(),

                BindSamples()
                    .DockRight(),

                NormalControls()
            )
    );

window.FirstFrameRendered = () =>
{
    WriteMetric();

    if (!isSmoke)
        return;

    try
    {
        var outDir = Path.Combine(AppContext.BaseDirectory, "smoke_out");
        Directory.CreateDirectory(outDir);

        Log($"Smoke output: {outDir}");
        File.AppendAllText(Path.Combine(outDir, "smoke_report.txt"),
            $"Backend={Application.DefaultGraphicsBackend}{Environment.NewLine}" +
            $"LoadedMs={loadedMs:F3}{Environment.NewLine}" +
            $"FirstFrameMs={stopwatch.Elapsed.TotalMilliseconds:F3}{Environment.NewLine}");

        if (Application.DefaultGraphicsBackend == GraphicsBackend.OpenGL)
            SmokeCapture.Request(Path.Combine(outDir, "frame.ppm"));
    }
    finally
    {
        Application.Quit();
    }
};

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
            .Children(
                AccentSwatch("Gold", Color.FromRgb(214, 176, 82)),
                AccentSwatch("Red", Color.FromRgb(244, 67, 54)),
                AccentSwatch("Pink", Color.FromRgb(233, 30, 99)),
                AccentSwatch("Purple", Color.FromRgb(156, 39, 176)),
                AccentSwatch("Deep Purple", Color.FromRgb(103, 58, 183)),
                AccentSwatch("Indigo", Color.FromRgb(63, 81, 181)),
                AccentSwatch("Blue", Color.FromRgb(33, 150, 243)),
                AccentSwatch("Light Blue", Color.FromRgb(3, 169, 244)),
                AccentSwatch("Teal", Color.FromRgb(0, 150, 136)),
                AccentSwatch("Green", Color.FromRgb(76, 175, 80)),
                AccentSwatch("Light Green", Color.FromRgb(139, 195, 74))
            )
    );

Button AccentSwatch(string name, Color color) =>
    new Button()
        .Content(string.Empty)
        .Background(color)
        .OnClick(() => ApplyAccent(color))
        .Apply(b => accentSwatches.Add((color, b)));

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
                        var nextBase = window.Theme.Name == Theme.Dark.Name ? Theme.Light : Theme.Dark;
                        Theme.Current = window.Theme = nextBase.WithAccent(currentAccent);
                        UpdateAccentSwatches();
                    }),

                new Label()
                    .Text("Theme: Light")
                    .Apply(l => window.ThemeChanged += (_, newTheme) =>
                    {
                        l.Text($"Theme: {newTheme.Name}");
                        UpdateAccentSwatches();
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
                            .OnClick(() => MessageBox.Show(window.Handle, "Button clicked!", "Aprillz.MewUI Demo", MessageBoxButtons.Ok, MessageBoxIcon.Information)),

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
                    .Horizontal()
                    .Spacing(12)
                    .Children(
                new CheckBox()
                    .Text("Enable feature"),

                new RadioButton()
                    .Text("A")
                    .GroupName("group1")
                    .IsChecked(true),

                new RadioButton()
                    .Text("B")
                    .GroupName("group1")
            ),

            new ListBox()
                .Items("First", "Second", "Third")
                .SelectedIndex(1)
                .Height(70),

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

Element BindSamples() => new StackPanel()
    .Vertical()
    .Margin(20, 0, 0, 0)
    .Children(
        new Label()
            .Text("Binding Demo")
            .Bold(),

        new Grid()
            .Rows("Auto,Auto,Auto,Auto")
            .Columns("100,*")
            .ChildMargin(8)
            .Children(
                new Label()
                    .Row(0).Column(0)
                    .BindText(vm.Percent, v => $"Percent ({Math.Round(v):0}%)")
                    .Bold(),

                new StackPanel()
                    .Row(0).Column(1)
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
                    .Row(1).Column(0)
                    .Text("Name")
                    .Bold(),

                new UniformGrid()
                    .Columns(2)
                    .Row(1).Column(1)
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
                    .Row(2).Column(0)
                    .Text("Enabled")
                    .Bold(),

                new StackPanel()
                    .Row(2).Column(1)
                    .Horizontal()
                    .Spacing(10)
                    .Children(
                        new CheckBox()
                            .Text("Enabled")
                            .Apply(cb =>
                            {
                                cb.CheckedChanged = v =>
                                {
                                    vm.EnabledButtonText.Value = v ? "Enabled action" : "Disabled action";
                                    enabledButton?.IsEnabled = v;
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
                    .Row(3).Column(0)
                    .Text("Selection")
                    .Bold(),

                new StackPanel()
                    .Row(3).Column(1)
                    .Horizontal()
                    .Spacing(10)
                    .Children(
                        new ListBox()
                            .Ref(out var selectionListBox)
                            .Items("Alpha", "Beta", "Gamma", "Delta")
                            .BindSelectedIndex(vm.SelectedIndex)
                            .Height(90)
                            .Width(80),

                        new Label()
                            .BindText(vm.SelectedIndex, i => $@"SelectedIndex = {i}{Environment.NewLine}Item = {selectionListBox.SelectedItem ?? string.Empty}")
                    )
            )

    );

void UpdateAccentSwatches()
{
    foreach (var (color, button) in accentSwatches)
    {
        bool selected = Theme.Current.Accent == color;
        button.BorderThickness = selected ? 2 : 1;
    }
}

void ApplyAccent(Color accent)
{
    currentAccent = accent;
    Theme.Current = window.Theme = window.Theme.WithAccent(accent);
    UpdateAccentSwatches();
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
    var path = Path.Combine(AppContext.BaseDirectory, "metrics.log");
    File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {metricsText.Value}{Environment.NewLine}");
    if (isBench)
        Application.Quit();
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
