using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.SkiaSharp.Sample;

#if DEBUG
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Aprillz.MewUI.HotReload.MewUiMetadataUpdateHandler))]
#endif

Startup();

IconSource icon;
using (var rs = typeof(Program).Assembly.GetManifestResourceStream("Aprillz.MewUI.SkiaSharp.Sample.appicon.ico")!)
{
    icon = IconSource.FromStream(rs);
}

Window window = null!;
TextBlock backendText = null!;
TextBlock themeText = null!;
ObservableValue<ThemeVariant> themeMode = new(ThemeVariant.System);

var logo = ImageSource.FromFile(CombineBaseDirectory("Resources", "logo_h-1280.png"));

Application
    .Create()
    .UseAccent(Accent.Purple)
    .BuildMainWindow(() =>
        new Window()
            .Resizable(1080, 760)
            .StartCenterScreen()
            .OnBuild(x => x
                .Ref(out window)
                .Icon(icon)
                .Title("Aprillz.MewUI SkiaSharp Sample")
                .Content(
                    new DockPanel()
                        .Margin(8)
                        .Children(
                            TopBar()
                                .DockTop(),

                            new SkiaSampleView(window)
                        )
                )
                .OnLoaded(() =>
                {
                    window.Icon = icon;
                    themeMode.Value = Application.Current.ThemeMode;
                    Application.Current.ThemeModeChanged += HandleThemeModeChanged;
                    UpdateTopBar();
                })
                .OnClosed(() => Application.Current.ThemeModeChanged -= HandleThemeModeChanged)
            )
    )
    .Run();

void HandleThemeModeChanged()
{
    themeMode.Value = Application.Current.ThemeMode;
    UpdateTopBar();
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
                            .Width(280)
                            .Height(72)
                            .CenterVertical(),

                        new StackPanel()
                            .Vertical()
                            .Spacing(2)
                            .Children(
                                new TextBlock()
                                    .Text("Aprillz.MewUI SkiaSharp")
                                    .WithTheme((t, c) => c.Foreground(t.Palette.Accent))
                                    .FontSize(18)
                                    .SemiBold(),

                                new TextBlock()
                                    .Ref(out backendText)
                            )
                    )
                    .DockLeft(),

                new StackPanel()
                    .DockRight()
                    .Horizontal()
                    .CenterVertical()
                    .Spacing(12)
                    .Children(
                        ThemeModePicker(),

                        new TextBlock()
                            .Ref(out themeText)
                            .CenterVertical()
                    )
            )
    );

FrameworkElement ThemeModePicker() => new StackPanel()
    .Horizontal()
    .CenterVertical()
    .Spacing(8)
    .Children(
        new RadioButton()
            .Content("System")
            .CenterVertical()
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

void UpdateTopBar()
{
    backendText.Text($"Backend: {Application.Current.GraphicsFactory.Backend}");
    themeText.WithTheme((t, c) => c.Text($"Theme: {t.Name}"));
}

static string CombineBaseDirectory(params string[] path)
    => Path.Combine([AppContext.BaseDirectory, .. path]);

static void Startup()
{
#if MEWUI_GALLERY_WIN
#pragma warning disable CA1416
    Win32Platform.Register();
    MewVGWin32Backend.Register();
    MewVGWin32SkiaBackend.Register();
#pragma warning restore CA1416
#elif MEWUI_GALLERY_OSX
    MacOSPlatform.Register();
    MewVGMacOSBackend.Register();
    MewVGMacOSSkiaBackend.Register();
#elif MEWUI_GALLERY_LINUX
    X11Platform.Register();
    MewVGX11Backend.Register();
    MewVGX11SkiaBackend.Register();
#else
    if (OperatingSystem.IsWindows())
    {
        Win32Platform.Register();
        MewVGWin32Backend.Register();
        MewVGWin32SkiaBackend.Register();
    }
    else if (OperatingSystem.IsMacOS())
    {
        MacOSPlatform.Register();
        MewVGMacOSBackend.Register();
        MewVGMacOSSkiaBackend.Register();
    }
    else if (OperatingSystem.IsLinux())
    {
        X11Platform.Register();
        MewVGX11Backend.Register();
        MewVGX11SkiaBackend.Register();
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
