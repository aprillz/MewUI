using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Gallery;

if (OperatingSystem.IsWindows())
{
    Thread.CurrentThread.SetApartmentState(ApartmentState.Unknown);
    Thread.CurrentThread.SetApartmentState(ApartmentState.STA);
}

var stopwatch = Stopwatch.StartNew();
Startup();
IconSource icon;
using (var rs = typeof(Program).Assembly.GetManifestResourceStream("Aprillz.MewUI.Gallery.appicon.ico")!)
{
    icon = IconSource.FromStream(rs);
}

Window window = null!;
TextBlock backendText = null!;
TextBlock themeText = null!;
GalleryView gallery = null!;

ObservableValue<ThemeVariant> themeMode = new(ThemeVariant.System);

var fpsText = new ObservableValue<string>("FPS: -");
var cullText = new ObservableValue<string>("Cull: -");
var fpsStopwatch = new Stopwatch();
var fpsFrames = 0;
var maxFpsEnabled = new ObservableValue<bool>(false);
var topMost = new ObservableValue<bool>(false);

var currentAccent = ThemeManager.DefaultAccent;

var logo = ImageSource.FromFile(GalleryView.CombineBaseDirectory("Resources", "logo_h-1280.png"));

var timer = new DispatcherTimer()
    .Interval(TimeSpan.FromSeconds(1))
    .OnTick(() => CheckFPS(ref fpsFrames));

Application.DispatcherUnhandledException += e =>
{
    Console.WriteLine(e.Exception.ToString());
    e.Handled = true;
};

Application
    .Create()
    .UseAccent(Accent.Purple)
    .BuildMainWindow(() =>
    new Window()
        .Resizable(1356, 720)
        .StartCenterScreen()
        .OnBuild(x => x
            .Ref(out window)
            .Bind(Window.TopmostProperty, topMost)
            .Icon(icon)
            .Padding(0)
            .Title("Aprillz.MewUI Controls Gallery")
            .Content(
                new DockPanel()
                    .Margin(0)
                    .Children(
                        TopBar()
                            .DockTop(),

                        new GalleryView(window)
                            .Ref(out gallery)
                    )
            )
            .OnLoaded(() =>
            {
                window.Icon = icon;
                Application.Current.ThemeModeChanged += () => themeMode.Value = Application.Current.ThemeMode;
                gallery.SettingsContent = SettingsControls();   // creates themeText; must precede UpdateTopBar
                UpdateTopBar();
                timer.Start();
                Debug.WriteLine($"Loaded: {stopwatch.Elapsed.TotalSeconds:0.00}s");
            })
            .OnClosed(() => maxFpsEnabled.Value = false)
            .OnFirstFrameRendered(() =>
            {
                stopwatch.Stop();
                Debug.WriteLine($"First: {stopwatch.Elapsed.TotalSeconds:0.00}s");
            })
            .OnFrameRendered(() =>
            {
                if (!fpsStopwatch.IsRunning)
                {
                    fpsStopwatch.Restart();
                    fpsFrames = 0;
                    return;
                }

                fpsFrames++;
                if (CheckFPS(ref fpsFrames))
                {
                    var stats = window.LastFrameStats;
                    cullText.Value = $"Draw: {stats.DrawCalls} | Cull: {stats.CullCount} ({stats.CullRatio:P0})";
                }
            })
        )
    )
    .Run();


bool CheckFPS(ref int fpsFrames)
{
    double elapsed = fpsStopwatch.Elapsed.TotalSeconds;
    if (elapsed >= 1.0)
    {
        var fps = $"FPS: {(fpsFrames <= 1 ? 0 : fpsFrames) / elapsed:0.0}";
        fpsText.Value = fps;
        fpsFrames = 0;
        fpsStopwatch.Restart();

        return true;
    }
    else
    {
        return false;
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
                    .DockLeft()
                    .Children(
                        new Image()
                            .Source(logo)
                            .ImageScaleQuality(ImageScaleQuality.HighQuality)
                            .Width(200)
                            .CenterVertical(),

                        new StackPanel()
                            .Vertical()
                            .CenterVertical()
                            .Spacing(2)
                            .Children(
                                new TextBlock()
                                    .Text("Aprillz.MewUI Gallery")
                                    .WithTheme((t, c) => c.Foreground(t.Palette.Accent))
                                    .FontSize(18)
                                    .SemiBold(),

                                new TextBlock()
                                    .Ref(out backendText)
                            )
                    ),

                // Live diagnostics + the enable toggle stay outside the gallery (always visible, and the
                // toggle can never disable itself).
                new StackPanel()
                    .Horizontal()
                    .CenterVertical()
                    .Spacing(12)
                    .Children(
                        new ToggleButton()
                            .IsChecked()
                            .Content("Toggle")
                            .OnCheckedChanged(x => gallery.IsEnabled(x)),
                        new CheckBox()
                            .Content("Max FPS")
                            .BindIsChecked(maxFpsEnabled)
                            .OnCheckedChanged(_ => EnsureMaxFpsLoop())
                            .CenterVertical(),
                        new TextBlock().BindText(fpsText).CenterVertical(),
                        new TextBlock().BindText(cullText).CenterVertical())
            ));

// Theme / rendering controls moved out of the top bar into the pane-footer Settings page.
FrameworkElement SettingsControls() => new StackPanel()
    .Vertical()
    .Margin(16)
    .Spacing(16)
    .Children(
        new TextBlock().Text("Settings").FontSize(22).Bold(),

        new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().Text("Theme").FontSize(14).Bold(),
            new StackPanel().Horizontal().Spacing(12).CenterVertical().Children(
                ThemeModePicker(),
                new TextBlock().Ref(out themeText).CenterVertical())),

        new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().Text("Accent").FontSize(14).Bold(),
            AccentPicker()),

        new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().Text("Rendering").FontSize(14).Bold(),
            new WrapPanel().Spacing(12).Children(
                new CheckBox()
                    .Content("Cached")
                    .IsChecked(true)
                    .OnCheckedChanged(v => gallery.SetCardsCached(v == true))
                    .CenterVertical(),
                new CheckBox()
                    .Content("TopMost")
                    .BindIsChecked(topMost)
                    .CenterVertical()))
    );

FrameworkElement ThemeModePicker() => new StackPanel()
    .Horizontal()
    .CenterVertical()
    .Spacing(8)
    .Children(
        new RadioButton()
            .Content("System")
            .CenterVertical()
            .IsChecked()
            .BindIsChecked(themeMode, mode => mode == ThemeVariant.System)
            .OnChecked(() => Application.Current.SetTheme(ThemeVariant.System)),

        new RadioButton()
            .Content("Light")
            .CenterVertical()
            .BindIsChecked(themeMode, mode => mode == ThemeVariant.Light)
            .OnChecked(() => Application.Current.SetTheme(ThemeVariant.Light)),

        new RadioButton()
            .Content("Dark")
            .CenterVertical()
            .BindIsChecked(themeMode, mode => mode == ThemeVariant.Dark)
            .OnChecked(() => Application.Current.SetTheme(ThemeVariant.Dark))
    );

FrameworkElement AccentPicker() => new StackPanel()
    .Horizontal()
    .Spacing(6)
    .Children(BuiltInAccent.Accents.Select(AccentSwatch).ToArray());

Button AccentSwatch(Accent accent) => new Button()
    .CornerRadius(11)
    .CenterVertical()
    .MinHeight(22)
    .Width(22)
    .Height(22)
    .BorderThickness(0)
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
    scheduler.VSyncEnabled = !maxFpsEnabled.Value;
    scheduler.SetContinuous(maxFpsEnabled.Value);
}

static void Startup()
{
    var args = Environment.GetCommandLineArgs();

#if MEWUI_GALLERY_WIN
#pragma warning disable CA1416
    Win32Platform.Register();

#if MEWUI_GALLERY_BACKEND_GDI
    GdiBackend.Register();
#elif MEWUI_GALLERY_BACKEND_MEWVG
    MewVGWin32Backend.Register();
#elif MEWUI_GALLERY_BACKEND_DIRECT2D
    Direct2DBackend.Register();
#else
    if (args.Any(a => a is "--gdi"))
    {
        GdiBackend.Register();
    }
    else if (args.Any(a => a is "--vg"))
    {
        MewVGWin32Backend.Register();
    }
    else
    {
        Direct2DBackend.Register();
    }
#endif
#pragma warning restore CA1416
#elif MEWUI_GALLERY_OSX
    MacOSPlatform.Register();
    MewVGMacOSBackend.Register();
#elif MEWUI_GALLERY_LINUX
    X11Platform.Register();
    MewVGX11Backend.Register();
#else
    if (OperatingSystem.IsWindows())
    {
        Win32Platform.Register();

        if (args.Any(a => a is "--gdi"))
        {
            GdiBackend.Register();
        }
        else if (args.Any(a => a is "--vg"))
        {
            MewVGWin32Backend.Register();
        }
        else
        {
            Direct2DBackend.Register();
        }
    }
    else if (OperatingSystem.IsMacOS())
    {
        MacOSPlatform.Register();
        MewVGMacOSBackend.Register();
    }
    else if (OperatingSystem.IsLinux())
    {
        X11Platform.Register();
        MewVGX11Backend.Register();
    }
#endif

    Application.DispatcherUnhandledException += e =>
    {
        try
        {
            NativeMessageBox.Show(e.Exception.ToString(), "Unhandled UI exception");
        }
        catch
        {
            // ignore
        }
        e.Handled = true;
    };
}
