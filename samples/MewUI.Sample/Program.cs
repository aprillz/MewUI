using System.Diagnostics;

using Aprillz.MewUI.Binding;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Core;
using Aprillz.MewUI.Elements;
using Aprillz.MewUI.Markup;
using Aprillz.MewUI.Panels;
using Aprillz.MewUI.Primitives;
using Aprillz.MewUI.Rendering;

var stopwatch = Stopwatch.StartNew();

Startup(Environment.GetCommandLineArgs(), out var isBench, out var isSmoke);

double loadedMs = -1;
var metricsText = new ObservableValue<string>("Metrics:");

Window window;
Button enabledButton = null!;
var accentSwatches = new List<(Color color, Button button)>();
var currentAccent = Theme.Current.Accent;

var vm = new DemoViewModel();

var root = new Window()
    .Ref(out window)
    .Title("Aprillz.MewUI Demo")
    .Resizable(744, 740)
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
                HeaderSection().DockTop(),

                Buttons().DockBottom(),

                BindSamples().DockRight(),

                NormalControls()
            )
    )
    .OnFirstFrameRendered(ProcessMetric);

Application.Run(root);

Element HeaderSection() => new StackPanel()
    .Vertical()
    .Spacing(8)
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
    .Spacing(8)
    .Children(
        new Label()
            .Text("Accent")
            .Bold(),

        new WrapPanel()
            .Orientation(Orientation.Horizontal)
            .Spacing(8)
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

Button AccentSwatch(string name, Color color) => new Button()
    .Content(string.Empty)
    .Background(color)
    .OnClick(() => ApplyAccent(color))
    .Apply(b => accentSwatches.Add((color, b)));

Element Buttons() => new StackPanel()
    .Horizontal()
    .Spacing(8)
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

Element NormalControls()
{
    var notesTextBox = new MultiLineTextBox()
        .Wrap(true)
        .FontFamily("Consolas")
        .Height(120)
        .Placeholder("Type multi-line text (wheel scroll + thin scrollbar)")
        .Text("Line 1\nLine 2\nLine 3\nLine 4\nLine 5\nLine 6\nLine 7");

    return new StackPanel()
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
                        .CenterVertical()
                        .Apply(l => window.ThemeChanged += (_, newTheme) =>
                        {
                            l.Text($"Theme: {newTheme.Name}");
                            UpdateAccentSwatches();
                        })
                ),

            AccentPicker()
                .Margin(0, 0, 0, 12),

            new Grid()
                .AutoIndexing()
                .Columns("Auto, *")
                .Rows("Auto,Auto,Auto,Auto")
                .Spacing(8)
                .Margin(0, 0, 0, 10)
                .Children(
                    new Label()
                        .Text("Your name:")
                        .Column(0)
                        .Margin(0, 0, 10, 0)
                        .CenterVertical(),

                    new TextBox()
                        .Placeholder("Type your name")
                        .Column(1),

                    new Label()
                        .Text("Notes:")
                        .Column(0)
                        .Margin(0, 0, 10, 0)
                        .CenterVertical(),

                        new StackPanel()
                            .Column(1)
                            .Vertical()
                            .Spacing(8)
                            .Children(
                                notesTextBox,

                                new CheckBox()
                                    .Text("Wrap")
                                    .IsChecked(notesTextBox.Wrap)
                                    .OnCheckedChanged(v => notesTextBox.Wrap = v)
                            ),

                    new Label()
                        .CenterVertical()
                        .Text("ComboBox"),

                    new ComboBox()
                        .Items("Alpha", "Beta", "Gamma", "Delta")
                        .SelectedIndex(1)
                        .Placeholder("Select...")
                ),

            new StackPanel()
                .Spacing(8)
                .Children(
                    new Label()
                        .Text("Buttons")
                        .Bold(),

                    new StackPanel()
                        .Horizontal()
                        .Spacing(8)
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

                    new DockPanel()
                        .Children(
                            new StackPanel()
                                .Vertical()
                                .Spacing(8)
                                .Children(
                                    new CheckBox()
                                        .Text("Enable feature"),

                                    new StackPanel()
                                        .Horizontal()
                                        .Spacing(8)
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
                                        .Spacing(8)
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
                                            .Spacing(8)
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
                                .Margin(16,0,0,0)
                                .Height(106)
                                .Items("First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth")
                                .SelectedIndex(1)
                        )
                )
        );
}

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
                    .Spacing(8)
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
                    .Spacing(8)
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
                    .Text("Selection")
                    .Bold(),

                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new ListBox()
                            .Ref(out var selectionListBox)
                            .Height(120)
                            .Items("Alpha", "Beta", "Gamma", "Delta")
                            .BindSelectedIndex(vm.SelectedIndex),

                        new StackPanel()
                            .Vertical()
                            .Spacing(8)
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
                    ),

                    new Label()
                        .Text("Image")
                        .Bold()
                        ,


                    new Image()
                        .SourceFile("logo-256.png")
                        .Size(192, 192)
                        .StretchMode(ImageStretch.Uniform)
                )
    );
}

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

void ProcessMetric()
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
}

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

static void Startup(string[] args, out bool isBench, out bool isSmoke)
{
    isBench = args.Any(a => a.Equals("--bench", StringComparison.OrdinalIgnoreCase));
    isSmoke = args.Any(a => a.Equals("--smoke", StringComparison.OrdinalIgnoreCase));

    var useGdi = args.Any(a => a.Equals("--gdi", StringComparison.OrdinalIgnoreCase));
    var useOpenGl = args.Any(a => a.Equals("--gl", StringComparison.OrdinalIgnoreCase));

    Application.DefaultGraphicsBackend =
        useGdi ? GraphicsBackend.Gdi :
        useOpenGl ? GraphicsBackend.OpenGL :
        OperatingSystem.IsWindows() ? GraphicsBackend.Direct2D : GraphicsBackend.OpenGL;

    Application.UiUnhandledException += (_, e) =>
    {
        Log($"UI exception: {e.Exception.GetType().Name}: {e.Exception.Message}");
        Log(e.Exception.ToString());
        e.Handled = true;
    };

    Log($"Args: {string.Join(' ', Environment.GetCommandLineArgs())}");
    Log($"Backend: {Application.DefaultGraphicsBackend}");
    Log($"Bench: {isBench}, Smoke: {isSmoke}");
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
