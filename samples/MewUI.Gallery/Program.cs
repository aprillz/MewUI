using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Gallery;

#if DEBUG
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Aprillz.MewUI.HotReload.MewUiMetadataUpdateHandler))]
#endif

var stopwatch = Stopwatch.StartNew();
Startup();

Window window = null!;
Label backendText = null!;
Label themeText = null!;
var fpsText = new ObservableValue<string>("FPS: -");
var fpsStopwatch = new Stopwatch();
var fpsFrames = 0;
var maxFpsEnabled = new ObservableValue<bool>(false);

var currentAccent = ThemeManager.DefaultAccent;

var app = Application
    .Create()
    //.UseMetrics(ThemeMetrics.Default with { ControlCornerRadius = 10, ControlBorderThickness = 2 })
    .UseAccent(Accent.Purple);

var logo = ImageSource.FromFile("logo_h-1280.png");

var timer = new DispatcherTimer().Interval(TimeSpan.FromSeconds(1)).OnTick(() => CheckFPS(ref fpsFrames));

var root = new Window()
    .Resizable(1336, 720)
    .OnBuild(x => x
        .Ref(out window)
        .Title("Aprillz.MewUI Controls Gallery")
        .Padding(16)
        .Content(
            new DockPanel()
                .Children(
                    TopBar()
                        .DockTop(),

                    new GalleryView(window)
                )
        )
        .OnLoaded(() => { UpdateTopBar(); timer.Start(); })
        .OnClosed(() => maxFpsEnabled.Value = false)
        .OnFrameRendered(() =>
        {
            if (!fpsStopwatch.IsRunning)
            {
                fpsStopwatch.Restart();
                fpsFrames = 0;
                return;
            }

            fpsFrames++;
            CheckFPS(ref fpsFrames);
        })
    );

using (var rs = typeof(Program).Assembly.GetManifestResourceStream("Aprillz.MewUI.Gallery.appicon.ico")!)
{
    root.Icon = IconSource.FromStream(rs);
}

app.Run(root);

void CheckFPS(ref int fpsFrames)
{
    double elapsed = fpsStopwatch.Elapsed.TotalSeconds;
    if (elapsed >= 1.0)
    {
        fpsText.Value = $"FPS: {Math.Max(fpsFrames - 1, 0) / elapsed:0.0}";
        fpsFrames = 0;
        fpsStopwatch.Restart();
    }
}

FrameworkElement TopBar() => new Border()
    .Padding(12, 10)
    .BorderThickness(1)
    .Child(
        new DockPanel()
            .Spacing(12)
            .Children(
                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new Image()
                            .Source(logo)
                            .ImageScaleQuality(ImageScaleQuality.HighQuality)
                            .Width(300)
                            .Height(80)
                            .CenterVertical(),

                        new StackPanel()
                            .Vertical()
                            .Spacing(2)
                            .Children(
                                new Label()
                                    .Text("Aprillz.MewUI Gallery")
                                    .WithTheme((t, c) => c.Foreground(t.Palette.Accent))
                                    .FontSize(18)
                                    .SemiBold(),

                                new Label()
                                    .Ref(out backendText)
                            )
                    )
                    .DockLeft(),
                new StackPanel()
                    .DockRight()
                    .Spacing(8)
                    .Children(
                        new StackPanel()
                            .Horizontal()
                            .CenterVertical()
                            .Spacing(12)
                            .Children(
                                ThemeModePicker(),

                                new Label()
                                    .Ref(out themeText)
                                    .CenterVertical(),

                                AccentPicker()
                            ),

                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new CheckBox()
                                    .Text("Max FPS")
                                    .BindIsChecked(maxFpsEnabled)
                                    .OnCheckedChanged(_ => EnsureMaxFpsLoop())
                                    .CenterVertical(),

                                new Label()
                                    .BindText(fpsText)
                                    .CenterVertical()
                            )
                    )
            ));

FrameworkElement ThemeModePicker() => new StackPanel()
    .Horizontal()
    .CenterVertical()
    .Spacing(8)
    .Children(
        new RadioButton()
            .Text("System")
            .CenterVertical()
            .IsChecked()
            .OnChecked(() => Application.Current.SetTheme(ThemeVariant.System)),

        new RadioButton()
            .Text("Light")
            .CenterVertical()
            .OnChecked(() => Application.Current.SetTheme(ThemeVariant.Light)),

        new RadioButton()
            .Text("Dark")
            .CenterVertical()
            .OnChecked(() => Application.Current.SetTheme(ThemeVariant.Dark))
    );

FrameworkElement AccentPicker() => new WrapPanel()
    .Orientation(Orientation.Horizontal)
    .Spacing(6)
    .CenterVertical()
    .ItemWidth(22)
    .ItemHeight(22)
    .Children(BuiltInAccent.Accents.Select(AccentSwatch).ToArray());

Button AccentSwatch(Accent accent) => new Button()
    .Content(string.Empty)
    .WithTheme((t, c) => c.Background(accent.GetAccentColor(t.IsDark)))
    .ToolTip(accent.ToString())
    .OnClick(() =>
    {
        currentAccent = accent;
        Application.Current.SetAccent(accent);
        UpdateTopBar();
    });

void UpdateTopBar()
{
    backendText.Text($"Backend: {Application.Current.GraphicsFactory.Backend}");
    themeText.WithTheme((t, c) => c.Text($"Theme: {t.Name}"));
}

void EnsureMaxFpsLoop()
{
    if (!Application.IsRunning)
    {
        return;
    }

    var scheduler = Application.Current.RenderLoopSettings;
    scheduler.TargetFps = 0;
    scheduler.SetContinuous(maxFpsEnabled.Value);
    scheduler.VSyncEnabled = !maxFpsEnabled.Value;
}

static void Startup()
{
    var args = Environment.GetCommandLineArgs();

    if (OperatingSystem.IsWindows())
    {
        if (args.Any(a => a is "--gdi"))
        {
            Application.DefaultGraphicsBackend = GraphicsBackend.Gdi;
        }
        else if (args.Any(a => a is "--gl"))
        {
            Application.DefaultGraphicsBackend = GraphicsBackend.OpenGL;
        }
        else
        {
            Application.DefaultGraphicsBackend = GraphicsBackend.Direct2D;
        }
    }
    else
    {
        Application.DefaultGraphicsBackend = GraphicsBackend.OpenGL;
    }

    Application.DispatcherUnhandledException += e =>
    {
        try
        {
            MessageBox.Show(e.Exception.ToString(), "Unhandled UI exception");
        }
        catch
        {
            // ignore
        }
        e.Handled = true;
    };
}
