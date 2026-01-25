using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

var stopwatch = Stopwatch.StartNew();
Startup();

Window window = null!;
Label backendText = null!;
Label themeText = null!;

var currentAccent = Theme.DefaultAccent;

var logo = ImageSource.FromFile("logo_h-1280.png");
var april = ImageSource.FromFile("april.jpg");

var theme = Theme.Light;

var root = new Window()
    .Resizable(1080, 840)
    .Ref(out window)
    .Title("Aprillz.MewUI Gallery")
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
    .OnLoaded(() =>
    {
        UpdateTopBar();
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
        .Apply(b => b.Child =
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
                                        .FontSize(11)
                                )
                        )
                        .DockLeft(),

                    new StackPanel()
                        .Horizontal()
                        .CenterVertical()
                        .Spacing(8)
                        .Children(
                            new Button()
                                .Content("Toggle Theme")
                                .CenterVertical()
                                .OnClick(() =>
                                {
                                    var nextBase = Palette.IsDarkBackground(theme.Palette.WindowBackground) ? Theme.Light : Theme.Dark;
                                    Application.Current.Theme = theme = nextBase.WithAccent(currentAccent);
                                    UpdateTopBar();
                                }),

                            new Label()
                                .Ref(out themeText)
                                .FontSize(11)
                                .CenterVertical(),

                            AccentPicker()
                        )
                        .DockRight()
                ));
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
        .Background(theme.GetAccentColor(accent))
        .ToolTip(accent.ToString())
        .OnClick(() =>
        {
            currentAccent = accent;
            Application.Current.Theme = theme = theme.WithAccent(accent);
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
        .Apply(b =>
        {
            b.CornerRadius = 10;
            b.Child = new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(header, content);
        });
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
                                .Item("Disabled", isEnabled: false)
                        )
                )
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
                            new TabItem().Header("Home").Content(new Label().Text("Home tab content").Padding(12)),
                            new TabItem().Header("Settings").Content(new Label().Text("Settings tab content").Padding(12)),
                            new TabItem().Header("About").Content(new Label().Text("About tab content").Padding(12))
                        ),

                    new TabControl()
                        .Height(120)
                        .Disable()
                        .TabItems(
                            new TabItem().Header("Home").Content(new Label().Text("Home tab content").Padding(12)),
                            new TabItem().Header("Settings").Content(new Label().Text("Settings tab content").Padding(12)),
                            new TabItem().Header("About").Content(new Label().Text("About tab content").Padding(12))
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
                .Apply(b =>
                {
                    b.CornerRadius = 12;
                    b.Child = new Label()
                        .Text("Centered Text")
                        .Center()
                        .Bold();
                })
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
                    new Image().Source(april).Width(220).Height(120).StretchMode(ImageStretch.Uniform),
                    new Label().Text("april.jpg").FontSize(11).Center()
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
    themeText.Text($"Theme: {theme.Name}");
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

    Application.UiUnhandledException += (_, e) =>
    {
        try
        {
            MessageBox.Show(0, e.Exception.ToString(), "Unhandled UI exception");
        }
        catch
        {
            // ignore
        }
        e.Handled = true;
    };
}
