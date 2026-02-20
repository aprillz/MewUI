using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

#if DEBUG
[assembly: System.Reflection.Metadata.MetadataUpdateHandler(typeof(Aprillz.MewUI.HotReload.MewUiMetadataUpdateHandler))]
#endif

var stopwatch = Stopwatch.StartNew();
Startup();

Window window = null!;
Label backendText = null!;
Label colorHexLabel = null!;
Label themeText = null!;
Image peekImage = null!;
var fpsText = new ObservableValue<string>("FPS: -");
var imagePeekText = new ObservableValue<string>("Color: -");
var fpsStopwatch = new Stopwatch();
var fpsFrames = 0;
var maxFpsEnabled = new ObservableValue<bool>(false);

var currentAccent = ThemeManager.DefaultAccent;

var app = Application
    .Create()
    //.UseMetrics(ThemeMetrics.Default with { ControlCornerRadius = 10, ControlBorderThickness = 2 })
    .UseAccent(Accent.Purple);

var logo = ImageSource.FromFile("logo_h-1280.png");
var april = ImageSource.FromFile("april.jpg");
var iconFolderOpen = ImageSource.FromResource<Program>("Aprillz.MewUI.Gallery.Resources.folder-horizontal-open.png");
var iconFolderClose = ImageSource.FromResource<Program>("Aprillz.MewUI.Gallery.Resources.folder-horizontal.png");
var iconFile = ImageSource.FromResource<Program>("Aprillz.MewUI.Gallery.Resources.document.png");

var timer = new DispatcherTimer().Interval(TimeSpan.FromSeconds(1)).OnTick(() => CheckFPS(ref fpsFrames));
var name = new ObservableValue<string>("Type your name");
var intBinding = new ObservableValue<int>(1);
var doubleBinding = new ObservableValue<double>(42.5);

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
                    GalleryRoot()
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

void EnableWindowDrag(Window window, UIElement element)
{
    ArgumentNullException.ThrowIfNull(element);

    bool dragging = false;
    Point dragStartScreenDip = default;
    Point windowStartDip = default;

    element.MouseDown += e =>
    {
        if (e.Button != MouseButton.Left)
        {
            return;
        }

        var local = e.GetPosition(element);
        if (local.X < 0 || local.Y < 0 || local.X >= element.RenderSize.Width || local.Y >= element.RenderSize.Height)
        {
            if (element.IsMouseCaptured)
            {
                window.ReleaseMouseCapture();
            }
            return;
        }

        dragging = true;
        dragStartScreenDip = GetScreenDip(window, e);
        windowStartDip = window.Position;

        window.CaptureMouse(element);
        e.Handled = true;
    };

    element.MouseMove += e =>
    {
        if (!dragging)
        {
            return;
        }

        if (!e.LeftButton)
        {
            dragging = false;
            window.ReleaseMouseCapture();
            return;
        }

        var screenDip = GetScreenDip(window, e);
        double dx = screenDip.X - dragStartScreenDip.X;
        double dy = screenDip.Y - dragStartScreenDip.Y;

        window.MoveTo(windowStartDip.X + dx, windowStartDip.Y + dy);

        e.Handled = true;
    };

    element.MouseUp += e =>
    {
        if (e.Button != MouseButton.Left)
        {
            return;
        }

        if (!dragging)
        {
            return;
        }

        dragging = false;
        window.ReleaseMouseCapture();
        e.Handled = true;
    };

    static Point GetScreenDip(Window window, MouseEventArgs e)
    {
        var screen = window.ClientToScreen(e.GetPosition(window));
        if (OperatingSystem.IsWindows())
        {
            var scale = Math.Max(1.0, window.DpiScale);
            return new Point(screen.X / scale, screen.Y / scale);
        }
        return screen;
    }
}

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

FrameworkElement GalleryRoot() => new ScrollViewer()
    .VerticalScroll(ScrollMode.Auto)
    .Padding(8)
    .Content(BuildGalleryContent());

FrameworkElement Card(string title, FrameworkElement content, double minWidth = 320) => new Border()
        .MinWidth(minWidth)
        .Padding(14)
        .BorderThickness(1)
        .CornerRadius(10)
        .Child(
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new Label()
                        .WithTheme((t, c) => c.Foreground(t.Palette.Accent))
                        .Text(title)
                        .Bold(),
                    content
                ));

FrameworkElement CardGrid(params FrameworkElement[] cards) => new WrapPanel()
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
            Section("Window/Menu", WindowsMenuPage()),
            Section("Selection", SelectionPage()),
            Section("Lists", ListsPage()),
            Section("Panels", PanelsPage()),
            Section("Layout", LayoutPage()),
            Section("Media", MediaPage())
        );
}

FrameworkElement ButtonsPage() =>
    CardGrid(
        Card(
            "Buttons",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new Button().Content("Default"),
                    new Button()
                        .Content("Accent")
                        .WithTheme((t, c) => c.Background(t.Palette.Accent).Foreground(t.Palette.AccentText)),
                    new Button().Content("Disabled").Disable(),
                    new Button()
                        .Content("Double Click")
                        .OnDoubleClick(() => MessageBox.Show("Double Click"))
                )
        ),

        Card(
            "Toggle / Switch",
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

        Card(
            "Progress",
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

FrameworkElement WindowsPage()
{
    var dialogStatus = new ObservableValue<string>("Dialog: -");
    var transparentStatus = new ObservableValue<string>("Transparent: -");

    async void ShowDialogSample()
    {
        dialogStatus.Value = "Dialog: opening...";

        var dlg = new Window()
            .Resizable(420, 220)
            .OnBuild(x => x
                .Title("ShowDialog sample")
                .Padding(16)
                .Content(
                    new StackPanel()
                        .Vertical()
                        .Spacing(10)
                        .Children(
                            new Label()
                                .Text("This is a modal window. The owner is disabled until you close this dialog.")
                                .FontSize(12),

                            new StackPanel()
                                .Horizontal()
                                .Spacing(8)
                                .Children(
                                    new Button()
                                        .Content("Open dialog")
                                        .OnClick(ShowDialogSample),
                                    new Button()
                                        .Content("Close")
                                        .OnClick(() => x.Close())
                                )
                        )
                )
            );

        try
        {
            await dlg.ShowDialogAsync(window);
            dialogStatus.Value = "Dialog: closed";
        }
        catch (Exception ex)
        {
            dialogStatus.Value = $"Dialog: error ({ex.GetType().Name})";
        }
    }

    void ShowTransparentSample()
    {
        transparentStatus.Value = "Transparent: opening...";

        Window tw = null!;

        new Window()
            .Ref(out tw)
            .Resizable(520, 400)
            .OnBuild(x =>
            {
                x.Title = "Transparent window sample";
                x.AllowsTransparency = true;
                x.Background = Color.Transparent;
                x.Padding = new Thickness(20);
                x.Content =
                        new Grid()
                            .Children(
                                new Image()
                                    .Source(logo)
                                    .Apply(x => EnableWindowDrag(tw, x))
                                    .Width(500)
                                    .Height(256)
                                    .ImageScaleQuality(ImageScaleQuality.HighQuality)
                                    .StretchMode(ImageStretch.Uniform)
                                    .Bottom(),
                                new Border()
                                    .Padding(16)
                                    .Top()
                                    .WithTheme((t, b) => b.Background(t.Palette.Accent.WithAlpha(32)))
                                    .CornerRadius(10)
                                    .Child(
                                        new StackPanel()
                                            .Vertical()
                                            .Spacing(10)
                                            .Children(

                                                new StackPanel()
                                                    .Vertical()
                                                    .Spacing(6)
                                                    .Children(
                                                        new Label()
                                                            .TextWrapping(TextWrapping.Wrap)
                                                            .Text("Wrapped label followed by a button. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog."),
                                                        new Button()
                                                            .Content("Close")
                                                            .OnClick(() => x.Close())
                                                        )
                                            )
                                    )
                        );
            });

        try
        {
            tw.Show();
            transparentStatus.Value = "Transparent: shown";
        }
        catch (Exception ex)
        {
            transparentStatus.Value = $"Transparent: error ({ex.GetType().Name})";
        }
    }

    return CardGrid(
        Card(
            "ShowDialogAsync",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new Button()
                        .Content("Open dialog")
                        .OnClick(ShowDialogSample),
                    new Label()
                        .BindText(dialogStatus)
                        .FontSize(11)
                )
        ),

        Card(
            "Transparent Window",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new Button()
                        .Content("Open transparent window")
                        .OnClick(ShowTransparentSample),
                    new Label()
                        .BindText(transparentStatus)
                        .FontSize(11)
                )
        )
    );
}

FrameworkElement InputsPage() =>
    CardGrid(
        Card(
            "TextBox",
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

        Card(
            "NumericUpDown (int/double)",
            new Grid()
                .Columns("Auto,Auto,Auto")
                .Rows("Auto,Auto")
                .Spacing(8)
                .AutoIndexing()
                .Children(
                    new Label()
                        .Text("Int")
                        .CenterVertical(),

                    new NumericUpDown()
                        .Width(140)
                        .Minimum(0)
                        .Maximum(100)
                        .Step(1)
                        .Format("0")
                        .BindValue(intBinding)
                        .CenterVertical(),

                    new Label()
                        .BindText(intBinding, value => $"Value: {value}")
                        .CenterVertical(),

                    new Label()
                        .Text("Double")
                        .CenterVertical(),

                    new NumericUpDown()
                        .Width(140)
                        .Minimum(0)
                        .Maximum(100)
                        .Step(0.1)
                        .Format("0.##")
                        .BindValue(doubleBinding)
                        .CenterVertical(),

                    new Label()
                        .BindText(doubleBinding, value => $"Value: {value:0.##}")
                        .CenterVertical()
                )
        ),

        Card(
            "MultiLineTextBox",
            new MultiLineTextBox()
                .Height(120)
                .Text("The quick brown fox jumps over the lazy dog.\n\n- Wrap supported\n- Selection supported\n- Scroll supported")
        ),

        Card(
            "ToolTip / ContextMenu",
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
        Card(
            "MenuBar (Multi-depth)",
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

FrameworkElement WindowsMenuPage() => new WrapPanel()
    .Spacing(12)
    .Children(
        MenusPage(),
        WindowsPage()
    );

FrameworkElement SelectionPage() =>
    CardGrid(
        Card(
            "CheckBox",
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

        Card(
            "RadioButton",
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

        Card(
            "TabControl",
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

FrameworkElement ListsPage()
{
    var items = Enumerable.Range(1, 20).Select(i => $"Item {i}").ToArray();

    var gridItems = Enumerable.Range(1, 64)
        .Select(i => new SimpleGridRow(i, $"Item {i}", (i % 6) switch { 1 => "Warning", 2 => "Error", _ => "Normal" }))
        .ToArray();

    var gridHitText = new ObservableValue<string>("Click: (none)");

    var treeItems = new[]
    {
        new TreeViewNode("src",
        [
            new TreeViewNode("MewUI",
            [
                new TreeViewNode("Controls",
                [
                    new TreeViewNode("Button.cs"),
                    new TreeViewNode("TextBox.cs"),
                    new TreeViewNode("TreeView.cs")
                ])
            ]),
            new TreeViewNode("Rendering",
            [
                new TreeViewNode("Gdi"),
                new TreeViewNode("Direct2D"),
                new TreeViewNode("OpenGL")
            ])
        ]),
        new TreeViewNode("README.md"),
        new TreeViewNode("assets",
        [
            new TreeViewNode("logo.png"),
            new TreeViewNode("icon.ico")
        ])
    };

    Label selectedNodeText = null!;

    var treeView = new TreeView()
        .Width(240)
        .ItemsSource(treeItems)
        .ExpandTrigger(TreeViewExpandTrigger.DoubleClickNode)
        .OnSelectionChanged(obj =>
        {
            var n = obj as TreeViewNode;
            selectedNodeText.Text = n == null ? "Selected: (none)" : $"Selected: {n.Text}";
        });

    treeView.ItemTemplate<TreeViewNode>(
        build: ctx => new StackPanel()
            .Horizontal()
            .Spacing(6)
            .Padding(8, 0)
            .Children(
                new Image()
                    .Register(ctx, "Icon")
                    .Size(16, 16)
                    .StretchMode(ImageStretch.None)
                    .CenterVertical(),
                new Label()
                    .Register(ctx, "Text")
                    .CenterVertical()
            ),
        bind: (view, item, _, ctx) =>
        {
            ctx.Get<Image>("Icon").Source(item.HasChildren ? (treeView.IsExpanded(item) ? iconFolderOpen : iconFolderClose) : iconFile);
            ctx.Get<Label>("Text").Text(item.Text);
        });

    treeView.Expand(treeItems[0]);
    treeView.Expand(treeItems[0].Children[0]);

    return CardGrid(
        Card(
            "ListBox",
            new ListBox()
                .Height(120)
                .Width(200)
                .Items(items)
        ),

        Card(
            "ComboBox",
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
        ),

        Card(
            "TreeView",
            new DockPanel()
                .Height(240)
                .Spacing(6)
                .Children(
                    new Label()
                        .DockBottom()
                        .Ref(out selectedNodeText)
                        .FontSize(11)
                        .Text("Selected: (none)"),
                    treeView
                ),
            minWidth: 280
        ),

        Card(
            "GridView",
            new DockPanel()
                .Height(240)
                .Spacing(6)
                .Children(
                    new Label()
                        .DockBottom()
                        .BindText(gridHitText)
                        .FontSize(11),

                    new GridView()
                        .Height(240)
                        .ItemsSource(gridItems)
                        .Apply(g => g.MouseDown += e =>
                        {
                            if (g.TryGetCellIndexAt(e, out int rowIndex, out int columnIndex, out bool isHeader))
                            {
                                gridHitText.Value = isHeader
                                    ? $"Click: Header  Col={columnIndex}"
                                    : $"Click: Row={rowIndex}  Col={columnIndex}";
                            }
                            else
                            {
                                gridHitText.Value = "Click: (none)";
                            }
                        })
                        .Columns(
                            new GridViewColumn<SimpleGridRow>()
                                .Header("#")
                                .Width(50)
                                .Text(row => row.Id.ToString()),

                            new GridViewColumn<SimpleGridRow>()
                                .Header("Name")
                                .Width(70)
                                .Text(row => row.Name),

                            new GridViewColumn<SimpleGridRow>()
                                .Header("Status")
                                .Width(70)
                                .Template(
                                    build: _ => new Label().Padding(8, 0).CenterVertical(),
                                    bind: (view, row) => view
                                        .Text(row.Status)
                                        .WithTheme((t, c) => c.Foreground(GetColor(t, row.Status)))
                                )
                        )
                )
        ),

        GridViewComplexBindingCard(),
		
        GridViewTemplateExamplesCard()
    );

    Color GetColor(Theme t, string status) => status switch
    {
        "Warning" => Color.Orange,
        "Error" => Color.Red,
        _ => t.Palette.WindowText
    };

    FrameworkElement GridViewComplexBindingCard()
    {
        var query = new ObservableValue<string>(string.Empty);
        var onlyErrors = new ObservableValue<bool>(false);
        var minAmount = new ObservableValue<double>(0);
        var sortKey = new ObservableValue<int>(0); // 0=Id,1=Name,2=Amount,3=Status
        var sortDesc = new ObservableValue<bool>(false);

        var summaryText = new ObservableValue<string>("Rows: -");
        var selectedText = new ObservableValue<string>("Selected: (none)");

        var all = Enumerable.Range(1, 800)
            .Select(i => new ComplexGridRow(
                id: i,
                name: $"User {i:00}",
                amount: Math.Round((i * 13.37) % 100, 2),
                hasError: i % 11 == 0 || i % 17 == 0,
                isActive: i % 9 != 0))
            .ToList();

        GridView grid = null!;

        void ApplyView()
        {
            IEnumerable<ComplexGridRow> rows = all;

            var q = (query.Value ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(q))
            {
                rows = rows.Where(r =>
                    r.Id.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (r.Name ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase));
            }

            if (onlyErrors.Value)
            {
                rows = rows.Where(r => r.HasError.Value);
            }

            rows = rows.Where(r => r.Amount.Value >= minAmount.Value);

            rows = sortKey.Value switch
            {
                1 => sortDesc.Value
                    ? rows.OrderByDescending(r => r.Name, StringComparer.OrdinalIgnoreCase)
                    : rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase),
                2 => sortDesc.Value
                    ? rows.OrderByDescending(r => r.Amount.Value)
                    : rows.OrderBy(r => r.Amount.Value),
                3 => sortDesc.Value
                    ? rows.OrderByDescending(r => r.StatusText.Value, StringComparer.OrdinalIgnoreCase)
                    : rows.OrderBy(r => r.StatusText.Value, StringComparer.OrdinalIgnoreCase),
                _ => sortDesc.Value
                    ? rows.OrderByDescending(r => r.Id)
                    : rows.OrderBy(r => r.Id)
            };

            var view = rows.ToList();
            grid.SetItemsSource(view);

            int errorCount = view.Count(r => r.HasError.Value);
            double sum = view.Sum(r => r.Amount.Value);
            summaryText.Value = $"Rows: {view.Count}/{all.Count}   Errors: {errorCount}   Sum: {sum:0.##}";
        }

        void TriggerApply() => ApplyView();

        query.Changed += TriggerApply;
        onlyErrors.Changed += TriggerApply;
        minAmount.Changed += TriggerApply;
        sortKey.Changed += TriggerApply;
        sortDesc.Changed += TriggerApply;

        foreach (var r in all)
        {
            r.Amount.Changed += TriggerApply;
            r.HasError.Changed += TriggerApply;
            r.IsActive.Changed += TriggerApply;
        }

        grid = new GridView()
            .Height(190)
            .ItemsSource(all)
            .Apply(g => g.SelectionChanged += obj =>
            {
                if (obj is ComplexGridRow row)
                {
                    selectedText.Value = $"Selected: #{row.Id}  {row.Name}  {row.StatusText.Value}";
                }
                else
                {
                    selectedText.Value = "Selected: (none)";
                }
            })
            .Columns(
                new GridViewColumn<ComplexGridRow>()
                    .Header("#")
                    .Width(44)
                    .Text(r => r.Id.ToString()),

                new GridViewColumn<ComplexGridRow>()
                    .Header("Name")
                    .Width(110)
                    .Text(x=>x.Name),

                new GridViewColumn<ComplexGridRow>()
                    .Header("Amount")
                    .Width(110)
                    .Template(
                        build: _ => new NumericUpDown().Padding(6, 0).CenterVertical().Minimum(0).Maximum(100).Step(0.5).Format("0.##"),
                        bind: (view, row) => view.BindValue(row.Amount)
                    ),

                new GridViewColumn<ComplexGridRow>()
                    .Header("Error")
                    .Width(60)
                    .Template(
                        build: _ => new CheckBox().Center(),
                        bind: (view, row) => view.BindIsChecked(row.HasError)
                    ),

                new GridViewColumn<ComplexGridRow>()
                    .Header("Status")
                    .Width(110)
                    .Template(
                        build: _ => new Label().Padding(8, 0).CenterVertical(),
                        bind: (view, row) => view.BindText(row.StatusText)
                    )
            );

        ApplyView();

        return Card(
            "GridView (Complex binding)",
            new DockPanel()
                .Height(240)
                .Spacing(8)
                .Children(
                    new StackPanel()
                        .DockTop()
                        .Horizontal()
                        .Spacing(8)
                        .Children(
                            new TextBox()
                                .Width(120)
                                .Placeholder("Search (id/name)")
                                .BindText(query),

                            new CheckBox()
                                .Text("Errors only")
                                .BindIsChecked(onlyErrors),

                            new Label().Text("Min amount").CenterVertical().FontSize(11),
                            new NumericUpDown()
                                .Width(90)
                                .Minimum(0)
                                .Maximum(100)
                                .Step(1)
                                .Format("0")
                                .BindValue(minAmount),

                            new ComboBox()
                                .Width(80)
                                .Items(["Id", "Name", "Amount", "Status"])
                                .BindSelectedIndex(sortKey),

                            new CheckBox()
                                .Text("Desc")
                                .BindIsChecked(sortDesc)
                        ),

                    new StackPanel()
                        .DockBottom()
                        .Vertical()
                        .Spacing(2)
                        .Children(
                            new Label().BindText(summaryText).FontSize(11),
                            new Label().BindText(selectedText).FontSize(11)
                        ),

                    grid
                ),
            minWidth: 520
        );
    }

    FrameworkElement GridViewTemplateExamplesCard()
    {
        var complexUsers = Enumerable.Range(1, 250)
            .Select(i => new TemplateComplexPersonRow(
                name: $"User {i:0000}",
                roleIndex: i % 3,
                isOnline: i % 5 != 0,
                progress: i % 101,
                score: (i * 7.3) % 100))
            .ToList();

        return Card(
            "GridView (complex cell templates)",
            new DockPanel()
                .Height(240)
                .Spacing(8)
                .Children(                    
                    new Label()
                        .DockTop()
                        .Text("Shows delegate-based complex cell templates (nested layout + multiple bound controls) similar to MewUI.Concept.")
                        .TextWrapping(TextWrapping.Wrap),

                    ComplexCellsGrid()
                        .ItemsSource(complexUsers)
                ),
            minWidth: 640
        );

        GridView ComplexCellsGrid() => new GridView()
            .HeaderHeight(28)
            .RowHeight(44)
            .ZebraStriping()
            .Columns(
                new GridViewColumn<TemplateComplexPersonRow>()
                    .Header("")
                    .Width(36)
                    .Bind(
                        build: _ => new CheckBox().Padding(0).Center(),
                        bind: (view, item) => ((CheckBox)view).BindIsChecked(item.IsSelected)),

                new GridViewColumn<TemplateComplexPersonRow>()
                    .Header("User")
                    .Width(240)
                    .Bind(
                        build: ctx => new StackPanel()
                            .Vertical()
                            .Spacing(2)
                            .Padding(6, 2)
                            .Children(
                                new Label()
                                    .Register(ctx, "Name")
                                    .Bold(),
                                new StackPanel()
                                    .Horizontal()
                                    .Spacing(8)
                                    .Children(
                                        new Label()
                                            .Register(ctx, "Role")
                                            .FontSize(11),
                                        new Label()
                                            .Register(ctx, "Online")
                                            .FontSize(11),
                                        new Label()
                                            .Register(ctx, "Score")
                                            .FontSize(11)
                                    )
                            ),
                        bind: (_, item, _, ctx) =>
                        {
                            ctx.Get<Label>("Name").BindText(item.Name);
                            ctx.Get<Label>("Role").BindText(item.RoleIndex, role => role switch { 1 => "Admin", 2 => "Guest", _ => "User" });
                            ctx.Get<Label>("Online").BindText(item.IsOnline, v => v ? "Online" : "Offline");
                            ctx.Get<Label>("Score").BindText(item.Score, v => $"Score: {v:0.#}");
                        }),

                new GridViewColumn<TemplateComplexPersonRow>()
                    .Header("Role")
                    .Width(120)
                    .Bind(
                        build: _ => new ComboBox()
                            .Items(["User", "Admin", "Guest"])
                            .Padding(6, 0)
                            .CenterVertical(),
                        bind: (view, item) => ((ComboBox)view).BindSelectedIndex(item.RoleIndex)),

                new GridViewColumn<TemplateComplexPersonRow>()
                    .Header("Progress")
                    .Width(140)
                    .Bind(
                        build: _ => new ProgressBar()
                            .Minimum(0)
                            .Maximum(100)
                            .Height(10)
                            .Margin(6, 0)
                            .CenterVertical(),
                        bind: (view, item) => ((ProgressBar)view).BindValue(item.Progress)),

                new GridViewColumn<TemplateComplexPersonRow>()
                    .Header("Online")
                    .Width(80)
                    .Bind(
                        build: _ => new ToggleSwitch().Center(),
                        bind: (view, item) => ((ToggleSwitch)view).BindIsChecked(item.IsOnline))
            );
    }
}


FrameworkElement LayoutPage()
{
    FrameworkElement LabelBox(string title, TextAlignment horizontal, TextAlignment vertical, TextWrapping wrapping)
    {
        const string sample =
            "The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog";

        return new StackPanel()
            .Vertical()
            .Spacing(4)
            .Children(
                new Label()
                    .Text(title)
                    .FontSize(11),
                new Border()
                    .Width(240)
                    .Height(80)
                    .Padding(6)
                    .BorderThickness(1)
                    .CornerRadius(6)
                    .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                    .Child(
                        new Label()
                            .Text(sample)
                            .TextWrapping(wrapping)
                            .TextAlignment(horizontal)
                            .VerticalTextAlignment(vertical)
                    )
            );
    }

    return CardGrid(
        Card(
            "GroupBox",
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

        Card(
            "Border + Alignment",
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

        Card(
            "Label Wrap/Alignment",
            new UniformGrid()
                .Columns(3)
                .Spacing(8)
                .Children(
                    LabelBox("Left/Top + Wrap", TextAlignment.Left, TextAlignment.Top, TextWrapping.Wrap),
                    LabelBox("Center/Top + Wrap", TextAlignment.Center, TextAlignment.Top, TextWrapping.Wrap),
                    LabelBox("Right/Top + Wrap", TextAlignment.Right, TextAlignment.Top, TextWrapping.Wrap),
                    LabelBox("Left/Center + Wrap", TextAlignment.Left, TextAlignment.Center, TextWrapping.Wrap),
                    LabelBox("Left/Bottom + Wrap", TextAlignment.Left, TextAlignment.Bottom, TextWrapping.Wrap),
                    LabelBox("Left/Top + NoWrap", TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap)
                )
        ),

        Card(
            "Border Top + Wrap Growth",
            new Border()
                .Width(260)
                .Top()
                .Padding(8)
                .BorderThickness(1)
                .CornerRadius(8)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .Child(
                    new Label()
                        .TextWrapping(TextWrapping.Wrap)
                        .Text("Top-aligned border should grow with wrapped text. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog.")
                )
        ),

        Card(
            "StackPanel Wrap Growth",
            new Border()
                .Width(260)
                .Top()
                .Padding(8)
                .BorderThickness(1)
                .CornerRadius(8)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .Child(
                    new StackPanel()
                        .Vertical()
                        .Spacing(6)
                        .Children(
                            new Label()
                                .TextWrapping(TextWrapping.Wrap)
                                .Text("First wrapped label. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog."),
                            new Label()
                                .TextWrapping(TextWrapping.Wrap)
                                .Text("Second wrapped label. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog.")
                        )
                )
        ),

        Card(
            "Wrap + Button",
            new Border()
                .Width(260)
                .Top()
                .Padding(8)
                .BorderThickness(1)
                .CornerRadius(8)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .Child(
                    new StackPanel()
                        .Vertical()
                        .Spacing(6)
                        .Children(
                            new Label()
                                .TextWrapping(TextWrapping.Wrap)
                                .Text("Wrapped label followed by a button. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog."),
                            new Button()
                                .Content("After Wrap")
                        )
                )
        ),

        Card(
            "ScrollViewer",
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

FrameworkElement PanelsPage()
{
    Button canvasButton = null!;
    var canvasInfo = new ObservableValue<string>("Pos: 20,20");
    double left = 20;
    double top = 20;

    void MoveCanvasButton()
    {
        left = (left + 24) % 140;
        top = (top + 16) % 70;
        Canvas.SetLeft(canvasButton, left);
        Canvas.SetTop(canvasButton, top);
        canvasInfo.Value = $"Pos: {left:0},{top:0}";
    }

    FrameworkElement PanelCard(string title, FrameworkElement content) =>
        Card(title, new Border()
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .BorderThickness(1)
                .CornerRadius(10)
                .Width(280)
                .Padding(8)
                .Child(content));

    return CardGrid(
        PanelCard(
            "StackPanel",
            new StackPanel()
                .Vertical()
                .Spacing(6)
                .Children(
                    new Button().Content("A"),
                    new Button().Content("B"),
                    new Button().Content("C")
                )
        ),

        PanelCard(
            "DockPanel",
            new DockPanel()
                .Spacing(6)
                .Children(
                    new Button().Content("Left").DockLeft(),
                    new Button().Content("Top").DockTop(),
                    new Button().Content("Bottom").DockBottom(),
                    new Button().Content("Fill")
                )
        ),

        PanelCard(
            "WrapPanel",
            new WrapPanel()
                .Orientation(Orientation.Horizontal)
                .Spacing(6)
                .ItemWidth(60)
                .ItemHeight(28)
                .Children(Enumerable.Range(1, 8).Select(i => new Button().Content($"#{i}")).ToArray())
        ),

        PanelCard(
            "UniformGrid",
            new UniformGrid()
                .Columns(3)
                .Rows(2)
                .Spacing(6)
                .Children(
                    new Button().Content("1"),
                    new Button().Content("2"),
                    new Button().Content("3"),
                    new Button().Content("4"),
                    new Button().Content("5"),
                    new Button().Content("6")
                )
        ),

        PanelCard(
            "Grid (Span)",
            new Grid()
                .Columns("Auto,*,*")
                .Rows("Auto,Auto,Auto")
                .AutoIndexing()
                .Spacing(6)
                .Children(
                    new Button().Content("ColSpan 2")
                        .ColumnSpan(2),

                    new Button().Content("R1C1"),

                    new Button().Content("RowSpan 2")
                        .RowSpan(2),

                    new Button().Content("R1C2"),

                    new Button().Content("R1C2"),

                    new Button().Content("R2C1"),

                    new Button().Content("R2C2")
                )
        ),

        Card(
            "Canvas",
            new StackPanel()
                .Vertical()
                .Spacing(6)
                .Children(
                    new Border()
                        .Height(120)
                        .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                        .BorderThickness(1)
                        .CornerRadius(10)
                        .Child(
                            new Canvas()
                                .Children(
                                    new Button()
                                        .Ref(out canvasButton)
                                        .Content("Move")
                                        .OnClick(MoveCanvasButton)
                                        .CanvasPosition(left, top)
                                )
                        ),

                    new Label()
                        .BindText(canvasInfo)
                        .FontSize(11)
                ),
            minWidth: 320
        )
    );
}

FrameworkElement MediaPage() =>
    CardGrid(
        Card(
            "Image",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new Image()
                        .Source(april)
                        .Width(120)
                        .Height(120)
                        .StretchMode(ImageStretch.Uniform)
                        .Center(),
                    new Label()
                        .Text("april.jpg")
                        .FontSize(11)
                        .Center()
                )
        ),

        Card(
            "Peek Color",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new Image()
                        .Ref(out peekImage)
                        .OnMouseMove(e => imagePeekText.Value = peekImage.TryPeekColor(e.GetPosition(peekImage), out var c)
                            ? $"Color: #{c.ToArgb():X8}"
                            : "Color: #--------")
                        .Source(logo)
                        .ImageScaleQuality(ImageScaleQuality.HighQuality)
                        .Width(200)
                        .Height(120)
                        .StretchMode(ImageStretch.Uniform)
                        .Center(),
                    new Label()
                        .Ref(out colorHexLabel)
                        .BindText(imagePeekText)
                        .FontFamily("Consolas")
                        .Center()
                )
        ),

        Card(
            "Image ViewBox",
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

sealed record IconTextItem(string Icon, string Text);
sealed record SimpleGridRow(int Id, string Name, string Status);

sealed class ComplexGridRow
{
    public ComplexGridRow(int id, string name, double amount, bool hasError, bool isActive)
    {
        Id = id;
        Name = name;
        Amount = new ObservableValue<double>(amount, v => double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Clamp(v, 0, 100));
        HasError = new ObservableValue<bool>(hasError);
        IsActive = new ObservableValue<bool>(isActive);

        StatusText = new ObservableValue<string>(string.Empty);

        void Recompute()
        {
            if (!IsActive.Value)
            {
                StatusText.Value = "Inactive";
                return;
            }

            StatusText.Value = HasError.Value ? "Error" : "OK";
        }

        HasError.Changed += Recompute;
        IsActive.Changed += Recompute;
        Recompute();
    }

    public int Id { get; }
    public string Name { get; }
    public ObservableValue<double> Amount { get; }
    public ObservableValue<bool> HasError { get; }
    public ObservableValue<bool> IsActive { get; }
    public ObservableValue<string> StatusText { get; }
}

sealed class TemplatePerson
{
    public TemplatePerson(string name, bool isOnline, double progress)
    {
        Name = name ?? string.Empty;
        IsChecked = new ObservableValue<bool>(false);
        IsOnline = new ObservableValue<bool>(isOnline);
        Progress = new ObservableValue<double>(progress, v => double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Clamp(v, 0, 100));
    }

    public ObservableValue<bool> IsChecked { get; }
    public string Name { get; }
    public ObservableValue<bool> IsOnline { get; }
    public ObservableValue<double> Progress { get; }
}

sealed class TemplateComplexPersonRow
{
    public TemplateComplexPersonRow(string name, int roleIndex, bool isOnline, double progress, double score)
    {
        Name = new ObservableValue<string>(name ?? string.Empty);
        RoleIndex = new ObservableValue<int>(roleIndex, v => Math.Clamp(v, 0, 2));
        IsOnline = new ObservableValue<bool>(isOnline);
        IsSelected = new ObservableValue<bool>(false);
        Progress = new ObservableValue<double>(progress, v => double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Clamp(v, 0, 100));
        Score = new ObservableValue<double>(score, v => double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Clamp(v, 0, 100));
    }

    public ObservableValue<bool> IsSelected { get; }
    public ObservableValue<string> Name { get; }
    public ObservableValue<int> RoleIndex { get; }
    public ObservableValue<bool> IsOnline { get; }
    public ObservableValue<double> Progress { get; }
    public ObservableValue<double> Score { get; }
}
