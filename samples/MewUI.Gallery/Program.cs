using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;


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

var logo = ImageSource.FromFile("logo_h-1280.png");
var april = ImageSource.FromFile("april.jpg");

var root = new Window()
    .Resizable(1080, 840)
    .Ref(out window)
    .Title("Aprillz.MewUI Controls Gallery")
    .Padding(0)
    .Content(
        new DockPanel()
            .LastChildFill()
            .Children(
                TopBar()
                    .DockTop(),

                new DockPanel()
                    .Padding(8)
                    .Spacing(8)
                    .Children(
                        GalleryRoot()
                    )
            )
    )
    .OnLoaded(() => UpdateTopBar())
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
            double elapsed = fpsStopwatch.Elapsed.TotalSeconds;
            if (elapsed >= 1.0)
            {
                fpsText.Value = $"FPS: {fpsFrames / elapsed:0.0}";
                fpsFrames = 0;
                fpsStopwatch.Restart();
            }
        });

using (var rs = typeof(Program).Assembly.GetManifestResourceStream("Aprillz.MewUI.Gallery.appicon.ico")!)
{
    root.Icon = IconSource.FromStream(rs);
}

Application.Run(root);

FrameworkElement TopBar()
{
    return new Border()
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
                                        .FontSize(18)
                                        .Bold(),

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

                                    new TextBlock()
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
}

FrameworkElement ThemeModePicker()
{
    const string group = "ThemeMode";

    return new StackPanel()
        .Horizontal()
        .CenterVertical()
        .Spacing(8)
        .Children(
            new RadioButton()
                .Text("System")
                .GroupName(group)
                .CenterVertical()
                .IsChecked()
                .OnChecked(() => Application.Current.SetTheme(ThemeVariant.System)),

            new RadioButton()
                .Text("Light")
                .GroupName(group)
                .CenterVertical()
                .OnChecked(() => Application.Current.SetTheme(ThemeVariant.Light)),

            new RadioButton()
                .Text("Dark")
                .GroupName(group)
                .CenterVertical()
                .OnChecked(() => Application.Current.SetTheme(ThemeVariant.Dark))
        );
}

FrameworkElement AccentPicker()
{
    return new WrapPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(6)
        .CenterVertical()
        .ItemWidth(22)
        .ItemHeight(22)
        .Children(BuiltInAccent.Accents.Select(AccentSwatch).ToArray());
}

Button AccentSwatch(Accent accent)
{
    return new Button()
        .Content(string.Empty)
        .WithTheme((t, c) => c.Background(accent.GetAccentColor(t.IsDark)))
        .ToolTip(accent.ToString())
        .OnClick(() =>
        {
            currentAccent = accent;
            Application.Current.SetAccent(accent);
            UpdateTopBar();
        });
}

FrameworkElement GalleryRoot()
{
    return new ScrollViewer()
                .VerticalScroll(ScrollMode.Auto)
                .Padding(8)
                .Content(BuildGalleryContent());
}

FrameworkElement Card(string title, FrameworkElement content, double minWidth = 320)
{
    var header = new Label()
        .WithTheme((t, c) => c.Foreground(t.Palette.Accent))
        .Text(title)
        .Bold()
        .FontSize(12);

    return new Border()
        .MinWidth(minWidth)
        .Padding(14)
        .BorderThickness(1)
        .CornerRadius(10)
        .Child(new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(header, content));
}

FrameworkElement CardGrid(params FrameworkElement[] cards) =>
    new WrapPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8)
        .Children(cards);

FrameworkElement BuildGalleryContent()
{
    FrameworkElement Section(string title, FrameworkElement content) =>
        new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new Label()
                    .Text(title)
                    .FontSize(18)
                    .Bold(),
                content
            );

    return new StackPanel()
        .Vertical()
        .Spacing(16)
        .Children(
            Section("Buttons", ButtonsPage()),
            Section("Inputs", InputsPage()),
            Section("Menus", MenusPage()),
            Section("Selection", SelectionPage()),
            Section("Lists", ListsPage()),
            Section("Layout", LayoutPage()),
            Section("Media", MediaPage())
        );
}

FrameworkElement ButtonsPage()
{
    var vm = new ObservableValue<bool>(true);

    return CardGrid(
        Card("Buttons",
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new Button().Content("Default"),
                            new Button()
                                .Content("Accent")
                                .WithTheme((t, c) => c.Background(t.Palette.Accent).Foreground(t.Palette.AccentText)),
                            new Button().Content("Disabled").Disable(),
                            new Button().Content("With icon").Content("Clock")
                        )
        ),

        Card("Toggle / Switch",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new ToggleSwitch().IsChecked(true),
                    new ToggleSwitch().IsChecked(false),
                    new ToggleSwitch().IsChecked(true).Disable(),
                    new ToggleSwitch().IsChecked(false).Disable()
                )
        ),

        Card("Progress",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new ProgressBar().Value(20),
                    new ProgressBar().Value(65),
                    new ProgressBar().Value(65).Disable(),
                    new Slider().Minimum(0).Maximum(100).Value(25),
                    new Slider().Minimum(0).Maximum(100).Value(25).Disable()
                )
        )
    );
}

FrameworkElement InputsPage()
{
    var name = new ObservableValue<string>("Type your name");

    return CardGrid(
        Card("TextBox",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBox(),
                    new TextBox().Placeholder("Your name"),
                    new TextBox().BindText(name),
                    new TextBox().Text("Disabled").Disable()
                )
        ),

        Card("MultiLineTextBox",
            new MultiLineTextBox()
                .Height(120)
                .Text("The quick brown fox jumps over the lazy dog.\n\n- Wrap supported\n- Selection supported\n- Scroll supported")
        ),

        Card("ToolTip / ContextMenu",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new Label()
                        .Text("Hover to show a tooltip. Right-click to open a context menu.")
                        .FontSize(11),

                    new Button()
                        .Content("Hover / Right-click me")
                        .ToolTip("ToolTip text")
                        .ContextMenu(
                            new ContextMenu()
                                .Item("Copy", "Ctrl+C")
                                .Item("Paste", "Ctrl+V")
                                .Separator()
                                .SubMenu("Transform", new ContextMenu()
                                    .Item("Uppercase")
                                    .Item("Lowercase")
                                    .Separator()
                                    .SubMenu("More", new ContextMenu()
                                        .Item("Trim")
                                        .Item("Normalize")
                                        .Item("Sort"))
                                )
                                .SubMenu("View", new ContextMenu()
                                    .Item("Zoom In", "Ctrl++")
                                    .Item("Zoom Out", "Ctrl+-")
                                    .Item("Reset Zoom", "Ctrl+0")
                                )
                                .Separator()
                                .Item("Disabled", isEnabled: false)
                        )
                )
        )
    );
}

FrameworkElement MenusPage()
{
    var fileMenu = new Menu()
        .Item("New", shortcutText: "Ctrl+N")
        .Item("Open...", shortcutText: "Ctrl+O")
        .Item("Save", shortcutText: "Ctrl+S")
        .Item("Save As...")
        .Separator()
        .SubMenu("Export", new Menu()
            .Item("PNG")
            .Item("JPEG")
            .SubMenu("Advanced", new Menu()
                .Item("With metadata")
                .Item("Optimized")
            )
        )
        .Separator()
        .Item("Exit");

    var editMenu = new Menu()
        .Item("Undo", shortcutText: "Ctrl+Z")
        .Item("Redo", shortcutText: "Ctrl+Y")
        .Separator()
        .Item("Cut", shortcutText: "Ctrl+X")
        .Item("Copy", shortcutText: "Ctrl+C")
        .Item("Paste", shortcutText: "Ctrl+V")
        .Separator()
        .SubMenu("Find", new Menu()
            .Item("Find...", shortcutText: "Ctrl+F")
            .Item("Find Next", shortcutText: "F3")
            .Item("Replace...", shortcutText: "Ctrl+H")
        );

    var viewMenu = new Menu()
        .Item("Toggle Sidebar")
        .SubMenu("Zoom", new Menu()
            .Item("Zoom In", shortcutText: "Ctrl++")
            .Item("Zoom Out", shortcutText: "Ctrl+-")
            .Item("Reset", shortcutText: "Ctrl+0")
        );

    return CardGrid(
        Card("MenuBar (Multi-depth)",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new MenuBar()
                        .Height(28)
                        .Items(
                            new MenuItem("File").Menu(fileMenu),
                            new MenuItem("Edit").Menu(editMenu),
                            new MenuItem("View").Menu(viewMenu)
                        ),
                    new Label()
                        .FontSize(11)
                        .Text("Hover to switch menus while a popup is open. Submenus supported.")
                ),
            minWidth: 520
        )
    );
}

FrameworkElement SelectionPage()
{
    return CardGrid(
        Card("CheckBox",
            new Grid()
                .Columns("Auto,Auto")
                .Rows("Auto,Auto,Auto")
                .Spacing(8)
                .Children(
                    new CheckBox().Text("CheckBox"),
                    new CheckBox().Text("Disabled").Disable(),
                    new CheckBox().Text("Checked").IsChecked(true),
                    new CheckBox().Text("Disabled (Checked)").IsChecked(true).Disable(),
                    new CheckBox().Text("Three-state").IsThreeState(true).IsChecked(null),
                    new CheckBox().Text("Disabled (Indeterminate)").IsThreeState(true).IsChecked(null).Disable()
                )
        ),

        Card("RadioButton",
            new Grid()
                .Columns("Auto,Auto")
                .Rows("Auto,Auto")
                .Spacing(8)
                .Children(
                    new RadioButton().Text("A").GroupName("g"),
                    new RadioButton().Text("C (Disabled)").GroupName("g2").Disable(),
                    new RadioButton().Text("B").GroupName("g").IsChecked(true),
                    new RadioButton().Text("Disabled (Checked)").GroupName("g2").IsChecked(true).Disable()
                )
        ),

        Card("TabControl",
            new UniformGrid()
                .Columns(2)
                .Spacing(8)
                .Children(
                    new TabControl()
                        .Height(120)
                        .TabItems(
                            new TabItem().Header("Home").Content(new Label().Text("Home tab content")),
                            new TabItem().Header("Settings").Content(new Label().Text("Settings tab content")),
                            new TabItem().Header("About").Content(new Label().Text("About tab content"))
                        ),

                    new TabControl()
                        .Height(120)
                        .Disable()
                        .TabItems(
                            new TabItem().Header("Home").Content(new Label().Text("Home tab content")),
                            new TabItem().Header("Settings").Content(new Label().Text("Settings tab content")),
                            new TabItem().Header("About").Content(new Label().Text("About tab content"))
                        )
                )
        )
    );
}

FrameworkElement ListsPage()
{
    var items = Enumerable.Range(1, 20).Select(i => $"Item {i}").ToArray();

    return CardGrid(
        Card("ListBox",
            new ListBox()
                .Height(120)
                .Width(200)
                .Items(items)
        ),

        Card("ComboBox",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new ComboBox()
                        .Items(["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa"])
                        .SelectedIndex(1),

                    new ComboBox()
                        .Placeholder("Select an item...")
                        .Items(items),

                    new ComboBox()
                        .Items(items)
                        .SelectedIndex(1)
                        .Disable()
                )
        )
    );
}

FrameworkElement LayoutPage()
{
    return CardGrid(
        Card("GroupBox",
            new GroupBox()
                .Header("Header")
                .Content(
                    new StackPanel()
                        .Vertical()
                        .Spacing(6)
                        .Padding(12)
                        .Children(
                            new Label().Text("GroupBox content"),
                            new Button().Content("Action")
                        )
                )
        ),

        Card("Border + Alignment",
            new Border()
                .Height(120)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .BorderThickness(1)
                .CornerRadius(12)
                .Child(new Label()
                        .Text("Centered Text")
                        .Center()
                        .Bold())
        ),

        Card("ScrollViewer",
            new ScrollViewer()
                .Height(120)
                .Width(200)
                .VerticalScroll(ScrollMode.Auto)
                .HorizontalScroll(ScrollMode.Auto)
                .Content(
                    new StackPanel()
                        .Vertical()
                        .Spacing(6)
                        .Children(Enumerable.Range(1, 15).Select(i => new Label().Text($"Line {i} - The quick brown fox jumps over the lazy dog.")).ToArray())
                )
        )
    );
}

FrameworkElement MediaPage()
{
    return CardGrid(
        Card("Image",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new Image()
                        .Source(april)
                        .Width(220)
                        .Height(120)
                        .StretchMode(ImageStretch.Uniform)
                        .Center(),
                    new Label().Text("april.jpg")
                        .FontSize(11)
                        .Center()
                )
        ),

        Card("Image ViewBox",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new WrapPanel()
                        .Orientation(Orientation.Horizontal)
                        .Spacing(8)
                        .ItemWidth(140)
                        .ItemHeight(90)
                        .Children(
                            new Image()
                                .Source(april)
                                .StretchMode(ImageStretch.Uniform)
                                .ImageScaleQuality(ImageScaleQuality.HighQuality),

                            new Image()
                                .Source(april)
                                .ViewBoxRelative(new Rect(0.25, 0.25, 0.5, 0.5))
                                .StretchMode(ImageStretch.UniformToFill)
                                .ImageScaleQuality(ImageScaleQuality.HighQuality)
                        ),

                    new Label()
                        .Text("Left: full image (Uniform). Right: ViewBox (center 50%) + UniformToFill.")
                        .FontSize(11)
                )
        )
    );
}

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
