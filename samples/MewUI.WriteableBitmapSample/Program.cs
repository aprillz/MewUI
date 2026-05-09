using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.WriteableBitmapSample.Controls;

Startup();

Window window = null!;
PixelCanvas pixelCanvas = null!;
PlasmaEffect plasma = null!;
SimpleChart chart = null!;

var brushColors = new[]
{
    (Color.Black, "Black"),
    (Color.Red, "Red"),
    (Color.Green, "Green"),
    (Color.Blue, "Blue"),
    (new Color(255, 255, 165, 0), "Orange"),
    (new Color(255, 128, 0, 128), "Purple"),
};

var chartData = GenerateRandomData(20);

var root = new Window()
    .Resizable(900, 700)
    .OnBuild(x => x
        .Ref(out window)
        .Title("WriteableBitmap Sample - Custom Control Development")
        .Padding(0)
        .Content(
            new DockPanel()
                .Children(
                    new MenuBar()
                        .DockTop()
                        .Items(
                            new MenuItem("File").Menu(
                                new Menu()
                                    .Item("Exit", () => Application.Quit())
                            )
                        ),

                    new TabControl()
                        .Margin(8)
                        .TabItems(
                            new TabItem()
                                .Header("Pixel Canvas")
                                .Content(PixelCanvasDemo()),

                            new TabItem()
                                .Header("Chart")
                                .Content(ChartDemo()),

                            new TabItem()
                                .Header("Plasma Effect")
                                .Content(PlasmaDemo()),

                            new TabItem()
                                .Header("Gradients")
                                .Content(GradientDemo())
                        )
                )
        )
    );

Application.Run(root);

Element PixelCanvasDemo() => new DockPanel()
    .Spacing(8)
    .Padding(8)
    .Children(
        new StackPanel()
            .DockTop()
            .Horizontal()
            .Spacing(8)
            .Children(
                new TextBlock()
                    .Text("Brush Color:")
                    .CenterVertical(),

                new ComboBox()
                    .Width(120)
                    .Items(brushColors.Select(c => c.Item2).ToArray())
                    .SelectedIndex(0)
                    .OnSelectionChanged(s =>
                    {
                        if (s is Tuple<Color, string> accent)
                        {
                            pixelCanvas.BrushColor = accent.Item1;
                        }
                    }),

                new TextBlock()
                    .Text("Brush Size:")
                    .CenterVertical(),

                new Slider()
                    .Width(100)
                    .Minimum(1)
                    .Maximum(30)
                    .Value(3)
                    .OnValueChanged(v => pixelCanvas.BrushSize = (int)v),

                new Button()
                    .Content("Clear")
                    .Width(80)
                    .OnClick(() => pixelCanvas.Clear())
            ),

        new GroupBox()
            .Header("Canvas (Draw with mouse)")
            .Content(
                new PixelCanvas()
                    .Ref(out pixelCanvas)
                    .BrushColor(Color.Black)
                    .BrushSize(3)
            )
    );

Element ChartDemo()
{
    var showFill = new ObservableValue<bool>(true);
    var showGrid = new ObservableValue<bool>(true);

    return new DockPanel()
        .Spacing(8)
        .Padding(8)
        .Children(
            new StackPanel()
                .DockTop()
                .Horizontal()
                .Spacing(12)
                .Children(
                    new Button()
                        .Content("Randomize")
                        .Width(100)
                        .OnClick(() =>
                        {
                            chartData = GenerateRandomData(20);
                            chart.Data = chartData;
                        }),

                    new Button()
                        .Content("Sine Wave")
                        .Width(100)
                        .OnClick(() =>
                        {
                            chartData = Enumerable.Range(0, 50)
                                .Select(i => Math.Sin(i * 0.2) * 50 + 50)
                                .ToArray();
                            chart.Data = chartData;
                        }),

                    new Button()
                        .Content("Add Points")
                        .Width(100)
                        .OnClick(() =>
                        {
                            chartData = chartData.Concat(GenerateRandomData(5)).ToArray();
                            chart.Data = chartData;
                        }),

                    new CheckBox()
                        .Content("Show Fill")
                        .BindIsChecked(showFill)
                        .OnCheckedChanged(v => chart.ShowFill = v),

                    new CheckBox()
                        .Content("Show Grid")
                        .BindIsChecked(showGrid)
                        .OnCheckedChanged(v => chart.ShowGrid = v)
                ),

            new GroupBox()
                .Header("Line Chart")
                .Content(
                    new SimpleChart()
                        .Ref(out chart)
                        .Data(chartData)
                        .ShowFill(true)
                        .ShowGrid(true)
                )
        );
}

Element PlasmaDemo()
{
    var isAnimating = new ObservableValue<bool>(false);
    var speed = new ObservableValue<double>(1.0);

    return new DockPanel()
        .Spacing(8)
        .Padding(8)
        .Children(
            new StackPanel()
                .DockTop()
                .Horizontal()
                .Spacing(12)
                .Children(
                    new Button()
                        .Content("Start/Stop")
                        .Width(100)
                        .OnClick(() =>
                        {
                            plasma.Toggle();
                            isAnimating.Value = plasma.IsAnimating;
                        }),

                    new TextBlock()
                        .BindText(isAnimating, v => v ? "Running" : "Stopped")
                        .CenterVertical(),

                    new TextBlock()
                        .Text("Speed:")
                        .CenterVertical(),

                    new Slider()
                        .Width(150)
                        .Minimum(0.1)
                        .Maximum(3.0)
                        .BindValue(speed)
                        .OnValueChanged(v => plasma.Speed = v),

                    new TextBlock()
                        .BindText(speed, v => $"{v:F1}x")
                        .CenterVertical()
                ),

            new GroupBox()
                .Header("Plasma Effect (Real-time rendering test)")
                .Content(
                    new PlasmaEffect()
                        .Ref(out plasma)
                        .Speed(1.0)
                )
        );
}

Element GradientDemo()
{
    GradientViewer linear = null!;
    GradientViewer radial = null!;
    GradientViewer angular = null!;
    GradientViewer diamond = null!;

    var startColor = new Color(255, 255, 0, 0);
    var endColor = new Color(255, 0, 0, 255);
    var middleColor = new Color(255, 0, 255, 0);

    return new ScrollViewer()
        .Padding(8)
        .Content(
            new StackPanel()
                .Vertical()
                .Spacing(16)
                .Children(
                    new StackPanel()
                        .Horizontal()
                        .Spacing(12)
                        .Children(
                            new Button()
                                .Content("Red -> Blue")
                                .OnClick(() =>
                                {
                                    UpdateGradients(new Color(255, 255, 0, 0), null, new Color(255, 0, 0, 255));
                                }),

                            new Button()
                                .Content("Rainbow")
                                .OnClick(() =>
                                {
                                    UpdateGradients(
                                        new Color(255, 255, 0, 0),
                                        new Color(255, 0, 255, 0),
                                        new Color(255, 0, 0, 255));
                                }),

                            new Button()
                                .Content("Sunset")
                                .OnClick(() =>
                                {
                                    UpdateGradients(
                                        new Color(255, 255, 94, 77),
                                        new Color(255, 255, 154, 97),
                                        new Color(255, 60, 60, 100));
                                }),

                            new Button()
                                .Content("Ocean")
                                .OnClick(() =>
                                {
                                    UpdateGradients(
                                        new Color(255, 0, 180, 216),
                                        null,
                                        new Color(255, 0, 53, 102));
                                })
                        ),

                    new Grid()
                        .Rows("*,*")
                        .Columns("*,*")
                        .Spacing(16)
                        .Children(
                            new GroupBox()
                                .Header("Linear Gradient")
                                .Height(180)
                                .Content(
                                    new GradientViewer()
                                        .Ref(out linear)
                                        .GradientType(GradientType.Linear)
                                        .Colors(startColor, endColor)
                                ),

                            new GroupBox()
                                .Header("Radial Gradient")
                                .Height(180)
                                .Content(
                                    new GradientViewer()
                                        .Ref(out radial)
                                        .GradientType(GradientType.Radial)
                                        .Colors(startColor, endColor)
                                ),

                            new GroupBox()
                                .Header("Angular Gradient")
                                .Height(180)
                                .Content(
                                    new GradientViewer()
                                        .Ref(out angular)
                                        .GradientType(GradientType.Angular)
                                        .Colors(startColor, endColor)
                                ),

                            new GroupBox()
                                .Header("Diamond Gradient")
                                .Height(180)
                                .Content(
                                    new GradientViewer()
                                        .Ref(out diamond)
                                        .GradientType(GradientType.Diamond)
                                        .Colors(startColor, endColor)
                                )
                        )
                )
        );

    void UpdateGradients(Color start, Color? middle, Color end)
    {
        if (middle.HasValue)
        {
            linear.Colors(start, middle.Value, end);
            radial.Colors(start, middle.Value, end);
            angular.Colors(start, middle.Value, end);
            diamond.Colors(start, middle.Value, end);
        }
        else
        {
            linear.Colors(start, end);
            radial.Colors(start, end);
            angular.Colors(start, end);
            diamond.Colors(start, end);
        }
    }
}

static double[] GenerateRandomData(int count)
{
    var random = new Random();
    return Enumerable.Range(0, count)
        .Select(_ => random.NextDouble() * 100)
        .ToArray();
}

static void Startup()
{
    var args = Environment.GetCommandLineArgs();

    if (OperatingSystem.IsWindows())
    {
        Win32Platform.Register();

        //if (args.Any(a => a is "--gdi"))
        //{
        //GdiBackend.Register();
        //}
        //else if (args.Any(a => a is "--gl"))
        //{
        //OpenGLWin32Backend.Register();
        //}
        //else
        //{
        Direct2DBackend.Register();
        //}
    }
 

    Application.DispatcherUnhandledException += e =>
    {
        Console.WriteLine($"UI exception: {e.Exception.GetType().Name}: {e.Exception.Message}");
        Console.WriteLine(e.Exception.ToString());
        e.Handled = true;
    };
}
