using System.Diagnostics;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

var stopwatch = Stopwatch.StartNew();

Startup(Environment.GetCommandLineArgs(), out var isBench, out var isSmoke);

double loadedMs = -1;
double firstFrameMs = -1;
var metricsText = new ObservableValue<string>("Metrics:");
var fpsText = new ObservableValue<string>("FPS: -");
var metricsTimer = new DispatcherTimer(TimeSpan.FromSeconds(2));
metricsTimer.Tick += () => UpdateMetrics(appendLog: false);
var maxFpsEnabled = new ObservableValue<bool>(false);
var fpsStopwatch = new Stopwatch();
var fpsFrames = 0;

Window window;
var accentSwatches = new List<(Accent accent, Button button)>();
var currentAccent = ThemeManager.DefaultAccent;
Label title = null!;
var vm = new DemoViewModel();

var logo = ImageSource.FromFile("logo-256.png");
var april = ImageSource.FromFile("april.jpg");

var root = new Window()
    .Ref(out window)
    .Padding(0)
    .Title("Aprillz.MewUI Demo")
    .Resizable(744, 700)
    .OnLoaded(() =>
    {
        loadedMs = stopwatch.Elapsed.TotalMilliseconds;
        UpdateAccentSwatches();
    })
    .OnClosed(() =>
    {
        maxFpsEnabled.Value = false;
        metricsTimer?.Dispose();
    })
    .Content(
        new DockPanel()
            .LastChildFill()
            .Children(
                MenuDemo()
                .DockTop(),

                new DockPanel()
                    .Padding(16)
                    .Spacing(16)
                    .Children(
                        TopSection()
                            .DockTop(),

                        Buttons()
                            .DockBottom(),

                        new TabControl()
                            .VerticalScroll(ScrollMode.Auto)
                            .TabItems(
                                new TabItem()
                                    .Header("Controls")
                                    .Content(
                                        NormalControls()
                                    ),

                                new TabItem()
                                    .Header("Binding")
                                    .Content(
                                        BindSamples()
                                    ),

                                new TabItem()
                                    .Header("Commanding")
                                    .Content(
                                        CommandingSamples()
                                    )
                            )
                    )
            )
    )
    .OnThemeChanged((_, _) => UpdateAccentSwatches())
    .OnFirstFrameRendered(() =>
    {
        ProcessMetric();
        metricsTimer.Start();
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
        double elapsed = fpsStopwatch.Elapsed.TotalSeconds;
        if (elapsed >= 1.0)
        {
            fpsText.Value = $"FPS: {fpsFrames / elapsed:0.0}";
            fpsFrames = 0;
            fpsStopwatch.Restart();
        }
    });

using (var rs = typeof(Program).Assembly.GetManifestResourceStream("Aprillz.MewUI.Sample.appicon.ico")!)
{
    root.Icon = IconSource.FromStream(rs);
}
Application.Run(root);

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

Element HeaderSection() => new StackPanel()
    .Vertical()
    .Spacing(8)
    .Children(
        new Label()
            .Ref(out title)
            .Text("Aprillz.MewUI Demo")
            .WithTheme((t, c) => c.Foreground(t.Palette.Accent))
            .FontSize(20)
            .Bold(),

        new Label()
            .BindText(metricsText),

        new Label()
            .BindText(fpsText)
    );

Element TopSection() => new DockPanel()
    .Children(
        ImageDemo()
            .DockRight(),

        new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                HeaderSection(),
                ThemeControls(),
                AccentPicker()
            )
    );

Element MenuDemo()
{
    var fileMenu = new Menu()
        .Item("New", () => MessageBox.Show(window.Handle, "New", "Menu"))
        .Item("Open...", () => MessageBox.Show(window.Handle, "Open", "Menu"))
        .Separator()
        .Item("Exit", () => Application.Quit());

    var deepMenu = new Menu()
        .Item("Deep A", () => MessageBox.Show(window.Handle, "Deep A", "Menu"))
        .Item("Deep B", () => MessageBox.Show(window.Handle, "Deep B", "Menu"));

    var recentMenu = new Menu()
        .Apply(x =>
        {
            for (char letter = 'a'; letter <= 'z'; letter++)
            {
                var text = letter + ".txt";
                x.Item(text, () => MessageBox.Show(window.Handle, text, "Recent"));
            }
        })
        .Separator()
        .SubMenu("More...", deepMenu);

    var editMenu = new Menu()
        .SubMenu("Recent", recentMenu)
        .Separator()
        .Item("Copy", () => { }, shortcutText: "Ctrl+C")
        .Item("Paste", () => { }, shortcutText: "Ctrl+V");

    var helpAboutMenu = new Menu()
        .Item("About", () => MessageBox.Show(window.Handle, "Aprillz.MewUI", "About"));

    var helpDocsMenu = new Menu()
        .Item("Docs", () => MessageBox.Show(window.Handle, "docs/", "Help"))
        .Item("Korean Docs", () => MessageBox.Show(window.Handle, "ko/docs/", "Help"));

    var helpMenu = new Menu()
        .SubMenu("Documentation", helpDocsMenu)
        .Separator()
        .SubMenu("About", helpAboutMenu);

    return new MenuBar()
        .Items(
            new MenuItem("File").Menu(fileMenu),
            new MenuItem("Edit").Menu(editMenu),
            new MenuItem("Help").Menu(helpMenu)
        );
}

Element ThemeControls()
{
    const string group = "ThemeMode";

    return new StackPanel()
        .Horizontal()
        .Spacing(12)
        .Children(
            new RadioButton()
                .Text("System")
                .GroupName(group)
                .IsChecked()
                .OnChecked(() => Application.Current.SetThemeMode(ThemeVariant.System)),

            new RadioButton()
                .Text("Light")
                .GroupName(group)
                .OnChecked(() => Application.Current.SetThemeMode(ThemeVariant.Light)),

            new RadioButton()
                .Text("Dark")
                .GroupName(group)
                .OnChecked(() => Application.Current.SetThemeMode(ThemeVariant.Dark)),

            new CheckBox()
                .Text("Max FPS")
                .BindIsChecked(maxFpsEnabled)
                .OnCheckedChanged(_ => EnsureMaxFpsLoop())
                .CenterVertical(),
            new Label()
                .Text("Theme: Light")
                .WithTheme((t, c) =>
                {
                    c.Text($"Theme: {t.Name}");
                    UpdateAccentSwatches();
                }, false)
                .CenterVertical()
    );
}

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
            .Children(BuiltInAccent.Accents.Select(AccentSwatch).ToArray())
    );

Button AccentSwatch(Accent accent) =>
    new Button()
        .Content(string.Empty)
        .WithTheme((t, c) => c.Background(accent.GetAccentColor(t.IsDark)))
        .ToolTip(accent.ToString())
        .OnClick(() => ApplyAccent(accent))
        .Apply(b => accentSwatches.Add((accent, b)));

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
            .Content("Quit")
            .Width(80)
            .OnClick(() => Application.Quit())
    );

Element NormalControls()
{
    MultiLineTextBox notesTextBox = null!;
    CheckBox wrapCheck = null!;
    var demoMenu = new ContextMenu();
    var nestedMenu = new ContextMenu()
        .Item("Option 1", () => MessageBox.Show(window.Handle, "Option 1", "Nested ContextMenu"))
        .Item("Option 2", () => MessageBox.Show(window.Handle, "Option 2", "Nested ContextMenu"));

    var deepMenu = new ContextMenu()
        .Item("Deep A", () => MessageBox.Show(window.Handle, "Deep A", "Nested ContextMenu"))
        .Item("Deep B", () => MessageBox.Show(window.Handle, "Deep B", "Nested ContextMenu"));

    nestedMenu.SubMenu("More...", deepMenu);

    demoMenu
        .Item("Item 1")
        .Item("Item 2")
        .Separator()
        .Item("Say hello", () => MessageBox.Show(window.Handle, "Hello from ContextMenu!", "ContextMenu"))
        .Separator()
        .SubMenu("Nested", nestedMenu)
        .Separator()
        .Item("Disabled item", () => { }, isEnabled: false);

    return new StackPanel()
        .Spacing(16)
        .Children(
            new GroupBox()
                .Header("ToolTip / ContextMenu")
                .Content(
                    new StackPanel()
                        .Vertical()
                        .Spacing(8)
                        .Children(
                            new Label()
                                .Text("Hover to show a tooltip. Right-click to open a context menu."),

                            new Button()
                                .Content("Hover / Right-click me")
                                .ToolTip("ToolTip: shown via Window internal popup overlay.")
                                .ContextMenu(demoMenu)
                        )),

            new Grid()
                .Columns("Auto,*,Auto,2*")
                .Spacing(8)
                .Children(
                    new Label()
                        .Text("Your name:")
                        .Column(0)
                        .CenterVertical(),

                    new TextBox()
                        .Placeholder("Type your name")
                        .Column(1),

                    new Label()
                        .CenterVertical()
                        .Text("Buttons:"),

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
                                    )
                                    .Show()),

                            new Button()
                                .Content("Disabled")
                                .Disable(),

                            new Button()
                                .Content("Async")
                                .OnClick(async () =>
                                {
                                    vm.AsyncStatus.Value = "Async: running...";
                                    await Task.Delay(750);
                                    vm.AsyncStatus.Value = $"Async: done @ {DateTime.Now:HH:mm:ss}";
                                }),

                            new Label()
                                .CenterVertical()
                                .BindText(vm.AsyncStatus)
                        )
                ),

            new TabControl()
                .Height(160)
                .TabItems(
                    new TabItem()
                        .Header(
                            new StackPanel()
                                .Horizontal()
                                .Spacing(8)
                                .Children(
                                    new Image()
                                        .Source(logo)
                                        .ImageScaleQuality(ImageScaleQuality.HighQuality)
                                        .StretchMode(ImageStretch.Uniform)
                                        .Size(16, 16),

                                    new Label()
                                        .Text("Home")
                                ))
                        .Content(
                            new StackPanel()
                                .Vertical()
                                .Spacing(8)
                                .Padding(4)
                                .Children(
                                    new Label().Text("This is the Home tab (rich header: StackPanel + labels)."),
                                    new Button().Content("Action").Width(120),

                                    new UniformGrid()
                                        .Spacing(8)
                                        .Columns(3)
                                        .Children(
                                            new Button()
                                                .Content("Open File...")
                                                .OnClick(() =>
                                                {
                                                    var file = FileDialog.OpenFile(new OpenFileDialogOptions
                                                    {
                                                        Owner = window.Handle,
                                                        Filter = "All Files (*.*)|*.*"
                                                    });

                                                    if (file is not null)
                                                    {
                                                        MessageBox.Show(window.Handle, file, "Open File");
                                                    }
                                                }),

                                            new Button()
                                                .Content("Save File...")
                                                .OnClick(() =>
                                                {
                                                    var file = FileDialog.SaveFile(new SaveFileDialogOptions
                                                    {
                                                        Owner = window.Handle,
                                                        Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
                                                        FileName = "demo.txt"
                                                    });

                                                    if (file is not null)
                                                    {
                                                        MessageBox.Show(window.Handle, file, "Save File");
                                                    }
                                                }),

                                            new Button()
                                                .Content("Select Folder...")
                                                .OnClick(() =>
                                                {
                                                    var folder = FileDialog.SelectFolder(new FolderDialogOptions
                                                    {
                                                        Owner = window.Handle
                                                    });

                                                    if (folder is not null)
                                                    {
                                                        MessageBox.Show(window.Handle, folder, "Select Folder");
                                                    }
                                                })
                                        )
                                )),

                    new TabItem()
                        .Header("Settings")
                        .Content(
                            new StackPanel()
                                .Vertical()
                                .Spacing(8)
                                .Padding(4)
                                .Children(
                                    new CheckBox().Text("Enable feature"),
                                    new Slider().Minimum(0).Maximum(100).Value(25)
                                )),

                    new TabItem()
                        .Header("About")
                        .Content(
                            new Label()
                                .Text("TabControl is minimal + code-first (NativeAOT-friendly).")
                                .Padding(4))
                ),

            new Grid()
                .Columns("Auto,*")
                .Spacing(8)
                .Children(
                    new Label()
                        .CenterVertical()
                        .Text("ComboBox:"),

                    new ComboBox()
                        .Items("Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa")
                        .SelectedIndex(1)
                        .Placeholder("Select...")
                ),

            new Grid()
                .Rows("Auto,Auto")
                .Columns("*,*")
                .Spacing(16)
                .Children(
                    new GroupBox()
                        .Header("Options")
                        .Content(
                            new StackPanel()
                                .Vertical()
                                .Spacing(8)
                                .Children(
                                    new CheckBox()
                                        .Text("Enable feature"),

                                    new CheckBox()
                                        .Text("Three-state (Indeterminate)")
                                        .IsThreeState(true)
                                        .IsChecked(null),
                                            new StackPanel()
                                                .Horizontal()
                                                .Spacing(8)
                                                .Children(
                                                    new Label()
                                                        .Text("GroupName: group1")
                                                        .CenterVertical(),

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
                                                    new Label().Text("GroupName: group2")
                                                        .CenterVertical(),

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
                                                    new Label().Text("GroupName: (parent-scope)")
                                                        .CenterVertical(),

                                                    new RadioButton()
                                                        .Text("X")
                                                        .IsChecked(true),

                                                    new RadioButton()
                                                        .Text("Y")
                                                )
                                        )
                                ),

                    new GroupBox()
                        .Header("MultiLineTextBox")
                        .RowSpan(2)
                        .Content(
                            new DockPanel()
                                .Spacing(8)
                                .Children(
                                    new CheckBox()
                                        .DockBottom()
                                        .Ref(out wrapCheck)
                                        .IsChecked(true)
                                        .Text("Wrap")
                                        .OnCheckedChanged(x => notesTextBox.Wrap = x),

                                    new MultiLineTextBox()
                                        .Ref(out notesTextBox)
                                        .OnWrapChanged(x => wrapCheck?.IsChecked = x)
                                        .Wrap(true)
                                        .FontFamily("Consolas")
                                        .Placeholder("Type multi-line text (wheel scroll + thin scrollbar).")
                                        .Text("Line 1\nLine 2\nLine 3\nLine 4\nLine 5\nLine 6\nLine 7")
                                )
                    ),

                    new DockPanel()
                        .Spacing(8)
                        .Children(
                            new Label()
                                .Text("ListBox")
                                .DockTop()
                                .CenterVertical(),

                            new ListBox()
                                .Items("First", "Second", "Third", "Fourth", "Fifth", "Sixth", "Seventh", "Eighth", "Ninth", "Tenth")
                                .SelectedIndex(1)
                                .Height(96)
                        )
                )
        );
}

FrameworkElement ImageDemo() => new UniformGrid()
    .Columns(2)
    .Spacing(8)
    .Children(
        new Image()
            .ImageScaleQuality(ImageScaleQuality.HighQuality)
            .Source(april)
            .Size(96, 96)
            .StretchMode(ImageStretch.Uniform),

        new Image()
            .ImageScaleQuality(ImageScaleQuality.HighQuality)
            .SourceFile("logo-256.png")
            .Size(96, 96)
            .StretchMode(ImageStretch.Uniform)
    );

FrameworkElement CommandingSamples() => new StackPanel()
    .Vertical()
    .Spacing(16)
    .Children(
        new Label()
            .Text("Commanding Demo")
            .Bold()
            .FontSize(14),

        new Label()
            .Text("Delegate-based commanding (Action + Func<bool>) for Native AOT compatibility."),

        // Example 1: Basic CanExecute based on text input
        new GroupBox()
            .Header("CanExecute with Input Validation")
            .Content(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Label()
                            .Text("Enter text to enable the Submit button:"),

                        new TextBox()
                            .Placeholder("Type something...")
                            .BindText(vm.InputText),

                        new Button()
                            .Content("Submit")
                            .OnCanClick(() => !string.IsNullOrWhiteSpace(vm.InputText.Value))
                            .OnClick(() => { vm.CommandLog.Value = $"Submitted: \"{vm.InputText.Value}\" at {DateTime.Now:HH:mm:ss}"; })
                    )
            ),

        // Example 2: Counter with bounds
        new GroupBox()
            .Header("Counter with Min/Max Bounds")
            .Content(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Label()
                            .BindText(vm.Counter, c => $"Count: {c} (range: 0-10)"),

                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new Button()
                                    .Content("- Decrement")
                                    .Width(100)
                                    .OnCanClick(() => vm.Counter.Value > 0)
                                    .OnClick(() => { vm.Counter.Value--; vm.CommandLog.Value = $"Decremented to {vm.Counter.Value}"; }),

                                new Button()
                                    .Content("+ Increment")
                                    .Width(100)
                                    .OnCanClick(() => vm.Counter.Value < 10)
                                    .OnClick(() => { vm.Counter.Value++; vm.CommandLog.Value = $"Incremented to {vm.Counter.Value}"; }),

                                new Button()
                                    .Content("Reset")
                                    .Width(80)
                                    .OnCanClick(() => vm.Counter.Value != 5)
                                    .OnClick(() => { vm.Counter.Value = 5; vm.CommandLog.Value = "Reset to 5"; })
                            )
                    )
            ),

        // Example 3: Feature toggle affecting multiple commands
        new GroupBox()
            .Header("Feature Toggle (Multiple Commands)")
            .Content(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new CheckBox()
                            .Text("Enable Premium Features")
                            .BindIsChecked(vm.IsFeatureEnabled),

                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new Button()
                                    .Content("Export PDF")
                                    .OnCanClick(() => vm.IsFeatureEnabled.Value)
                                    .OnClick(() => { vm.CommandLog.Value = "Exporting PDF..."; }),

                                new Button()
                                    .Content("Cloud Sync")
                                    .OnCanClick(() => vm.IsFeatureEnabled.Value)
                                    .OnClick(() => { vm.CommandLog.Value = "Syncing to cloud..."; }),

                                new Button()
                                    .Content("Analytics")
                                    .OnCanClick(() => vm.IsFeatureEnabled.Value)
                                    .OnClick(() => { vm.CommandLog.Value = "Opening analytics..."; })
                            ),

                        new Label()
                            .Text("(Enable the checkbox above to unlock these features)")
                            .FontSize(11)
                    )
            ),

        // Example 4: Combined conditions
        new GroupBox()
            .Header("Combined Conditions")
            .Content(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Label()
                            .Text("Button enabled when: text is entered AND feature is enabled AND counter > 0"),

                        new Button()
                            .Content("Execute Complex Action")
                            .OnCanClick(() =>
                                !string.IsNullOrWhiteSpace(vm.InputText.Value) &&
                                vm.IsFeatureEnabled.Value &&
                                vm.Counter.Value > 0)
                            .OnClick(() => { vm.CommandLog.Value = $"Complex action: text=\"{vm.InputText.Value}\", count={vm.Counter.Value}"; })
                    )
            ),

        // Command log output
        new GroupBox()
            .Header("Command Log")
            .Content(
                new Label()
                    .BindText(vm.CommandLog)
                    .FontFamily("Consolas")
            )
    );

FrameworkElement BindSamples()
{
    var selectionItems = new List<string> { "Alpha", "Beta", "Gamma", "Delta" };

    return new StackPanel()
        .Vertical()
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
                    .BindText(vm.Percent, v => $"Percent ({Math.Round(v):0}%)"),

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
                    .Text("Name:"),

                new UniformGrid()
                    .Columns(2)
                    .Spacing(8)
                    .Children(
                        new TextBox()
                            .Width(100)
                            .BindText(vm.Name),

                        new Label()
                            .BindText(vm.Name)
                            .CenterVertical()
                    ),

                new Label()
                    .Text("Enabled:"),

                new StackPanel()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new CheckBox()
                            .Text("Enabled")
                            .BindIsChecked(vm.IsEnabled),

                        new Button()
                            .BindContent(vm.IsEnabled, x => x ? "Enabled action" : "Disabled action")
                            .BindIsEnabled(vm.IsEnabled)
                            .OnClick(() => MessageBox.Show(window.Handle, "Enabled button clicked", "Aprillz.MewUI Demo", MessageBoxButtons.Ok, MessageBoxIcon.Information))
                    ),

                new Label()
                    .Text("Selection:"),

                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new ListBox()
                            .Ref(out var selectionListBox)
                            .Height(120)
                            .ItemsSource(ItemsSource.Create(selectionItems))
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
                                        selectionItems.EnsureCapacity(selectionItems.Count + repeat * 4);

                                        for (int i = 0; i < repeat; i++)
                                        {
                                            selectionItems.Add("Alpha");
                                            selectionItems.Add("Beta");
                                            selectionItems.Add("Gamma");
                                            selectionItems.Add("Delta");
                                        }

                                        selectionListBox.InvalidateMeasure();
                                        vm.SelectionItemCount.Value = selectionItems.Count;
                                    }),

                                new Label()
                                    .BindText(vm.SelectionItemCount, c => $"Items: {c:N0}")
                            )
                    )

                )
    );
}

void UpdateAccentSwatches()
{
    var theme = Application.Current.Theme;
    foreach (var (accent, button) in accentSwatches)
    {
        button.Background = accent.GetAccentColor(theme.IsDark);
        bool selected = currentAccent == accent;
        button.BorderThickness = selected ? 2 : 1;
    }
    title.Foreground = Application.Current.Theme.Palette.Accent;
}

void ApplyAccent(Accent accent)
{
    currentAccent = accent;
    Application.Current.SetAccent(accent);

    UpdateAccentSwatches();
}

void UpdateMetrics(bool appendLog, bool captureFirstFrame = false)
{
    if (captureFirstFrame && firstFrameMs < 0)
    {
        firstFrameMs = stopwatch.Elapsed.TotalMilliseconds;
    }

    using var p = Process.GetCurrentProcess();
    p.Refresh();

    double wsMb = p.WorkingSet64 / (1024.0 * 1024.0);
    double pmMb = p.PrivateMemorySize64 / (1024.0 * 1024.0);

    var loadedText = loadedMs >= 0 ? $"{loadedMs:0} ms" : "n/a";
    var firstText = firstFrameMs >= 0 ? $"{firstFrameMs:0} ms" : "n/a";
    metricsText.Value = $"Metrics ({Application.Current.GraphicsFactory.Backend}): Loaded {loadedText}, FirstFrame {firstText}, WS {wsMb:0.0} MB, Private {pmMb:0.0} MB";

    if (appendLog)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "metrics.log");
        File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {metricsText.Value}{Environment.NewLine}");
    }
}

void ProcessMetric()
{
    UpdateMetrics(appendLog: true, captureFirstFrame: true);

    if (isBench)
    {
        Application.Quit();
    }

    if (!isSmoke)
    {
        return;
    }

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

    Application.DispatcherUnhandledException += e =>
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

    public ObservableValue<int> SelectedIndex { get; } = new(1, v => Math.Max(-1, v));

    public ObservableValue<string> AsyncStatus { get; } = new("Async: idle");

    public ObservableValue<string> CommandLog { get; } = new ObservableValue<string>("Command log:");

    public ObservableValue<string> InputText { get; } = new ObservableValue<string>(string.Empty);

    public ObservableValue<int> Counter { get; } = new ObservableValue<int>(0);

    public ObservableValue<bool> IsFeatureEnabled { get; } = new ObservableValue<bool>(false);

    public ObservableValue<int> SelectionItemCount { get; } = new ObservableValue<int>(4);
}
