#:sdk Microsoft.NET.Sdk
#:property OutputType=WinExe
#:property TargetFramework=net10.0
#:property PublishAot=true
#:property TrimMode=full
#:package Aprillz.MewUI@0.15.0

using System.Collections.ObjectModel;
using System.Net.Http;
using System.Text.RegularExpressions;

using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

// Platform/Backend registration
if (OperatingSystem.IsWindows())
{
    Win32Platform.Register();
    Direct2DBackend.Register();
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

Window window = null!;

// Resource download from GitHub
const string SampleResourcesBase = "https://raw.githubusercontent.com/aprillz/MewUI/main/samples/MewUI.Gallery/Resources/";
const string AssetsBase = "https://raw.githubusercontent.com/aprillz/MewUI/main/assets/";
var http = new HttpClient();
var resourcesStarted = false;
var resourceStallToastShown = false;

var logoResource = new ObservableValue<IImageSource?>(null);
var aprilResource = new ObservableValue<IImageSource?>(null);
var iconFolderOpenResource = new ObservableValue<IImageSource?>(null);
var iconFolderCloseResource = new ObservableValue<IImageSource?>(null);
var iconFileResource = new ObservableValue<IImageSource?>(null);
var iconsXamlResource = new ObservableValue<string?>(null);
var resourceStatus = new ObservableValue<string>("Resources: loading...");
var resourceDetail = new ObservableValue<string>("Resource detail: waiting...");

var imageResources = new ImageResourceEntry[]
{
    new("logo_h-1280.png", AssetsBase + "logo/logo_h-1280.png", logoResource),
    new("april.jpg", AssetsBase + "images/april.jpg", aprilResource),
    new("folder-horizontal-open.png", SampleResourcesBase + "folder-horizontal-open.png", iconFolderOpenResource),
    new("folder-horizontal.png", SampleResourcesBase + "folder-horizontal.png", iconFolderCloseResource),
    new("document.png", SampleResourcesBase + "document.png", iconFileResource),
};
var textResources = new TextResourceEntry[]
{
    new("Icons.xaml", SampleResourcesBase + "Icons.xaml", iconsXamlResource),
};

// DragDrop state
var _dropSummary = new ObservableValue<string>(
    "Drop files on this window.\n\nCurrent support:\n- IDataObject API\n- Win32\n- macOS\n- Linux (X11/XDND)");

// TopBar state
var currentAccent = ThemeManager.DefaultAccent;
var fpsText = new ObservableValue<string>("FPS: -");
var cullText = new ObservableValue<string>("Cull: -");
var fpsStopwatch = new System.Diagnostics.Stopwatch();
var fpsFrames = 0;
var backendText = new TextBlock();
var themeText = new TextBlock();

var timer = new DispatcherTimer().Interval(TimeSpan.FromSeconds(1)).OnTick(() =>
{
    double elapsed = fpsStopwatch.Elapsed.TotalSeconds;
    if (elapsed >= 1.0) { fpsText.Value = $"FPS: {(fpsFrames <= 1 ? 0 : fpsFrames) / elapsed:0.0}"; fpsFrames = 0; fpsStopwatch.Restart(); }
});

Application
    .Create()
    .UseAccent(Accent.Purple)
    .Run(new Window()
        .Ref(out window)
        .Apply(_ => window.Drop += e =>
        {
            if (e.Data.TryGetData<IReadOnlyList<string>>(StandardDataFormats.StorageItems, out var items) && items is not null)
            {
                _dropSummary.Value = $"Drop at {e.Position.X:0.#}, {e.Position.Y:0.#}\nCount: {items.Count}\n\n{string.Join("\n", items)}";
                e.Handled = true;
            }
            else
                _dropSummary.Value = $"Drop at {e.Position.X:0.#}, {e.Position.Y:0.#}\nFormats: {string.Join(", ", e.Data.Formats)}";
        })
        .Title("MewUI Gallery (FBA)")
        .Resizable(1356, 720)
        .StartCenterScreen()
        .OnLoaded(() =>
        {
            StartResourceLoading();
            UpdateTopBar();
            timer.Start();
        })
        .OnFrameRendered(() =>
        {
            if (!fpsStopwatch.IsRunning) { fpsStopwatch.Restart(); fpsFrames = 0; return; }
            fpsFrames++;
            var stats = window.LastFrameStats;
            cullText.Value = $"Draw: {stats.DrawCalls} | Cull: {stats.CullCount} ({stats.CullRatio:P0})";
        })
        .Content(new DockPanel().Margin(8).Children(
            TopBar().DockTop(),
            new ScrollViewer()
                .VerticalScroll(ScrollMode.Auto)
                .Padding(8)
                .Content(BuildGallery()))));

// ═══════════════════════════════════════════════════════════════════════
// Gallery
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement TopBar()
{
    var logoImage = BindResourceImage(
        new Image().ImageScaleQuality(ImageScaleQuality.HighQuality).Width(300).Height(80).CenterVertical(),
        logoResource);

    return new Border().Padding(12, 10).BorderThickness(1).Child(
        new DockPanel().Spacing(12).Children(
            new StackPanel().Horizontal().Spacing(8).Children(
                logoImage,
                new StackPanel().Vertical().Spacing(2).Children(
                    new TextBlock().Text("MewUI Gallery (FBA)").WithTheme((t, c) => c.Foreground(t.Palette.Accent)).FontSize(18).SemiBold(),
                    backendText,
                    new TextBlock().BindText(resourceStatus).FontSize(11))).DockLeft(),
        new StackPanel().DockRight().Spacing(8).Children(
            new StackPanel().Horizontal().CenterVertical().Spacing(12).Children(
                new StackPanel().Horizontal().CenterVertical().Spacing(8).Children(
                    new RadioButton().Content("System").CenterVertical().IsChecked().OnChecked(() => Application.Current.SetTheme(ThemeVariant.System)),
                    new RadioButton().Content("Light").CenterVertical().OnChecked(() => Application.Current.SetTheme(ThemeVariant.Light)),
                    new RadioButton().Content("Dark").CenterVertical().OnChecked(() => Application.Current.SetTheme(ThemeVariant.Dark))),
                themeText.CenterVertical(),
                new WrapPanel().Orientation(Orientation.Horizontal).Spacing(6).CenterVertical().ItemWidth(22).ItemHeight(22)
                    .Children(BuiltInAccent.Accents.Select(a => new Button().CornerRadius(14).BorderThickness(0).Content("")
                        .WithTheme((t, c) => c.Background(a.GetAccentColor(t.IsDark))).ToolTip(a.ToString())
                        .OnClick(() => { currentAccent = a; Application.Current.SetAccent(a); UpdateTopBar(); })).ToArray())),
            new StackPanel().Horizontal().Spacing(8).Children(
                new TextBlock().BindText(fpsText).CenterVertical(),
                new TextBlock().BindText(cullText).CenterVertical()))));
}

FrameworkElement BuildGallery() => new StackPanel()
    .Vertical()
    .Spacing(16)
    .Children(
                Section("Buttons", ButtonsPage()),
                Section("Inputs", InputsPage()),
                Section("Selection", SelectionPage()),
                Section("Window/Menu", WindowMenuPage()),
                Section("MessageBox", MessageBoxPage()),
                Section("Lists", ListsPage()),
                Section("GridView", GridViewPage()),
                Section("Panels", PanelsPage()),
                Section("Layout", LayoutPage()),
                Section("Typography", TypographyPage()),
                Section("Media", MediaPage()),
                Section("Shapes", ShapesPage()),
                Section("Icons", IconsPage()),
                Section("Transitions", TransitionsPage()),
                Section("Overlay", OverlayPage())
    );

// ── Helpers ──
void UpdateTopBar()
{
    backendText.Text($"Backend: {Application.Current.GraphicsFactory.Backend}");
    themeText.WithTheme((t, c) => c.Text($"Theme: {t.Name}"));
}

async Task LoadResourcesAsync()
{
    async Task<ImageResourceResult> DownloadImage(ImageResourceEntry resource)
    {
        try
        {
            var bytes = await http.GetByteArrayAsync(resource.Url);
            return new(resource, ImageSource.FromBytes(bytes), null);
        }
        catch
        {
            return new(resource, null, $"{resource.Name}: failed to download {resource.Url}");
        }
    }

    async Task<TextResourceResult> DownloadText(TextResourceEntry resource)
    {
        try
        {
            return new(resource, await http.GetStringAsync(resource.Url), null);
        }
        catch
        {
            return new(resource, null, $"{resource.Name}: failed to download {resource.Url}");
        }
    }

    var imageResults = await Task.WhenAll(imageResources.Select(DownloadImage));
    var textResults = await Task.WhenAll(textResources.Select(DownloadText));

    void ApplyLoadedResources()
    {
        foreach (var result in imageResults)
        {
            result.Resource.Target.Value = result.Image;
        }

        foreach (var result in textResults)
        {
            result.Resource.Target.Value = result.Text;
        }

        var loaded = 0;
        loaded += imageResources.Count(x => x.Target.Value != null);
        loaded += textResources.Count(x => x.Target.Value != null);
        var total = imageResources.Length + textResources.Length;
        resourceStatus.Value = loaded switch
        {
            _ when loaded == total => "Resources: ready",
            0 => "Resources: failed",
            _ => $"Resources: partial ({loaded}/{total})"
        };

        var failures = new List<string>();
        foreach (var result in imageResults)
        {
            if (!string.IsNullOrWhiteSpace(result.error))
            {
                failures.Add(result.error);
            }
        }

        foreach (var result in textResults)
        {
            if (!string.IsNullOrWhiteSpace(result.error))
            {
                failures.Add(result.error);
            }
        }

        resourceDetail.Value = failures.Count == 0
            ? "Resource detail: all downloads succeeded"
            : $"Resource detail: {string.Join(" | ", failures)}";

        if (loaded is > 0 && loaded < total && !resourceStallToastShown)
        {
            resourceStallToastShown = true;
            window.ShowToast($"{resourceStatus.Value} - {string.Join(", ", failures.Select(x => x.Split(':')[0]))}");
        }
    }

    if (Application.Current.Dispatcher is { } dispatcher)
    {
        dispatcher.BeginInvoke(DispatcherPriority.Normal, ApplyLoadedResources);
    }
    else
    {
        ApplyLoadedResources();
    }
}

void StartResourceLoading()
{
    if (resourcesStarted)
    {
        return;
    }

    resourcesStarted = true;
    _ = LoadResourcesAsync();
}

FrameworkElement Section(string title, FrameworkElement content) =>
    new StackPanel()
        .Vertical()
        .Spacing(8)
        .Children(
            new TextBlock().Text(title).FontSize(18).Bold(),
            content);

FrameworkElement Card(string title, FrameworkElement content, double minWidth = 320) =>
    new Border()
        .MinWidth(minWidth)
        .Padding(14)
        .CornerRadius(10)
        .Child(
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .WithTheme((t, c) => c.Foreground(t.Palette.Accent))
                        .Text(title)
                        .Bold(),
                    content));

FrameworkElement CardGrid(params FrameworkElement[] cards) =>
    new WrapPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8)
        .Children(cards);

Image BindResourceImage(Image image, ObservableValue<IImageSource?> source)
{
    image.SetBinding(Image.SourceProperty, source, BindingMode.OneWay);
    return image;
}

// ═══════════════════════════════════════════════════════════════════════
// Buttons
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement ButtonsPage() =>
    CardGrid(
        Card("Buttons", new StackPanel().Vertical().Spacing(8).Children(
            new Button().Content("Default"),
            new Button().Content("Disabled").Disable(),
            new Button().Content("Double Click").OnDoubleClick(() => _ = MessageBox.NotifyAsync("Double Click")))),

        Card("Built-in Styles", new StackPanel().Vertical().Spacing(8).Children(
            new Button().Content("Flat Button").Apply(b => b.StyleName = BuiltInStyles.FlatButton),
            new Button().Content("Flat Disabled").Apply(b => b.StyleName = BuiltInStyles.FlatButton).Disable(),
            new Button().Content("Accent Button").Apply(b => b.StyleName = BuiltInStyles.AccentButton),
            new Button().Content("Accent Disabled").Apply(b => b.StyleName = BuiltInStyles.AccentButton).Disable())),

        Card("ToggleButton", new StackPanel().Vertical().Spacing(8).Children(
            new ToggleButton().Content("Toggle"),
            new ToggleButton().Content("Checked").IsChecked(true),
            new ToggleButton().Content("Disabled").Disable(),
            new ToggleButton().Content("Disabled (Checked)").IsChecked(true).Disable())),

        Card("Toggle / Switch", new StackPanel().Vertical().Spacing(8).Children(
            new ToggleSwitch().IsChecked(true),
            new ToggleSwitch().IsChecked(false),
            new ToggleSwitch().IsChecked(true).Disable(),
            new ToggleSwitch().IsChecked(false).Disable())),

        Card("Progress", new StackPanel().Vertical().Spacing(8).Children(
            new ProgressBar().Value(20),
            new ProgressBar().Value(65),
            new ProgressBar().Value(65).Disable(),
            new Slider().Minimum(0).Maximum(100).Value(25),
            new Slider().Minimum(0).Maximum(100).Value(25).Disable()))
    );

// ═══════════════════════════════════════════════════════════════════════
// Inputs
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement InputsPage()
{
    var name = new ObservableValue<string>("This is my name");
    var intBinding = new ObservableValue<int>(1);
    var doubleBinding = new ObservableValue<double>(42.5);

    return CardGrid(
        Card("TextBox", new StackPanel().Vertical().Spacing(8).Children(
            new TextBox(),
            new TextBox().Placeholder("Type your name..."),
            new TextBox().BindText(name),
            new TextBox().Text("Disabled").Disable())),

        Card("PasswordBox", new StackPanel().Vertical().Spacing(8).Children(
            new PasswordBox().Placeholder("Password"),
            new PasswordBox { PasswordChar = '*' }.Placeholder("Custom mask"),
            new PasswordBox().Password("Disabled").Disable())),

        Card("NumericUpDown (int/double)",
            new Grid()
                .Columns("Auto,Auto,Auto")
                .Rows("Auto,Auto")
                .Spacing(8)
                .AutoIndexing()
                .Children(
                    new TextBlock().Text("Int").CenterVertical(),
                    new NumericUpDown().Width(140).Minimum(0).Maximum(100).Step(1).Format("0").BindValue(intBinding).CenterVertical(),
                    new TextBlock().BindText(intBinding, v => $"Value: {v}").CenterVertical(),
                    new TextBlock().Text("Double").CenterVertical(),
                    new NumericUpDown().Width(140).Minimum(0).Maximum(100).Step(0.1).Format("0.##").BindValue(doubleBinding).CenterVertical(),
                    new TextBlock().BindText(doubleBinding, v => $"Value: {v:0.##}").CenterVertical())),

        Card("MultiLineTextBox",
            new MultiLineTextBox()
                .Height(120)
                .Text("The quick brown fox jumps over the lazy dog.\n\n- Wrap supported\n- Selection supported\n- Scroll supported")),

        Card("ToolTip / ContextMenu", new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock()
                .Text("Hover to show a tooltip. Right-click to open a context menu.")
                .TextWrapping(TextWrapping.Wrap).Width(290).FontSize(11),
            new Button()
                .Content("Hover / Right-click me")
                .ToolTip("ToolTip text")
                .ContextMenu(
                    new ContextMenu()
                        .Item("Copy", new KeyGesture(Key.C, ModifierKeys.Primary))
                        .Item("Paste", new KeyGesture(Key.V, ModifierKeys.Primary))
                        .Separator()
                        .SubMenu("Transform", new ContextMenu()
                            .Item("Uppercase").Item("Lowercase")
                            .Separator()
                            .SubMenu("More", new ContextMenu().Item("Trim").Item("Normalize").Item("Sort")))
                        .SubMenu("View", new ContextMenu()
                            .Item("Zoom In", new KeyGesture(Key.Add, ModifierKeys.Primary))
                            .Item("Zoom Out", new KeyGesture(Key.Subtract, ModifierKeys.Primary))
                            .Item("Reset Zoom", new KeyGesture(Key.D0, ModifierKeys.Primary)))
                        .Separator()
                        .Item("Disabled", isEnabled: false)))),

        Card("Drag and Drop",
            new DockPanel().Height(220).Spacing(8).Children(
                new TextBlock().FontSize(11).DockTop()
                    .Text("Window-level drag and drop. Drop files anywhere on the gallery window."),
                new MultiLineTextBox().BindText(_dropSummary)))
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Selection
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement SelectionPage()
{
    var items = Enumerable.Range(1, 20).Select(i => $"Item {i}").Append("Item Long Long Long Long Long Long Long").ToArray();
    Calendar calendar = null!;

    return CardGrid(
        Card("CheckBox", new Grid().Columns("Auto,Auto").Rows("Auto,Auto,Auto").Spacing(8).Children(
            new CheckBox().Content("CheckBox"),
            new CheckBox().Content("Disabled").Disable(),
            new CheckBox().Content("Checked").IsChecked(true),
            new CheckBox().Content("Disabled (Checked)").IsChecked(true).Disable(),
            new CheckBox().Content("Three-state").IsThreeState(true).IsChecked(null),
            new CheckBox().Content("Disabled (Indeterminate)").IsThreeState(true).IsChecked(null).Disable())),

        Card("RadioButton", new Grid().Columns("Auto,Auto").Rows("Auto,Auto").Spacing(8).Children(
            new RadioButton().Content("A").GroupName("g"),
            new RadioButton().Content("C (Disabled)").GroupName("g2").Disable(),
            new RadioButton().Content("B").GroupName("g").IsChecked(true),
            new RadioButton().Content("Disabled (Checked)").GroupName("g2").IsChecked(true).Disable())),

        Card("ComboBox", new StackPanel().Vertical().Width(200).Spacing(8).Children(
            new ComboBox().Items(["Alpha", "Beta", "Gamma", "Delta", "Epsilon", "Zeta", "Eta", "Theta", "Iota", "Kappa"]).SelectedIndex(1),
            new ComboBox().Placeholder("Select an item...").Items(items),
            new ComboBox().Items(items).SelectedIndex(1).Disable()), minWidth: 250),

        Card("Calendar", new StackPanel().Vertical().Spacing(8).Children(
            new Calendar().Ref(out calendar),
            new TextBlock().Bind(TextBlock.TextProperty, calendar, Calendar.SelectedDateProperty, x => $"Selected: {x:yyyy-MM-dd}"))),

        Card("DatePicker", new StackPanel().Vertical().Spacing(8).Children(
            new DatePicker { Placeholder = "Select a date..." },
            new DatePicker { SelectedDate = DateTime.Today },
            new DatePicker { Placeholder = "Disabled" }.Disable()), minWidth: 250),

        Card("TabControl", new UniformGrid().Columns(2).Spacing(8).Children(
            new TabControl().Height(120).TabItems(
                new TabItem().Header("_Home").Content(new TextBlock().Text("Home tab content")),
                new TabItem().Header("Se_ttings").Content(new TextBlock().Text("Settings tab content")),
                new TabItem().Header("A_bout").Content(new TextBlock().Text("About tab content"))),
            new TabControl().Height(120).Disable().TabItems(
                new TabItem().Header("Home").Content(new TextBlock().Text("Home tab content")),
                new TabItem().Header("Settings").Content(new TextBlock().Text("Settings tab content")),
                new TabItem().Header("About").Content(new TextBlock().Text("About tab content")))))
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Panels
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement PanelsPage()
{
    Button canvasButton = null!;
    var canvasInfo = new ObservableValue<string>("Pos: 20,20");
    double left = 20, top = 20;

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
            .BorderThickness(1).CornerRadius(10).Width(280).Padding(8)
            .Child(content));

    return CardGrid(
        PanelCard("StackPanel", new StackPanel().Vertical().Spacing(6).Children(
            new Button().Content("A"), new Button().Content("B"), new Button().Content("C"))),

        PanelCard("DockPanel", new DockPanel().Spacing(6).Children(
            new Button().Content("Left").DockLeft(),
            new Button().Content("Top").DockTop(),
            new Button().Content("Bottom").DockBottom(),
            new Button().Content("Fill"))),

        PanelCard("WrapPanel", new WrapPanel().Orientation(Orientation.Horizontal).Spacing(6)
            .ItemWidth(60).ItemHeight(28)
            .Children(Enumerable.Range(1, 8).Select(i => new Button().Content($"#{i}")).ToArray())),

        PanelCard("UniformGrid", new UniformGrid().Columns(3).Rows(2).Spacing(6).Children(
            new Button().Content("1"), new Button().Content("2"), new Button().Content("3"),
            new Button().Content("4"), new Button().Content("5"), new Button().Content("6"))),

        PanelCard("Grid (Span)", new Grid().Columns("Auto,*,*").Rows("Auto,Auto,Auto").AutoIndexing().Spacing(6).Children(
            new Button().Content("ColSpan 2").ColumnSpan(2),
            new Button().Content("R1C1"),
            new Button().Content("RowSpan 2").RowSpan(2),
            new Button().Content("R1C2"),
            new Button().Content("R1C2"),
            new Button().Content("R2C1"),
            new Button().Content("R2C2"))),

        Card("Canvas", new StackPanel().Vertical().Spacing(6).Children(
            new Border().Height(140)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .BorderThickness(1).CornerRadius(10)
                .Child(new Canvas().Children(
                    new Button().Ref(out canvasButton).Content("Move").OnClick(MoveCanvasButton).CanvasPosition(left, top))),
            new TextBlock().BindText(canvasInfo).FontSize(11)), minWidth: 320),

        PanelCard("SplitPanel", new SplitPanel().Horizontal().SplitterThickness(8).Height(140)
            .MinFirst(60).MinSecond(60)
            .FirstLength(GridLength.Stars(1)).SecondLength(GridLength.Stars(1))
            .First(new Border().WithTheme((t, b) => b.Background(t.Palette.ButtonFace)).CornerRadius(8).Padding(8)
                .Child(new TextBlock().Text("First").Center()))
            .Second(new Border().WithTheme((t, b) => b.Background(t.Palette.ButtonFace)).CornerRadius(8).Padding(8)
                .Child(new TextBlock().Text("Second").Center()))),

        PanelCard("SplitPanel (Vertical)", new SplitPanel().Vertical().SplitterThickness(8).Height(140)
            .MinFirst(40).MinSecond(40)
            .FirstLength(GridLength.Stars(1)).SecondLength(GridLength.Stars(1))
            .First(new Border().WithTheme((t, b) => b.Background(t.Palette.ButtonFace)).CornerRadius(8).Padding(8)
                .Child(new TextBlock().Text("Top").Center()))
            .Second(new Border().WithTheme((t, b) => b.Background(t.Palette.ButtonFace)).CornerRadius(8).Padding(8)
                .Child(new TextBlock().Text("Bottom").Center())))
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Layout
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement LayoutPage()
{
    FrameworkElement LabelBox(string title, TextAlignment h, TextAlignment v, TextWrapping w)
    {
        const string sample = "The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog";
        return new StackPanel().Vertical().Spacing(4).Children(
            new TextBlock().Text(title).FontSize(11),
            new Border().Width(240).Height(80).Padding(6).BorderThickness(1).CornerRadius(6)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .Child(new TextBlock().Text(sample).TextWrapping(w).TextAlignment(h).VerticalTextAlignment(v)));
    }

    return CardGrid(
        Card("GroupBox", new GroupBox().Header("Header").Content(
            new StackPanel().Vertical().Spacing(6).Children(
                new TextBlock().Text("GroupBox content"),
                new Button().Content("Action")))),

        Card("Border + Alignment", new Border().Height(120)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .BorderThickness(1).CornerRadius(12)
            .Child(new TextBlock().Text("Centered Text").Center().Bold())),

        Card("Label Wrap/Alignment", new UniformGrid().Columns(3).Spacing(8).Children(
            LabelBox("Left/Top + Wrap", TextAlignment.Left, TextAlignment.Top, TextWrapping.Wrap),
            LabelBox("Center/Top + Wrap", TextAlignment.Center, TextAlignment.Top, TextWrapping.Wrap),
            LabelBox("Right/Top + Wrap", TextAlignment.Right, TextAlignment.Top, TextWrapping.Wrap),
            LabelBox("Left/Center + Wrap", TextAlignment.Left, TextAlignment.Center, TextWrapping.Wrap),
            LabelBox("Left/Bottom + Wrap", TextAlignment.Left, TextAlignment.Bottom, TextWrapping.Wrap),
            LabelBox("Left/Top + NoWrap", TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap),
            LabelBox("Right/Top + NoWrap", TextAlignment.Right, TextAlignment.Top, TextWrapping.NoWrap))),

        Card("TextTrimming", new StackPanel().Vertical().Spacing(8).Children(
            new Border().Width(200).Padding(6).BorderThickness(1).CornerRadius(6)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .Child(new TextBlock().Text("No trimming: The quick brown fox jumps over the lazy dog")),
            new Border().Width(200).Padding(6).BorderThickness(1).CornerRadius(6)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .Child(new TextBlock().Text("CharacterEllipsis: The quick brown fox jumps over the lazy dog").TextTrimming(TextTrimming.CharacterEllipsis)),
            new Border().Width(200).Height(50).Padding(6).BorderThickness(1).CornerRadius(6)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .Child(new TextBlock().Text("Wrap + Ellipsis: The quick brown fox jumps over the lazy dog. The quick brown fox jumps.")
                    .TextWrapping(TextWrapping.Wrap).TextTrimming(TextTrimming.CharacterEllipsis)))),

        Card("ScrollViewer", new ScrollViewer().Height(120).Width(200)
            .VerticalScroll(ScrollMode.Auto).HorizontalScroll(ScrollMode.Auto)
            .Content(new StackPanel().Vertical().Spacing(6)
                .Children(Enumerable.Range(1, 15).Select(i => new TextBlock().Text($"Line {i} - The quick brown fox jumps over the lazy dog.")).ToArray())))
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Typography
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement TypographyPage()
{
    Border TypoBorder(FrameworkElement child) =>
        new Border().Padding(12).BorderThickness(1).CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(child);

    return CardGrid(
        Card("Font Size Inheritance", TypoBorder(
            new StackPanel().Vertical().Spacing(6).Children(
                new TextBlock().Text("Inherited 16pt (from parent Border)"),
                new TextBlock().Text("Also inherited 16pt"),
                new TextBlock().Text("Override: 10pt").FontSize(10),
                new Button().Content("Button (inherited 16pt)"),
                new TextBox().Placeholder("TextBox (inherited 16pt)")))
            .FontSize(16)),

        Card("Font Family Inheritance", TypoBorder(
            new StackPanel().Vertical().Spacing(6).Children(
                new TextBlock().Text("Inherited Consolas"),
                new TextBlock().Text("Also Consolas"),
                new TextBlock().Text("Override: Segoe UI").FontFamily("Segoe UI"),
                new Button().Content("Consolas Button")))
            .FontFamily("Consolas")),

        Card("Font Weight Inheritance", TypoBorder(
            new StackPanel().Vertical().Spacing(6).Children(
                new TextBlock().Text("Inherited Bold"),
                new TextBlock().Text("Also Bold"),
                new TextBlock().Text("Override: Normal").FontWeight(FontWeight.Normal),
                new Button().Content("Bold Button")))
            .Bold()),

        Card("Nested Inheritance", TypoBorder(
            new StackPanel().Vertical().Spacing(6).Children(
                new TextBlock().Text("20pt (from outer)"),
                new Border().FontSize(12).Padding(8).BorderThickness(1).CornerRadius(6)
                    .WithTheme((t, b) => b.BorderBrush(t.Palette.ControlBorder))
                    .Child(new StackPanel().Vertical().Spacing(4).Children(
                        new TextBlock().Text("12pt (from inner Border)"),
                        new TextBlock().Text("Also 12pt"))),
                new TextBlock().Text("Back to 20pt")))
            .FontSize(20))
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Shapes
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement ShapesPage()
{
    var g = new PathGeometry();
    g.MoveTo(40, 0); g.LineTo(80, 70); g.LineTo(0, 70); g.Close(); g.Freeze();

    return CardGrid(
        Card("Rectangle", new StackPanel().Vertical().Spacing(8).Children(
            new Rectangle().Width(120).Height(60).Fill(Color.FromRgb(70, 130, 230)).Stroke(Color.FromRgb(40, 80, 180), 2),
            new Rectangle().Width(120).Height(60).CornerRadius(12).Fill(Color.FromRgb(100, 200, 120)).Stroke(Color.FromRgb(60, 140, 80), 2))),

        Card("Ellipse", new StackPanel().Vertical().Spacing(8).Children(
            new Ellipse().Width(100).Height(100).Fill(Color.FromRgb(230, 100, 80)).Stroke(Color.FromRgb(180, 60, 50), 2),
            new Ellipse().Width(120).Height(60).Fill(Color.FromRgb(200, 160, 60)))),

        Card("Line", new StackPanel().Vertical().Spacing(8).Children(
            new Line().Points(0, 0, 120, 40).Stroke(Color.FromRgb(70, 130, 230), 2),
            new Line().Points(0, 0, 120, 0).Stroke(Color.FromRgb(230, 100, 80), 3)
                .StrokeStyle(new StrokeStyle { DashArray = [6, 4] }))),

        Card("Path (SVG)", new StackPanel().Vertical().Spacing(8).Children(
            new PathShape()
                .Data("M12 21.35l-1.45-1.32C5.4 15.36 2 12.28 2 8.5 2 5.42 4.42 3 7.5 3c1.74 0 3.41.81 4.5 2.09C13.09 3.81 14.76 3 16.5 3 19.58 3 22 5.42 22 8.5c0 3.78-3.4 6.86-8.55 11.54L12 21.35z")
                .Fill(Color.FromRgb(220, 60, 80)).Stretch(Stretch.Uniform).Width(64).Height(64),
            new PathShape()
                .Data("M12 2l3.09 6.26L22 9.27l-5 4.87 1.18 6.88L12 17.77l-6.18 3.25L7 14.14 2 9.27l6.91-1.01L12 2z")
                .Fill(Color.FromRgb(240, 190, 40)).Stroke(Color.FromRgb(200, 150, 20), 1).Stretch(Stretch.Uniform).Width(64).Height(64))),

        Card("Path (Geometry)", new StackPanel().Vertical().Spacing(8).Children(
            new PathShape().Data(g).Fill(Color.FromRgb(120, 80, 200)).Width(80).Height(70),
            new PathShape()
                .Data("M4 12h12m0 0l-5-5m5 5l-5 5")
                .Stroke(Color.FromRgb(70, 130, 230), 2.5).Stretch(Stretch.Uniform).Width(64).Height(64))),

        Card("Stroke Styles", new StackPanel().Vertical().Spacing(8).Children(
            new Line().Points(0, 0, 160, 0).Stroke(Color.FromRgb(100, 100, 100), 3),
            new Line().Points(0, 0, 160, 0).Stroke(Color.FromRgb(100, 100, 100), 3).StrokeStyle(new StrokeStyle { DashArray = [8, 4] }),
            new Line().Points(0, 0, 160, 0).Stroke(Color.FromRgb(100, 100, 100), 3).StrokeStyle(new StrokeStyle { DashArray = [2, 4] }),
            new Line().Points(0, 0, 160, 0).Stroke(Color.FromRgb(100, 100, 100), 3).StrokeStyle(new StrokeStyle { DashArray = [8, 4, 2, 4] }))),

        Card("Prompt Icons",
            new WrapPanel().Orientation(Orientation.Horizontal).Spacing(12).Children(
                PromptTile("Question", new PromptIcon { Kind = PromptIconKind.Question }),
                PromptTile("Info", new PromptIcon { Kind = PromptIconKind.Info }),
                PromptTile("Warning", new PromptIcon { Kind = PromptIconKind.Warning }),
                PromptTile("Error", new PromptIcon { Kind = PromptIconKind.Error }),
                PromptTile("Success", new PromptIcon { Kind = PromptIconKind.Success }),
                PromptTile("Shield", new PromptIcon { Kind = PromptIconKind.Shield }),
                PromptTile("Crash", new PromptIcon { Kind = PromptIconKind.Crash })),
            minWidth: 720)
    );

    static FrameworkElement PromptTile(string title, FrameworkElement icon) =>
        new StackPanel().Width(90).Vertical().Spacing(6).Children(
            icon.Width(60).Height(60).Center(),
            new TextBlock().Text(title).Center());
}

// ═══════════════════════════════════════════════════════════════════════
// Transitions
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement TransitionsPage()
{
    Color[] colors = [
        Color.FromArgb(255, 70, 130, 220), Color.FromArgb(255, 220, 90, 70),
        Color.FromArgb(255, 70, 190, 120), Color.FromArgb(255, 200, 160, 50)];

    FrameworkElement Block(string text, int ci) =>
        new Border().Background(colors[ci % colors.Length]).CornerRadius(6).Padding(12, 8)
            .Child(new TextBlock().Text(text).Foreground(Color.White).Bold().Center());

    // Fade
    int fadeIdx = 0;
    string[] fadeItems = ["Hello, World!", "MewUI Transitions", "Fade Effect", "Smooth & Simple"];
    var fadeView = new TransitionContentControl { Transition = ContentTransition.CreateFade(durationMs: 300) };
    fadeView.Content = Block(fadeItems[0], 0);

    // Slide Left
    int slideIdx = 0;
    string[] slideItems = ["Page 1", "Page 2", "Page 3", "Page 4"];
    var slideLeftView = new TransitionContentControl { Transition = ContentTransition.CreateSlide(SlideDirection.Left, durationMs: 300) };
    slideLeftView.Content = Block(slideItems[0], 0);

    // Slide Up
    int slideUpIdx = 0;
    var slideUpView = new TransitionContentControl { Transition = ContentTransition.CreateSlide(SlideDirection.Up, durationMs: 300) };
    slideUpView.Content = Block(slideItems[0], 0);

    // Scale
    int scaleIdx = 0;
    string[] scaleItems = ["Zoom A", "Zoom B", "Zoom C", "Zoom D"];
    var scaleView = new TransitionContentControl { Transition = ContentTransition.CreateScale(durationMs: 300) };
    scaleView.Content = Block(scaleItems[0], 0);

    // Rotate
    int rotateIdx = 0;
    string[] rotateItems = ["Spin 1", "Spin 2", "Spin 3", "Spin 4"];
    var rotateView = new TransitionContentControl { Transition = ContentTransition.CreateRotate(durationMs: 400) };
    rotateView.Content = Block(rotateItems[0], 0);

    // Delay
    int delayIdx = 0;
    var delayView = new TransitionContentControl { Transition = ContentTransition.CreateFade(durationMs: 400, delayMs: 200) };
    delayView.Content = Block("Delayed Fade", 0);

    // ProgressRing
    var ring = new ProgressRing { IsActive = false };

    return CardGrid(
        Card("ProgressRing", new StackPanel().Vertical().Spacing(8).Children(
            new Border().Height(60).HorizontalAlignment(HorizontalAlignment.Center)
                .Child(ring.Width(48).Height(48).WithTheme((t, c) => c.Foreground(t.Palette.Accent))),
            new Button().Content("Toggle").OnClick(() => ring.IsActive = !ring.IsActive))),

        Card("Fade", new StackPanel().Vertical().Spacing(8).Children(
            new Border().Height(60).Child(fadeView),
            new Button().Content("Next").OnClick(() =>
            {
                fadeIdx = (fadeIdx + 1) % fadeItems.Length;
                fadeView.Content = Block(fadeItems[fadeIdx], fadeIdx);
            }))),

        Card("Slide Left", new StackPanel().Vertical().Spacing(8).Children(
            new Border().Height(60).Child(slideLeftView),
            new Button().Content("Next").OnClick(() =>
            {
                slideIdx = (slideIdx + 1) % slideItems.Length;
                slideLeftView.Content = Block(slideItems[slideIdx], slideIdx);
            }))),

        Card("Slide Up", new StackPanel().Vertical().Spacing(8).Children(
            new Border().Height(60).Child(slideUpView),
            new Button().Content("Next").OnClick(() =>
            {
                slideUpIdx = (slideUpIdx + 1) % slideItems.Length;
                slideUpView.Content = Block(slideItems[slideUpIdx], slideUpIdx);
            }))),

        Card("Scale", new StackPanel().Vertical().Spacing(8).Children(
            new Border().Height(60).Child(scaleView),
            new Button().Content("Next").OnClick(() =>
            {
                scaleIdx = (scaleIdx + 1) % scaleItems.Length;
                scaleView.Content = Block(scaleItems[scaleIdx], scaleIdx);
            }))),

        Card("Rotate", new StackPanel().Vertical().Spacing(8).Children(
            new Border().Height(60).Child(rotateView),
            new Button().Content("Next").OnClick(() =>
            {
                rotateIdx = (rotateIdx + 1) % rotateItems.Length;
                rotateView.Content = Block(rotateItems[rotateIdx], rotateIdx);
            }))),

        Card("Fade + Delay (200ms)", new StackPanel().Vertical().Spacing(8).Children(
            new Border().Height(60).Child(delayView),
            new Button().Content("Next").OnClick(() =>
            {
                delayIdx = (delayIdx + 1) % fadeItems.Length;
                delayView.Content = Block(fadeItems[delayIdx], delayIdx);
            })))
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Media
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement MediaPage()
{
    Image peekImage = null!;
    var imagePeekText = new ObservableValue<string>("Color: -");
    var aprilPreview = BindResourceImage(
        new Image().Width(120).Height(120)
            .StretchMode(Stretch.Uniform)
            .Center(),
        aprilResource);
    var peekColorImage = BindResourceImage(
        new Image().Ref(out peekImage)
            .OnMouseMove(e => imagePeekText.Value = peekImage.TryPeekColor(e.GetPosition(peekImage), out var c)
                ? $"Color: #{c.ToArgb():X8}"
                : "Color: #--------")
            .Width(200)
            .Height(120)
            .StretchMode(Stretch.Uniform)
            .Center()
            .ImageScaleQuality(ImageScaleQuality.HighQuality),
        logoResource);
    var fullImage = BindResourceImage(
        new Image()
            .StretchMode(Stretch.Uniform)
            .ImageScaleQuality(ImageScaleQuality.HighQuality),
        aprilResource);
    var viewBoxImage = BindResourceImage(
        new Image()
            .ViewBoxRelative(new Rect(0.25, 0.25, 0.5, 0.5))
            .StretchMode(Stretch.UniformToFill)
            .ImageScaleQuality(ImageScaleQuality.HighQuality),
        aprilResource);

    return CardGrid(
        Card("Image",
            new StackPanel().Vertical().Spacing(8).Children(
                aprilPreview,
                new TextBlock()
                    .Text("april.jpg")
                    .FontSize(11)
                    .Center())),

        Card("Peek Color",
            new StackPanel().Vertical().Spacing(8).Children(
                peekColorImage,
                new TextBlock()
                    .BindText(imagePeekText)
                    .FontFamily("Consolas")
                    .Center())),

        Card("Image ViewBox",
            new StackPanel().Vertical().Spacing(8).Children(
                new WrapPanel()
                    .Orientation(Orientation.Horizontal)
                    .Spacing(8)
                    .ItemWidth(140)
                    .ItemHeight(90)
                    .Children(
                        fullImage,
                        viewBoxImage),
                new TextBlock()
                    .Text("Left: full image (Uniform). Right: ViewBox (center 50%) + UniformToFill.")
                    .FontSize(11)))
    );
}

FrameworkElement IconsPage()
{
    var query = new ObservableValue<string>(string.Empty);
    var countText = new ObservableValue<string>("loading icons...");
    GridView grid = null!;

    void ApplyFilter()
    {
        var allIcons = IconResource.GetAll(iconsXamlResource.Value)
            .Select(e => new IconItem(e.Name, e.PathData))
            .ToArray();

        var q = (query.Value ?? string.Empty).Trim();
        IEnumerable<IconItem> filtered = allIcons;
        if (!string.IsNullOrWhiteSpace(q))
        {
            filtered = filtered.Where(i => i.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        var view = filtered.ToList();
        grid.SetItemsSource(view);
        countText.Value = allIcons.Length == 0
            ? "0 icons (resource pending or failed)"
            : $"{view.Count} / {allIcons.Length} icons";
    }

    query.Changed += ApplyFilter;
    iconsXamlResource.Changed += ApplyFilter;

    grid = new GridView()
        .RowHeight(32)
        .Width(300)
        .ItemsSource(Array.Empty<IconItem>())
        .Columns(
            new GridViewColumn<IconItem>()
                .Header("")
                .Width(40)
                .Template(
                    build: _ => new PathShape()
                        .Stretch(Stretch.Uniform)
                        .Width(24).Height(24)
                        .Center()
                        .WithTheme((t, p) => p.Fill(t.Palette.WindowText)),
                    bind: (view, item) => view.Data = item.Geometry),
            new GridViewColumn<IconItem>()
                .Header("Name")
                .Width(240)
                .Text(item => item.Name));

    ApplyFilter();

    return Card(
        "Icons (Path)",
        new DockPanel()
            .Height(400)
            .Spacing(6)
            .Children(
                new StackPanel()
                    .DockTop()
                    .Horizontal()
                    .Spacing(8)
                    .Children(
                        new TextBox()
                            .Width(200)
                            .Placeholder("Filter icons...")
                            .BindText(query),
                        new TextBlock()
                            .BindText(countText)
                            .CenterVertical()
                            .FontSize(11),
                        new TextBlock()
                            .Text("Fluent System Icons by Microsoft (MIT License)")
                            .WithTheme((t, c) => c.Foreground(t.Palette.DisabledText))
                            .CenterVertical()
                            .FontSize(11)),
                grid),
        minWidth: 460);
}

// ═══════════════════════════════════════════════════════════════════════
// Lists
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement ListsPage()
{
    var items = Enumerable.Range(1, 20).Select(i => $"Item {i}").Append("Item Long Long Long Long Long Long Long").ToArray();

    var users = new ObservableCollection<DemoUser>(
    [
        new(1, "Alice", "Admin", true), new(2, "Bob", "Editor", false),
        new(3, "Charlie", "Viewer", true), new(4, "Diana", "Editor", true),
        new(5, "Eve", "Viewer", false), new(6, "Frank", "Admin", true),
        new(7, "Grace", "Viewer", true), new(8, "Heidi", "Editor", false),
        new(9, "Ivan", "Viewer", true), new(10, "Judy", "Admin", true),
        new(11, "Mallory", "Editor", false), new(12, "Niaj", "Viewer", true),
        new(13, "Olivia", "Viewer", true), new(14, "Peggy", "Editor", false),
        new(15, "Sybil", "Admin", true),
    ]);

    // Simple ListBox
    var simpleList = Card("ListBox", new ListBox().Height(120).Width(200).Items(items));

    // Class items
    TextBlock classSelected = null!;
    var classList = Card("ListBox (class items)",
        new DockPanel().Spacing(6).Children(
            new TextBlock().DockBottom().Ref(out classSelected).FontSize(11).Text("Selected: (none)"),
            new ListBox().Height(160).Width(240)
                .Items(users, u => $"{u.Name} ({u.Role})", keySelector: u => u.Id)
                .OnSelectionChanged(obj =>
                {
                    var u = obj as DemoUser;
                    classSelected.Text = u == null ? "Selected: (none)" : $"Selected: {u.Name} ({u.Role})";
                })));

    // ItemsView + ItemTemplate
    var nextId = users.Max(u => u.Id) + 1;
    var usersView = new ItemsView<DemoUser>(users, u => u.Name, u => u.Id);
    ListBox templateList = null!; TextBlock templateSelected = null!;
    var templateCard = Card("ListBox (ItemsView + ItemTemplate)",
        new DockPanel().Spacing(6).Children(
            new StackPanel().DockTop().Horizontal().Spacing(8).Children(
                new Button().Content("Add").OnClick(() => { var id = nextId++; users.Add(new DemoUser(id, $"User {id}", "Viewer", id % 2 == 0)); }),
                new Button().Content("Remove").OnClick(() => { if (users.Count > 0) users.RemoveAt(users.Count - 1); })),
            new TextBlock().DockBottom().Ref(out templateSelected).FontSize(11).Text("Selected: (none)"),
            new ListBox().Ref(out templateList).Height(170).Width(260).ItemHeight(40).ItemsSource(usersView)
                .OnSelectionChanged(obj => { var u = obj as DemoUser; templateSelected.Text = u == null ? "Selected: (none)" : $"Selected: {u.Name}"; })));
    templateList.ItemTemplate<DemoUser>(
        build: ctx => new Border().Padding(6, 4).Child(new StackPanel().Horizontal().Spacing(8).Children(
            new Ellipse().Register(ctx, "Dot").Size(10, 10).CenterVertical(),
            new StackPanel().Vertical().Children(new TextBlock().Register(ctx, "Name").FontSize(12).Bold(), new TextBlock().Register(ctx, "Role").FontSize(10)))),
        bind: (_, u, _, ctx) =>
        {
            ctx.Get<TextBlock>("Name").Text = u.Name; ctx.Get<TextBlock>("Role").Text = u.Role;
            ctx.Get<Ellipse>("Dot").WithTheme((t, b) => { b.Fill(u.IsOnline ? t.Palette.Accent : t.Palette.ControlBorder); });
        });

    // TreeView with icons
    TreeViewNode[] Get(params string[] texts) => texts.Select(x => new TreeViewNode(x)).ToArray();
    var treeItems = new[] {
        new TreeViewNode("src", [
            new TreeViewNode("MewUI", [
                new TreeViewNode("Controls", Get("Button.cs", "TextBox.cs", "TreeView.cs"))
            ]),
            new TreeViewNode("Rendering", [
                new TreeViewNode("Gdi", Get("GdiMeasurementContext.cs","GdiGrapchisContext.cs","GdiGraphicsFactory.cs")),
                new TreeViewNode("Direct2D", Get("Direct2DMeasurementContext.cs","Direct2DGrapchisContext.cs","Direct2DGraphicsFactory.cs")),
                new TreeViewNode("OpenGL", Get("OpenGLMeasurementContext.cs","OpenGLGrapchisContext.cs","OpenGLGraphicsFactory.cs")),
            ])
        ]),
        new TreeViewNode("README.md"),
        new TreeViewNode("assets", [new TreeViewNode("logo.png"), new TreeViewNode("icon.ico")]) };
    TextBlock treeSelected = null!;
    var treeView = new TreeView().Width(200).ItemsSource(treeItems).ExpandTrigger(TreeViewExpandTrigger.DoubleClickNode)
        .OnSelectionChanged(obj => { var n = obj as TreeViewNode; treeSelected.Text = n == null ? "Selected: (none)" : $"Selected: {n.Text}"; });
    treeView.ItemTemplate<TreeViewNode>(
        build: ctx => new StackPanel().Horizontal().Spacing(6).Children(
            new Image().Register(ctx, "I").Size(16, 16).StretchMode(Stretch.None).CenterVertical(),
            new TextBlock().Register(ctx, "T").CenterVertical()),
        bind: (_, it, _, ctx) =>
        {
            var icon = ctx.Get<Image>("I");
            var source = it.HasChildren
                ? (treeView.IsExpanded(it) ? iconFolderOpenResource : iconFolderCloseResource)
                : iconFileResource;
            icon.SetBinding(Image.SourceProperty, source, BindingMode.OneWay);
            ctx.Get<TextBlock>("T").Text(it.Text);
        });

    treeView.Expand(treeItems[0]); treeView.Expand(treeItems[0].Children[0]);
    var treeCard = Card("TreeView", new DockPanel().Height(240).Spacing(6).Children(
        new TextBlock().DockBottom().Ref(out treeSelected).FontSize(11).Text("Selected: (none)"), treeView));

    // WrapPresenter
    var wc = new[] { Color.FromRgb(230, 100, 100), Color.FromRgb(100, 180, 230), Color.FromRgb(100, 200, 130), Color.FromRgb(220, 180, 80) };
    var wi = Enumerable.Range(0, 4800).Select(i => $"Tile {i + 1}").ToArray();
    var ws = new TextBlock { Text = "Selected: (none)" };
    var wrapCard = Card("ListBox (WrapPresenter)", new StackPanel().Vertical().Spacing(6).Children(
        new ListBox().ItemPadding(new(2)).Height(240).Width(402).WrapPresenter(80, 80).Items(wi)
            .ItemTemplate(new DelegateTemplate<string>(
                build: ctx => new Border().Register(ctx, "Bg").CornerRadius(6).Child(new TextBlock().Register(ctx, "L").Center().FontSize(11)),
                bind: (_, item, idx, ctx) => { ctx.Get<Border>("Bg").Background(wc[idx % wc.Length].WithAlpha(180)); ctx.Get<TextBlock>("L").Text(item ?? ""); }))
            .OnSelectionChanged(obj => ws.Text = obj is string s ? $"Selected: {s}" : "Selected: (none)"), ws));

    var itemsControlWrapCard = Card("ItemsControl (WrapPresenter)",
        new ItemsControl()
            .ItemPadding(new(2))
            .Height(240)
            .Width(402)
            .WrapPresenter(80, 80)
            .ItemsSource(ItemsView.Create(wi))
            .ItemTemplate(new DelegateTemplate<string>(
                build: ctx => new Border().Register(ctx, "Bg").CornerRadius(6).Child(new TextBlock().Register(ctx, "L").Center().FontSize(11)),
                bind: (_, item, idx, ctx) =>
                {
                    ctx.Get<Border>("Bg").Background(wc[idx % wc.Length].WithAlpha(120));
                    ctx.Get<TextBlock>("L").Text(item ?? "");
                })));

    // Chat (variable height)
    long chatId = 1;
    var msgs = new ObservableCollection<ChatMessage>();
    void AddMsg(bool mine, string snd, string txt) => msgs.Add(new ChatMessage(chatId++, snd, txt, mine, DateTimeOffset.Now));
    static string CT(int s) => (s % 7) switch { 0 => "Short.", 1 => "A longer message that wraps.", 2 => "Multi:\n- A\n- B", 3 => "Lorem ipsum dolor sit amet.", 4 => "Symbols: !@#$%", 5 => "Quick brown fox.", _ => "superlongword_superlongword" };
    AddMsg(false, "Bot", "Chat-style ItemsControl.\nVariable-height virtualization.");
    AddMsg(true, "You", "Try scrolling, click 'Prepend 20'.");
    for (int i = 0; i < 40; i++) AddMsg(i % 3 == 0, i % 3 == 0 ? "You" : "Bot", CT(10 + i));
    var cv = new ItemsView<ChatMessage>(msgs, m => m.Text, m => m.Id);
    ItemsControl cl = null!; var ci = new ObservableValue<string>(""); var cst = new ObservableValue<string>("");
    void CScr() { cl?.ScrollIntoView(msgs.Count - 1); }
    void CSnd() { var t = (ci.Value ?? "").Trim(); if (t.Length == 0) return; msgs.Add(new ChatMessage(chatId++, "You", t, true, DateTimeOffset.Now)); ci.Value = ""; Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render, CScr); }
    void CSt() => cst.Value = $"Messages: {msgs.Count}";
    var chatCard = Card("ItemsControl (chat / variable height)",
        new DockPanel().MinWidth(640).MaxWidth(960).Height(320).Spacing(6).Children(
            new StackPanel().DockTop().Horizontal().Spacing(8).Children(
                new Button().Content("Prepend 20").OnClick(() => { var st = chatId; for (int i = 19; i >= 0; i--) msgs.Insert(0, new ChatMessage(chatId++, "Bot", CT((int)(st + i)), false, DateTimeOffset.Now.AddMinutes(-i))); CSt(); }),
                new Button().Content("To bottom").OnClick(() => { CScr(); CSt(); }),
                new TextBlock().BindText(cst).FontSize(11).CenterVertical()),
            new DockPanel().DockBottom().Spacing(6).Children(
                new Button().DockRight().Content("Send").OnClick(() => { CSnd(); CSt(); }),
                new TextBox().Placeholder("Message...").BindText(ci).OnKeyDown(e => { if (e.Key == Key.Enter) { e.Handled = true; CSnd(); } })),
            new ItemsControl().Ref(out cl).HorizontalAlignment(HorizontalAlignment.Stretch).VariableHeightPresenter()
                .WithTheme((t, _) => cl.BorderBrush(t.Palette.ControlBorder).BorderThickness(t.Metrics.ControlBorderThickness))
                .ItemsSource(cv).ItemPadding(Thickness.Zero)
                .ItemTemplate(new DelegateTemplate<ChatMessage>(
                    build: ctx => new Border().Register(ctx, "B").BorderThickness(1).CornerRadius(10).Margin(16, 8).Padding(10, 6)
                        .Child(new StackPanel().Vertical().Spacing(2).Children(new TextBlock().Register(ctx, "S").FontSize(10).Bold(), new TextBlock().Register(ctx, "X").TextWrapping(TextWrapping.Wrap))),
                    bind: (_, m, _, ctx) =>
                    {
                        var b = ctx.Get<Border>("B"); ctx.Get<TextBlock>("S").Text = m.Sender; ctx.Get<TextBlock>("S").IsVisible = !m.Mine; ctx.Get<TextBlock>("X").Text = m.Text;
                        b.HorizontalAlignment = m.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                        b.WithTheme((t, bb) => { if (m.Mine) { bb.Background(t.Palette.Accent.Lerp(t.Palette.WindowBackground, 0.85)); bb.BorderBrush(t.Palette.Accent.Lerp(t.Palette.WindowText, 0.15)); } else { bb.Background(t.Palette.ControlBackground); bb.BorderBrush(t.Palette.ControlBorder); } });
                    }))
                .Apply(_ => CSt()).Apply(_ => CScr())), minWidth: 420);

    return CardGrid(simpleList, classList, templateCard, treeCard, wrapCard, itemsControlWrapCard, chatCard);
}

// ═══════════════════════════════════════════════════════════════════════
// GridView
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement GridViewPage()
{
    // Simple GridView
    var gridItems = Enumerable.Range(1, 10_000)
        .Select(i => new SimpleGridRow(i, $"Item {i}", (i % 6) switch { 1 => "Warning", 2 => "Error", _ => "Normal" }))
        .ToArray();
    GridView simple = null!;
    var gridHitText = new ObservableValue<string>("Click: (none)");

    Color GetColor(Theme t, string status) => status switch
    {
        "Warning" => Color.Orange,
        "Error" => Color.Red,
        _ => t.Palette.WindowText
    };

    var simpleCard = Card("GridView",
        new DockPanel().Height(240).Spacing(6).Children(
            new TextBlock().DockBottom().BindText(gridHitText).FontSize(11),
            new GridView().Ref(out simple).Height(240).ItemsSource(gridItems)
                .OnMouseDown(e =>
                {
                    if (simple.TryGetCellIndexAt(e, out int row, out int col, out bool isHeader))
                        gridHitText.Value = isHeader ? $"Click: Header Col={col}" : $"Click: Row={row} Col={col}";
                    else
                        gridHitText.Value = "Click: (none)";
                })
                .Columns(
                    new GridViewColumn<SimpleGridRow>().Header("#").Width(60).Text(r => r.Id.ToString()),
                    new GridViewColumn<SimpleGridRow>().Header("Name").Width(100).Text(r => r.Name),
                    new GridViewColumn<SimpleGridRow>().Header("Status").Width(100)
                        .Template(
                            build: _ => new TextBlock().Margin(8, 0).CenterVertical(),
                            bind: (view, row) => view.Text(row.Status).WithTheme((t, c) => c.Foreground(GetColor(t, row.Status)))))));

    // Complex binding card
    var query = new ObservableValue<string>(""); var onlyErrors = new ObservableValue<bool>(false); var minAmt = new ObservableValue<double>(0);
    var sKey = new ObservableValue<int>(0); var sDesc = new ObservableValue<bool>(false);
    var sumText = new ObservableValue<string>("Rows: -"); var selText = new ObservableValue<string>("Selected: (none)");
    var allRows = Enumerable.Range(1, 800).Select(i => new ComplexGridRow(i, $"User {i:00}", Math.Round((i * 13.37) % 100, 2), i % 11 == 0 || i % 17 == 0, i % 9 != 0)).ToList();
    GridView cg = null!;
    void AV()
    {
        IEnumerable<ComplexGridRow> rows = allRows; var q = (query.Value ?? "").Trim();
        if (q.Length > 0) rows = rows.Where(r => r.Id.ToString().Contains(q, StringComparison.OrdinalIgnoreCase) || r.Name.Contains(q, StringComparison.OrdinalIgnoreCase));
        if (onlyErrors.Value) rows = rows.Where(r => r.HasError.Value); rows = rows.Where(r => r.Amount.Value >= minAmt.Value);
        rows = sKey.Value switch { 1 => sDesc.Value ? rows.OrderByDescending(r => r.Name) : rows.OrderBy(r => r.Name), 2 => sDesc.Value ? rows.OrderByDescending(r => r.Amount.Value) : rows.OrderBy(r => r.Amount.Value), _ => sDesc.Value ? rows.OrderByDescending(r => r.Id) : rows.OrderBy(r => r.Id) };
        var v = rows.ToList(); cg.SetItemsSource(v); sumText.Value = $"Rows:{v.Count}/{allRows.Count} Err:{v.Count(r => r.HasError.Value)} Sum:{v.Sum(r => r.Amount.Value):0.##}";
    }
    query.Changed += AV; onlyErrors.Changed += AV; minAmt.Changed += AV; sKey.Changed += AV; sDesc.Changed += AV;
    cg = new GridView().Height(190).ItemsSource(allRows).Apply(g => g.SelectionChanged += o => selText.Value = o is ComplexGridRow r ? $"Selected:#{r.Id} {r.Name}" : "Selected:(none)")
        .Columns(new GridViewColumn<ComplexGridRow>().Header("#").Width(44).Text(r => r.Id.ToString()),
            new GridViewColumn<ComplexGridRow>().Header("Name").Width(110).Text(r => r.Name),
            new GridViewColumn<ComplexGridRow>().Header("Amount").Width(110).Template(build: _ => new NumericUpDown().Padding(6, 0).CenterVertical().Minimum(0).Maximum(100).Step(0.5).Format("0.##"), bind: (v, r) => v.BindValue(r.Amount)),
            new GridViewColumn<ComplexGridRow>().Header("Error").Width(60).Template(build: _ => new CheckBox().Center(), bind: (v, r) => v.BindIsChecked(r.HasError)),
            new GridViewColumn<ComplexGridRow>().Header("Status").Width(110).Template(build: _ => new TextBlock().Margin(8, 0).CenterVertical(), bind: (v, r) => v.BindText(r.StatusText)));
    AV();
    var complexCard = Card("GridView (Complex binding)", new DockPanel().Height(240).Spacing(8).Children(
        new StackPanel().DockTop().Horizontal().Spacing(8).Children(new TextBox().Width(120).Placeholder("Search").BindText(query), new CheckBox().Content("Errors only").BindIsChecked(onlyErrors),
            new TextBlock().Text("Min").CenterVertical().FontSize(11), new NumericUpDown().Width(90).Minimum(0).Maximum(100).Step(1).Format("0").BindValue(minAmt),
            new ComboBox().Width(80).Items(["Id", "Name", "Amount"]).BindSelectedIndex(sKey), new CheckBox().Content("Desc").BindIsChecked(sDesc)),
        new StackPanel().DockBottom().Vertical().Spacing(2).Children(new TextBlock().BindText(sumText).FontSize(11), new TextBlock().BindText(selText).FontSize(11)), cg), minWidth: 520);

    return CardGrid(simpleCard, complexCard);
}

// ═══════════════════════════════════════════════════════════════════════
// MessageBox
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement MessageBoxPage()
{
    FrameworkElement PromptSample(string title, Func<Task<string>> showFunc)
    {
        var status = new ObservableValue<string>("Result: -");
        return Card(title, new StackPanel().Vertical().Spacing(8).Children(
            new Button().Content("Show").OnClick(async () => status.Value = await showFunc()),
            new TextBlock().BindText(status).FontSize(11)));
    }

    return CardGrid(
        PromptSample("Info (NotifyAsync)", async () =>
        {
            await MessageBox.NotifyAsync("This is an Info message box sample.", PromptIconKind.Info, owner: window);
            return "Result: closed";
        }),
        PromptSample("Warning (ConfirmAsync + Detail)", async () =>
        {
            var r = await MessageBox.ConfirmAsync("This is a Warning message box sample.",
                icon: PromptIconKind.Warning,
                detail: "System.InvalidOperationException: The operation failed.\n   at App.Module.Process() in Module.cs:line 42",
                owner: window);
            return $"Result: {r}";
        }),
        PromptSample("Error (AskYesNoAsync + Detail)", async () =>
        {
            var r = await MessageBox.AskYesNoAsync("A critical error occurred.\nWould you like to retry?",
                icon: PromptIconKind.Error,
                detail: "A critical error occurred while saving the file.",
                owner: window);
            return $"Result: {r}";
        }),
        PromptSample("Question (AskYesNoCancelAsync)", async () =>
        {
            var r = await MessageBox.AskYesNoCancelAsync("This is a Question message box sample.", owner: window);
            return $"Result: {r}";
        }),
        PromptSample("Success (NotifyAsync + Detail)", async () =>
        {
            await MessageBox.NotifyAsync("Build completed successfully.", PromptIconKind.Success,
                detail: "Output: bin/Release/net8.0/MyApp.dll\nTime: 2.3s\nWarnings: 0\nErrors: 0", owner: window);
            return "Result: closed";
        }),
        PromptSample("Shield (PromptAsync)", async () =>
        {
            var r = await MessageBox.PromptAsync(new MessageBoxOptions
            {
                Message = "Connection to server timed out after 30 seconds.",
                Icon = PromptIconKind.Shield,
                Buttons = [new("Retry", MessageButtonRole.Accept), new("Ignore", MessageButtonRole.Destructive), new("Abort", MessageButtonRole.Reject)],
                Detail = "Host: api.example.com:443\nAttempts: 3/3",
                Owner = window
            });
            return $"Result: {r}";
        }),
        PromptSample("Crash (NotifyAsync + StackTrace)", async () =>
        {
            await MessageBox.NotifyAsync("An unhandled exception has occurred.", PromptIconKind.Crash,
                "System.NullReferenceException: Object reference not set to an instance of an object.\n"
                + "   at App.Module.OnRender() in Module.cs:line 387\n"
                + "   at App.Main() in Program.cs:line 10", owner: window);
            return "Result: Closed";
        }),
        Card("Native", new StackPanel().Vertical().Spacing(8).Children(
            new Button().Content("OK").OnClick(() => NativeMessageBox.Show("This is a native OK message box.", "FBA Gallery")),
            new Button().Content("OK / Cancel").OnClick(() => NativeMessageBox.Show("Do you want to continue?", "FBA Gallery", NativeMessageBoxButtons.OkCancel, NativeMessageBoxIcon.Question)),
            new Button().Content("Yes / No").OnClick(() => NativeMessageBox.Show("Are you sure?", "FBA Gallery", NativeMessageBoxButtons.YesNo, NativeMessageBoxIcon.Warning)),
            new Button().Content("Yes / No / Cancel").OnClick(() => NativeMessageBox.Show("Save changes?", "FBA Gallery", NativeMessageBoxButtons.YesNoCancel, NativeMessageBoxIcon.Information))))
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Overlay
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement OverlayPage()
{
    var confetti = new ConfettiOverlay();
    window.OverlayLayer.Add(confetti);

    return CardGrid(
        Card("Toast", new StackPanel().Vertical().Spacing(8).Children(
            new Button().Content("Show Toast").OnClick(() => window.ShowToast("Hello, Toast!")),
            new Button().Content("Long Message").OnClick(() => window.ShowToast("This is a longer toast message to test auto-dismiss duration scaling.")),
            new Button().Content("Rapid Fire").OnClick(() => window.ShowToast($"Toast at {DateTime.Now:HH:mm:ss}")))),

        Card("BusyIndicator", new StackPanel().Vertical().Spacing(8).Children(
            new Button().Content("Show (non-cancellable)").OnClick(() => ShowBusyDemo(false)),
            new Button().Content("Show (cancellable)").OnClick(() => ShowBusyDemo(true)))),

        Card("Confetti", new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().Text("Port of WpfConfetti by caefale")
                .WithTheme((t, c) => c.Foreground(t.Palette.DisabledText)).FontSize(11),
            new Grid().Columns("*,*").Rows("Auto,Auto,Auto,Auto").Spacing(4).Children(
                new Button().Content("Burst").OnClick(() => confetti.Burst()).ColumnSpan(2),
                new Button().Content("Start Cannons").OnClick(() => confetti.Cannons()).Row(1),
                new Button().Content("Stop Cannons").OnClick(() => confetti.StopCannons()).Row(1).Column(1),
                new Button().Content("Start Rain").OnClick(() => confetti.StartRain()).Row(2),
                new Button().Content("Stop Rain").OnClick(() => confetti.StopRain()).Row(2).Column(1),
                new Button().Content("Clear All").OnClick(() => confetti.Clear()).Row(3).ColumnSpan(2))))
    );
}

// ═══════════════════════════════════════════════════════════════════════
// Window / Menu
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement WindowMenuPage()
{
    var dialogStatus = new ObservableValue<string>("Dialog: -");

    async void ShowDialogSample()
    {
        dialogStatus.Value = "Dialog: opening...";
        var dlg = new Window()
            .Resizable(420, 220)
            .StartCenterScreen()
            .OnBuild(x => x
                .Title("ShowDialog sample")
                .Padding(16)
                .Content(new StackPanel().Vertical().Spacing(10).Children(
                    new TextBlock().Text("This is a modal window."),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new Button().Content("Open dialog").OnClick(ShowDialogSample),
                        new Button().Content("Close").OnClick(() => x.Close())))));
        try
        {
            await dlg.ShowDialogAsync(window);
            dialogStatus.Value = "Dialog: closed";
        }
        catch (Exception ex) { dialogStatus.Value = $"Dialog: error ({ex.GetType().Name})"; }
    }

    var shortcutLog = new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
        .Text("Press a shortcut key (e.g. Ctrl+N, Ctrl+S, ...)");
    void OnShortcut(string action) => shortcutLog.Text = $"[{DateTime.Now:HH:mm:ss.fff}] {action}";

    return CardGrid(
        Card("MenuBar", new StackPanel().Width(290).Vertical().Spacing(8).Children(
            CreateMenu(OnShortcut),
            new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
                .Text("Hover to switch menus while a popup is open. Submenus supported."),
            shortcutLog)),

        Card("Native Custom Chrome", new StackPanel().Vertical().Spacing(8).Children(
            new Button().Content("Open Native Chrome Window")
                .OnClick(() => new NativeCustomWindowSample().Show(window)),
            new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
                .Text("Hides the default title bar while keeping\nthe native frame (rounded corners, shadow)."))),

        Card("ShowDialogAsync", new StackPanel().Vertical().Spacing(8).Children(
            new Button().Content("Open dialog").OnClick(ShowDialogSample),
            new TextBlock().BindText(dialogStatus).FontSize(11))),

        TransparentWindowCard(),
        ManualPositionCard(),
        FileDialogsCard(),
        PromptDialogCard(),
        NativeMessageHookCard(),
        Card("AccessKey & Shortcuts", AccessKeyCard())
    );
}

FrameworkElement TransparentWindowCard()
{
    var status = new ObservableValue<string>("Transparent: -");
    return Card("Transparent Window", new StackPanel().Vertical().Spacing(8).Children(
        new Button().Content("Open transparent window").OnClick(() =>
        {
            status.Value = "Transparent: opening...";
            Window tw = null!;
            new Window().Ref(out tw)
                .FitContentHeight(520)
                .Background(Color.Pink.WithAlpha(64))
                .StartCenterOwner()
                .OnBuild(x =>
                {
                    x.Title = "Transparent window sample";
                    x.AllowsTransparency = true;
                    x.Padding = new Thickness(20);
                    x.Content = new DockPanel().Children(
                        new Border().DockBottom().Background(Color.Green.WithAlpha(64))
                            .Child(BindResourceImage(
                                new Image().Apply(x => EnableWindowDrag(tw, x)).Width(500).Height(128)
                                    .ImageScaleQuality(ImageScaleQuality.HighQuality)
                                    .StretchMode(Stretch.Uniform),
                                logoResource)),
                        new Border().Padding(16).Top()
                            .WithTheme((t, b) => b.Background(t.Palette.Accent.WithAlpha(32)))
                            .CornerRadius(10)
                            .Child(new StackPanel().Vertical().Spacing(10).Children(
                                new TextBlock().TextWrapping(TextWrapping.Wrap)
                                    .Text("Wrapped label followed by a button. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog."),
                                new Button().Content("Close").OnClick(() => x.Close()))));
                });
            try { tw.Show(window); status.Value = "Transparent: shown"; }
            catch (Exception ex) { status.Value = $"Transparent: error ({ex.GetType().Name})"; }
        }),
        new TextBlock().BindText(status).FontSize(11)));
}

FrameworkElement ManualPositionCard()
{
    var status = new ObservableValue<string>("Manual: -");
    return Card("StartupManualPosition", new StackPanel().Vertical().Spacing(8).Children(
        new TextBlock().FontSize(11).Text("Opens a window with StartManualPosition(120, 140)."),
        new Button().Content("Open manual-position window").OnClick(() =>
        {
            status.Value = "Manual: opening at (120, 140)";
            Window manual = null!;
            new Window().Ref(out manual).Resizable(360, 180).StartManualPosition(120, 140)
                .OnBuild(x => x.Title("StartupManualPosition sample").Padding(16)
                    .Content(new StackPanel().Vertical().Spacing(10).Children(
                        new TextBlock().Text("StartupLocation.Manual\nLeft: 120\nTop: 140"),
                        new Button().Content("Close").OnClick(() => x.Close()))));
            try { manual.Show(); status.Value = "Manual: shown"; }
            catch (Exception ex) { status.Value = $"Manual: error ({ex.GetType().Name})"; }
        }),
        new TextBlock().BindText(status).FontSize(11)));
}

FrameworkElement FileDialogsCard()
{
    var openStatus = new ObservableValue<string>("Open Files: -");
    var saveStatus = new ObservableValue<string>("Save File: -");
    var folderStatus = new ObservableValue<string>("Select Folder: -");
    return Card("File Dialogs", new StackPanel().Vertical().Spacing(8).Children(
        new WrapPanel().Spacing(6).Children(
            new Button().Content("Open Files...").OnClick(() =>
            {
                var files = FileDialog.OpenFiles(new OpenFileDialogOptions { Owner = window.Handle, Filter = "All Files (*.*)|*.*" });
                openStatus.Value = files is null || files.Length == 0 ? "Open Files: canceled"
                    : files.Length == 1 ? $"Open Files: {files[0]}" : $"Open Files: {files.Length} files";
            }),
            new Button().Content("Save File...").OnClick(() =>
            {
                var file = FileDialog.SaveFile(new SaveFileDialogOptions { Owner = window.Handle, Filter = "Text Files (*.txt)|*.txt|All Files (*.*)|*.*", FileName = "demo.txt" });
                saveStatus.Value = file is null ? "Save File: canceled" : $"Save File: {file}";
            }),
            new Button().Content("Select Folder...").OnClick(() =>
            {
                var folder = FileDialog.SelectFolder(new FolderDialogOptions { Owner = window.Handle });
                folderStatus.Value = folder is null ? "Select Folder: canceled" : $"Select Folder: {folder}";
            })),
        new TextBlock().BindText(openStatus).FontSize(11).TextWrapping(TextWrapping.Wrap),
        new TextBlock().BindText(saveStatus).FontSize(11).TextWrapping(TextWrapping.Wrap),
        new TextBlock().BindText(folderStatus).FontSize(11).TextWrapping(TextWrapping.Wrap)));
}

FrameworkElement PromptDialogCard()
{
    var status = new ObservableValue<string>("Result: -");
    return Card("Prompt Dialog (FitContentHeight)", new StackPanel().Vertical().Spacing(8).Children(
        new TextBlock().FontSize(11).Text("Opens a FitContentHeight dialog.\nWindow height adjusts to content."),
        new Button().Content("Show Prompt").OnClick(async () =>
        {
            string? result = null;
            TextBox input = null!;
            Window dialog = null!;
            await new Window().Ref(out dialog).Title("Input").FitContentHeight(300, 300).Padding(12)
                .Content(new StackPanel().Vertical().Spacing(12).Children(
                    new TextBlock().Text("Enter your name:"),
                    new TextBox().Ref(out input).Placeholder("Name..."),
                    new StackPanel().Horizontal().Right().Spacing(6).Children(
                        new Button().Content("OK")
                            .OnCanClick(() => !string.IsNullOrWhiteSpace(input.Text))
                            .OnClick(() => { result = input.Text; dialog.Close(); }),
                        new Button().Content("Cancel").OnClick(dialog.Close))))
                .ShowDialogAsync(window);
            status.Value = result is null ? "Result: canceled" : $"Result: {result}";
        }),
        new TextBlock().BindText(status).FontSize(11)));
}

FrameworkElement NativeMessageHookCard()
{
    var hookLog = new ObservableValue<string>("Hook: idle");
    int messageCount = 0;
    bool hookActive = false;

    void OnNativeMessage(NativeMessageEventArgs args)
    {
        messageCount++;
        hookLog.Value = args switch
        {
            Win32NativeMessageEventArgs win32 => $"Win32 #{messageCount}: msg=0x{win32.Msg:X4}",
            X11NativeMessageEventArgs x11 => $"X11 #{messageCount}: type={x11.EventType}",
            MacOSNativeMessageEventArgs macos => $"macOS #{messageCount}: type={macos.EventType}",
            _ => $"#{messageCount}: {args.GetType().Name}"
        };
    }

    return Card("NativeMessage Hook", new StackPanel().Vertical().Spacing(8).Children(
        new TextBlock().FontSize(11).Text("Subscribes to Window.NativeMessage to observe raw platform messages."),
        new StackPanel().Horizontal().Spacing(6).Children(
            new Button().Content("Start Hook").OnClick(() =>
            {
                if (!hookActive) { hookActive = true; messageCount = 0; window.NativeMessage += OnNativeMessage; hookLog.Value = "Hook: active"; }
            }),
            new Button().Content("Stop Hook").OnClick(() =>
            {
                if (hookActive) { hookActive = false; window.NativeMessage -= OnNativeMessage; hookLog.Value = $"Hook: stopped ({messageCount} msgs)"; }
            })),
        new TextBlock().BindText(hookLog).FontSize(11).TextWrapping(TextWrapping.Wrap)));
}

FrameworkElement AccessKeyCard()
{
    var nameBox = new TextBox().Placeholder("Name").Width(160);
    return new StackPanel().Vertical().Spacing(8).Children(
        new TextBlock().Text("Press Alt to show access key underlines (Windows/Linux).").FontSize(11),
        new StackPanel().Horizontal().Spacing(8).Children(
            new Label().CenterVertical().Text("_Name:").AccessKeyTarget(nameBox), nameBox),
        new StackPanel().Horizontal().Spacing(8).Children(
            new Button().Content("_OK"), new Button().Content("_Cancel")),
        new StackPanel().Vertical().Spacing(4).Children(
            new CheckBox().Content("_Remember me"), new CheckBox().Content("_Auto-save")),
        new StackPanel().Vertical().Spacing(4).Children(
            new RadioButton().Content("_Small").GroupName("size"),
            new RadioButton().Content("_Medium").GroupName("size"),
            new RadioButton().Content("_Large").GroupName("size")));
}

void EnableWindowDrag(Window dragWindow, UIElement element)
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
                dragWindow.ReleaseMouseCapture();
            }
            return;
        }

        dragging = true;
        dragStartScreenDip = GetScreenDip(dragWindow, e);
        windowStartDip = dragWindow.Position;

        dragWindow.CaptureMouse(element);
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
            dragWindow.ReleaseMouseCapture();
            return;
        }

        var screenDip = GetScreenDip(dragWindow, e);
        var dx = screenDip.X - dragStartScreenDip.X;
        var dy = screenDip.Y - dragStartScreenDip.Y;

        dragWindow.MoveTo(windowStartDip.X + dx, windowStartDip.Y + dy);
        e.Handled = true;
    };

    element.MouseUp += e =>
    {
        if (e.Button != MouseButton.Left || !dragging)
        {
            return;
        }

        dragging = false;
        dragWindow.ReleaseMouseCapture();
        e.Handled = true;
    };

    static Point GetScreenDip(Window dragWindow, MouseEventArgs e)
    {
        var screen = dragWindow.ClientToScreen(e.GetPosition(dragWindow));
        var scale = Math.Max(1.0, dragWindow.DpiScale);
        if (OperatingSystem.IsMacOS())
        {
            return new Point(screen.X / scale, -screen.Y / scale);
        }

        return new Point(screen.X / scale, screen.Y / scale);
    }
}

MenuBar CreateMenu(Action<string> onShortcut)
{
    var p = ModifierKeys.Primary;
    var fileMenu = new Menu()
        .Item("_New", () => onShortcut("File > New"), shortcut: new KeyGesture(Key.N, p))
        .Item("_Open...", () => onShortcut("File > Open"), shortcut: new KeyGesture(Key.O, p))
        .Item("_Save", () => onShortcut("File > Save"), shortcut: new KeyGesture(Key.S, p))
        .Item("Save _As...", () => onShortcut("File > Save As"))
        .Separator()
        .SubMenu("_Export", new Menu()
            .Item("_PNG", () => onShortcut("File > Export > PNG"))
            .Item("_JPEG", () => onShortcut("File > Export > JPEG"))
            .SubMenu("_Advanced", new Menu()
                .Item("With _metadata", () => onShortcut("File > Export > Advanced > Include metadata"))
                .Item("_Optimized", () => onShortcut("File > Export > Advanced > Optimized output"))))
        .Separator()
        .Item("E_xit", () => onShortcut("File > Exit"));
    var editMenu = new Menu()
        .Item("_Undo", () => onShortcut("Edit > Undo"), shortcut: new KeyGesture(Key.Z, p))
        .Item("_Redo", () => onShortcut("Edit > Redo"), shortcut: new KeyGesture(Key.Y, p))
        .Separator()
        .Item("Cu_t", () => onShortcut("Edit > Cut"), shortcut: new KeyGesture(Key.X, p))
        .Item("_Copy", () => onShortcut("Edit > Copy"), shortcut: new KeyGesture(Key.C, p))
        .Item("_Paste", () => onShortcut("Edit > Paste"), shortcut: new KeyGesture(Key.V, p));
    var viewMenu = new Menu()
        .Item("_Toggle Sidebar", () => onShortcut("View > Toggle Sidebar"))
        .SubMenu("_Zoom", new Menu()
            .Item("Zoom _In", () => onShortcut("View > Zoom In"), shortcut: new KeyGesture(Key.Add, p))
            .Item("Zoom _Out", () => onShortcut("View > Zoom Out"), shortcut: new KeyGesture(Key.Subtract, p))
            .Item("_Reset", () => onShortcut("View > Zoom Reset"), shortcut: new KeyGesture(Key.D0, p)));
    return new MenuBar().Height(28).Items(
        new MenuItem("_File").Menu(fileMenu),
        new MenuItem("_Edit").Menu(editMenu),
        new MenuItem("_View").Menu(viewMenu));
}

async void ShowBusyDemo(bool cancellable)
{
    using var busy = window.CreateBusyIndicator("Initializing...", cancellable);
    try
    {
        for (int i = 1; i <= 5; i++)
        {
            await Task.Delay(1000, busy.CancellationToken);
            busy.NotifyProgress($"Step {i} of 5...");
        }
        await Task.Delay(500, busy.CancellationToken);
        window.ShowToast("Operation completed!");
    }
    catch (OperationCanceledException)
    {
        window.ShowToast("Operation aborted.");
    }
}

// ═══════════════════════════════════════════════════════════════════════
// Custom classes
// ═══════════════════════════════════════════════════════════════════════

sealed record DemoUser(int Id, string Name, string Role, bool IsOnline);
sealed record SimpleGridRow(int Id, string Name, string Status);
sealed record ChatMessage(long Id, string Sender, string Text, bool Mine, DateTimeOffset Time);
sealed record ImageResourceEntry(string Name, string Url, ObservableValue<IImageSource?> Target);
sealed record TextResourceEntry(string Name, string Url, ObservableValue<string?> Target);
sealed record ImageResourceResult(ImageResourceEntry Resource, ImageSource? Image, string? error);
sealed record TextResourceResult(TextResourceEntry Resource, string? Text, string? error);

sealed class IconItem(string name, string pathData)
{
    public string Name { get; } = name;
    PathGeometry? geometry;
    public PathGeometry Geometry => geometry ??= PathGeometry.Parse(pathData);
}

static partial class IconResource
{
    public sealed record IconEntry(string Name, string PathData);

    static string? lastXaml;
    static IconEntry[] cached = [];

    public static IconEntry[] GetAll(string? xaml)
    {
        if (string.IsNullOrWhiteSpace(xaml))
        {
            return [];
        }

        if (string.Equals(lastXaml, xaml, StringComparison.Ordinal))
        {
            return cached;
        }

        var list = new List<IconEntry>();
        Load(xaml, list);
        lastXaml = xaml;
        cached = [.. list];
        return cached;
    }

    static void Load(string xaml, List<IconEntry> list)
    {
        foreach (Match m in ContentRegex().Matches(xaml))
        {
            var key = m.Groups[1].Value;
            var data = Normalize(m.Groups[2].Value);
            if (data.Length > 0)
            {
                list.Add(new IconEntry(key, data));
            }
        }

        foreach (Match m in FiguresRegex().Matches(xaml))
        {
            var key = m.Groups[1].Value;
            var data = Normalize(m.Groups[2].Value);
            if (data.Length > 0 && !list.Exists(e => e.Name == key))
            {
                list.Add(new IconEntry(key, data));
            }
        }

        list.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.Ordinal));
    }

    static string Normalize(string data) => WhitespaceRegex().Replace(data.Trim(), " ");

    [GeneratedRegex(@"<PathGeometry\s+x:Key=""([^""]+)""[^>]*(?<!/)>\s*([\s\S]*?)\s*</PathGeometry>", RegexOptions.Compiled)]
    private static partial Regex ContentRegex();

    [GeneratedRegex(@"<PathGeometry\s+x:Key=""([^""]+)""[^>]*\sFigures=""([^""]+)""", RegexOptions.Compiled)]
    private static partial Regex FiguresRegex();

    [GeneratedRegex(@"\s+", RegexOptions.Compiled)]
    private static partial Regex WhitespaceRegex();
}

static class GalleryView
{
    public static MenuBar CreateMenu(Action<string> OnShortcut)
    {
        var p = ModifierKeys.Primary;
        var fileMenu = new Menu()
            .Item("_New", () => OnShortcut("File > New document created"), shortcut: new KeyGesture(Key.N, p))
            .Item("_Open...", () => OnShortcut("File > Open file dialog"), shortcut: new KeyGesture(Key.O, p))
            .Item("_Save", () => OnShortcut("File > Document saved"), shortcut: new KeyGesture(Key.S, p))
            .Item("Save _As...", () => OnShortcut("File > Save As dialog"))
            .Separator()
            .SubMenu("_Export", new Menu()
                .Item("_PNG", () => OnShortcut("File > Export > PNG format"))
                .Item("_JPEG", () => OnShortcut("File > Export > JPEG format"))
                .SubMenu("_Advanced", new Menu()
                    .Item("With _metadata", () => OnShortcut("File > Export > Advanced > Include metadata"))
                    .Item("_Optimized", () => OnShortcut("File > Export > Advanced > Optimized output"))
                )
            )
            .Separator()
            .Item("E_xit", () => OnShortcut("File > Exit application"));

        var editMenu = new Menu()
            .Item("_Undo", () => OnShortcut("Edit > Undo last action"), shortcut: new KeyGesture(Key.Z, p))
            .Item("_Redo", () => OnShortcut("Edit > Redo last action"), shortcut: new KeyGesture(Key.Y, p))
            .Separator()
            .Item("Cu_t", () => OnShortcut("Edit > Cut to clipboard"), shortcut: new KeyGesture(Key.X, p))
            .Item("_Copy", () => OnShortcut("Edit > Copy to clipboard"), shortcut: new KeyGesture(Key.C, p))
            .Item("_Paste", () => OnShortcut("Edit > Paste from clipboard"), shortcut: new KeyGesture(Key.V, p))
            .Separator()
            .SubMenu("_Find", new Menu()
                .Item("_Find...", () => OnShortcut("Edit > Find > Open find dialog"), shortcut: new KeyGesture(Key.F, p))
                .Item("Find _Next", () => OnShortcut("Edit > Find > Find next occurrence"), shortcut: new KeyGesture(Key.F3))
                .Item("_Replace...", () => OnShortcut("Edit > Find > Open replace dialog"), shortcut: new KeyGesture(Key.H, p))
            );

        var viewMenu = new Menu()
            .Item("_Toggle Sidebar", () => OnShortcut("View > Toggle sidebar visibility"))
            .SubMenu("_Zoom", new Menu()
                .Item("Zoom _In", () => OnShortcut("View > Zoom > Zoom in"), shortcut: new KeyGesture(Key.Add, p))
                .Item("Zoom _Out", () => OnShortcut("View > Zoom > Zoom out"), shortcut: new KeyGesture(Key.Subtract, p))
                .Item("_Reset", () => OnShortcut("View > Zoom > Reset to 100%"), shortcut: new KeyGesture(Key.D0, p))
            );
        var menu = new MenuBar()
                            .Height(28)
                            .Items(
                                new MenuItem("_File").Menu(fileMenu),
                                new MenuItem("_Edit").Menu(editMenu),
                                new MenuItem("_View").Menu(viewMenu)
                            );
        return menu;
    }
}

sealed class ComplexGridRow
{
    public ComplexGridRow(int id, string name, double amount, bool hasError, bool isActive)
    {
        Id = id; Name = name;
        Amount = new ObservableValue<double>(amount, v => double.IsNaN(v) || double.IsInfinity(v) ? 0 : Math.Clamp(v, 0, 100));
        HasError = new ObservableValue<bool>(hasError);
        IsActive = new ObservableValue<bool>(isActive);
        StatusText = new ObservableValue<string>(string.Empty);
        void Recompute() => StatusText.Value = !IsActive.Value ? "Inactive" : HasError.Value ? "Error" : "OK";
        HasError.Changed += Recompute; IsActive.Changed += Recompute; Recompute();
    }
    public int Id { get; }
    public string Name { get; }
    public ObservableValue<double> Amount { get; }
    public ObservableValue<bool> HasError { get; }
    public ObservableValue<bool> IsActive { get; }
    public ObservableValue<string> StatusText { get; }
}

// ═══════════════════════════════════════════════════════════════════════
// NativeCustomWindow (from Gallery)
// ═══════════════════════════════════════════════════════════════════════

internal class NativeCustomWindowSample : NativeCustomWindow
{
    static readonly PathGeometry LightIcon = PathGeometry.Parse(
        @"M8.462,15.537C7.487,14.563,7,13.383,7,12c0-1.383,0.487-2.563,1.462-3.538S10.617,7,12,7
            c1.383,0,2.563,0.487,3.537,1.462C16.513,9.438,17,10.617,17,12c0,1.383-0.487,2.563-1.463,3.537C14.563,16.513,13.383,17,12,17
            C10.617,17,9.438,16.513,8.462,15.537z M5,13H1v-2h4V13z M23,13h-4v-2h4V13z M11,5V1h2v4H11z M11,23v-4h2v4H11z M6.4,7.75
            L3.875,5.325L5.3,3.85l2.4,2.5L6.4,7.75z M18.7,20.15l-2.425-2.525L17.6,16.25l2.525,2.425L18.7,20.15z M16.25,6.4l2.425-2.525
            L20.15,5.3l-2.5,2.4L16.25,6.4z M3.85,18.7l2.525-2.425L7.75,17.6l-2.425,2.525L3.85,18.7z");

    static readonly PathGeometry DarkIcon = PathGeometry.Parse(
        @"M12.058,19.904c-2.222,0-4.111-0.777-5.667-2.334c-1.556-1.555-2.333-3.444-2.333-5.667
            c0-2.025,0.66-3.782,1.981-5.27C7.359,5.147,8.994,4.269,10.942,4c0.054,0,0.106,0.002,0.159,0.006
            c0.052,0.004,0.103,0.009,0.153,0.017c-0.337,0.471-0.604,0.994-0.801,1.57s-0.295,1.18-0.295,1.811
            c0,1.778,0.622,3.289,1.867,4.533c1.244,1.245,2.755,1.867,4.533,1.867c0.635,0,1.239-0.099,1.813-0.296
            c0.574-0.195,1.09-0.463,1.549-0.801c0.007,0.051,0.013,0.102,0.017,0.154c0.004,0.051,0.006,0.104,0.006,0.158
            c-0.257,1.949-1.128,3.583-2.615,4.904C15.84,19.244,14.084,19.904,12.058,19.904z M12.058,18.904c1.467,0,2.784-0.404,3.95-1.213
            s2.017-1.863,2.55-3.162c-0.333,0.083-0.667,0.149-1,0.199c-0.333,0.051-0.667,0.075-1,0.075c-2.05,0-3.796-0.721-5.237-2.163
            C9.878,11.2,9.158,9.454,9.158,7.404c0-0.333,0.025-0.667,0.075-1c0.05-0.333,0.117-0.667,0.2-1c-1.3,0.533-2.354,1.383-3.163,2.55
            c-0.808,1.167-1.212,2.483-1.212,3.95c0,1.934,0.684,3.583,2.05,4.95C8.475,18.221,10.125,18.904,12.058,18.904z");

    readonly ObservableValue<string> _stateText = new();
    readonly ObservableValue<string> _capText = new();

    public NativeCustomWindowSample()
    {
        this.OnBuild(OnBuild)
            .Resizable(600, 400, minWidth: 400, minHeight: 250)
            .OnActivated(UpdateStateLabel)
            .OnDeactivated(UpdateStateLabel)
            .OnWindowStateChanged(_ => UpdateStateLabel())
            .OnSizeChanged(_ => UpdateStateLabel())
            .StartCenterOwner();
    }

    void UpdateStateLabel() =>
        _stateText.Value = $"WindowState: {WindowState} | IsActive: {IsActive} | Size: {ClientSize.Width:0}x{ClientSize.Height:0}";

    void OnBuild(NativeCustomWindowSample window)
    {
        TitleBarLeft.Add(
            GalleryView.CreateMenu(_ => { })
                .Apply(x => x.DrawBottomSeparator = false)
                .Background(Color.Transparent));

        var themeIcon = new PathShape()
            .Center()
            .Size(12)
            .Stretch(Stretch.Uniform)
            .WithTheme((t, s) => s.Data(t.IsDark ? LightIcon : DarkIcon).Fill(t.Palette.WindowText));

        var themeBtn = new Button()
            .Content(themeIcon)
            .CornerRadius(0)
            .StyleName("chrome")
            .MinWidth(36)
            .MinHeight(32);

        themeBtn.OnClick(() =>
        {
            var isDark = Application.Current.Theme.IsDark;
            Application.Current.SetTheme(isDark ? ThemeVariant.Light : ThemeVariant.Dark);
        });

        TitleBarRight.Add(themeBtn);

        window
            .Title("Native Chrome Demo")
            .OnBuild(x => x
                .Content(new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .Text("Native chrome: DWM frame (Win11) / fullSizeContentView (macOS).\nRounded corners and shadow preserved by the OS.")
                            .TextWrapping(TextWrapping.Wrap),
                        new Border()
                            .Padding(8)
                            .CornerRadius(4)
                            .Child(new StackPanel()
                                .Vertical()
                                .Spacing(6)
                                .Children(
                                    new TextBlock().Text("Window Properties").Bold(),
                                    BoolCheckBox(this, "CanMinimize", Window.CanMinimizeProperty),
                                    BoolCheckBox(this, "CanMaximize", Window.CanMaximizeProperty),
                                    BoolCheckBox(this, "CanClose", Window.CanCloseProperty),
                                    BoolCheckBox(this, "Topmost", Window.TopmostProperty),
                                    BoolCheckBox(this, "ShowInTaskbar", Window.ShowInTaskbarProperty),
                                    new StackPanel()
                                        .Horizontal()
                                        .Spacing(6)
                                        .Children(
                                            new Button().Content("Minimize")
                                                .OnClick(() => Minimize())
                                                .OnCanClick(() => WindowState == WindowState.Normal || WindowState == WindowState.Maximized),
                                            new Button().Content("Maximize")
                                                .OnClick(() => Maximize())
                                                .OnCanClick(() => WindowState == WindowState.Normal),
                                            new Button().Content("Restore")
                                                .OnClick(() => Restore())
                                                .OnCanClick(() => WindowState != WindowState.Normal)),
                                    new TextBlock().BindText(_stateText),
                                    new TextBlock().BindText(_capText))),
                        new TextBox(),
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new Button().Content("OK"),
                                new Button().Content("Close").OnClick(() => Close())))))
            .OnLoaded(() => _capText.Value = $"ChromeCapabilities: {window.ChromeCapabilities}");
    }

    static CheckBox BoolCheckBox(Window target, string label, MewProperty<bool> property)
    {
        bool initial = property == Window.CanMinimizeProperty ? target.CanMinimize
            : property == Window.CanMaximizeProperty ? target.CanMaximize
            : property == Window.CanCloseProperty ? target.CanClose
            : property == Window.TopmostProperty ? target.Topmost
            : property == Window.ShowInTaskbarProperty ? target.ShowInTaskbar
            : false;

        return new CheckBox()
            .Left()
            .IsChecked(initial)
            .Content(label)
            .OnCheckedChanged(v =>
            {
                bool val = v == true;
                if (property == Window.CanMinimizeProperty) target.CanMinimize = val;
                else if (property == Window.CanMaximizeProperty) target.CanMaximize = val;
                else if (property == Window.CanCloseProperty) target.CanClose = val;
                else if (property == Window.TopmostProperty) target.Topmost = val;
                else if (property == Window.ShowInTaskbarProperty) target.ShowInTaskbar = val;
            });
    }
}

// ═══════════════════════════════════════════════════════════════════════
// NativeCustomWindow base class (from Gallery)
// ═══════════════════════════════════════════════════════════════════════

class NativeCustomWindow : Window
{
    const double DefaultTitleBarHeight = 28;
    const double ButtonWidth = 46;
    const double ChromeButtonSize = 4;

    static readonly Style ChromeButtonStyle = new(typeof(Button))
    {
        Transitions = [Transition.Create(Control.BackgroundProperty)],
        Setters =
        [
            Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace.WithAlpha(0)),
            Setter.Create(Control.BorderThicknessProperty, 0.0),
            Setter.Create(Control.CornerRadiusProperty, 0.0),
            Setter.Create(Control.PaddingProperty, new Thickness(0)),
        ],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Hot,
                Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonFace)],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Pressed,
                Setters = [Setter.Create(Control.BackgroundProperty, t => t.Palette.ButtonPressedBackground)],
            },
        ],
    };

    static readonly Style CloseButtonStyle = new(typeof(Button))
    {
        Transitions = [Transition.Create(Control.BackgroundProperty)],
        Setters =
        [
            Setter.Create(Control.BackgroundProperty, Color.FromRgb(232, 17, 35).WithAlpha(0)),
            Setter.Create(Control.BorderThicknessProperty, 0.0),
            Setter.Create(Control.CornerRadiusProperty, 0.0),
            Setter.Create(Control.PaddingProperty, new Thickness(0)),
        ],
        Triggers =
        [
            new StateTrigger
            {
                Match = VisualStateFlags.Hot,
                Setters =
                [
                    Setter.Create(Control.BackgroundProperty, Color.FromRgb(232, 17, 35)),
                    Setter.Create(Control.ForegroundProperty, Color.White),
                ],
            },
            new StateTrigger
            {
                Match = VisualStateFlags.Pressed,
                Setters =
                [
                    Setter.Create(Control.BackgroundProperty, Color.FromRgb(200, 12, 28)),
                    Setter.Create(Control.ForegroundProperty, Color.White),
                ],
            },
        ],
    };

    readonly Border _contentArea;
    readonly Border _chromeBorder;
    readonly AlphaTextPanel _titleBar;
    readonly TextBlock _titleText;
    readonly StackPanel _controlButtons;
    readonly StackPanel _leftArea;
    readonly StackPanel _rightArea;
    readonly Button _minimizeBtn;
    readonly Button _maximizeBtn;

    protected override void OnMewPropertyChanged(MewProperty property)
    {
        base.OnMewPropertyChanged(property);

        if (ChromeCapabilities.HasFlag(WindowChromeCapabilities.NativeBorderColor)
            && property.Name == nameof(BorderBrush))
        {
            SetWindowBorderColor(BorderBrush);
        }
    }

    public NativeCustomWindow()
    {
        ExtendClientAreaTitleBarHeight = DefaultTitleBarHeight;
        base.Padding = new Thickness(0);

        StyleSheet = new StyleSheet();
        StyleSheet.Define("chrome", ChromeButtonStyle);
        StyleSheet.Define("close", CloseButtonStyle);

        var titleText = new TextBlock
        {
            IsHitTestVisible = false,
            FontWeight = FontWeight.SemiBold,
            FontSize = 13,
            Margin = new Thickness(8, 0),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        titleText.SetBinding(TextBlock.TextProperty, this, TitleProperty);
        _titleText = titleText;

        _minimizeBtn = CreateChromeButton(GlyphKind.Minus);
        _minimizeBtn.Click += () => Minimize();
        _minimizeBtn.SetBinding(UIElement.IsVisibleProperty, this, CanMinimizeProperty);

        var maxGlyph = new GlyphElement().Kind(GlyphKind.WindowMaximize).GlyphSize(ChromeButtonSize);
        _maximizeBtn = CreateChromeButton(maxGlyph);
        _maximizeBtn.Click += () =>
        {
            if (WindowState == WindowState.Maximized)
                Restore();
            else
                Maximize();
        };
        _maximizeBtn.SetBinding(UIElement.IsVisibleProperty, this, CanMaximizeProperty);

        var closeBtn = CreateChromeButton(GlyphKind.Cross, isClose: true);
        closeBtn.Click += () => Close();
        closeBtn.SetBinding(UIElement.IsVisibleProperty, this, CanCloseProperty);

        _controlButtons = new StackPanel { Orientation = Orientation.Horizontal };
        _controlButtons.Add(_minimizeBtn);
        _controlButtons.Add(_maximizeBtn);
        _controlButtons.Add(closeBtn);

        Activated += UpdateChromeButtonVisibility;

        _leftArea = new StackPanel { Orientation = Orientation.Horizontal };
        _rightArea = new StackPanel { Orientation = Orientation.Horizontal };

        var titleBarContent = new DockPanel().Children(
            new Border().DockRight().Child(_controlButtons),
            new Border().DockRight().Child(_rightArea),
            new Border().DockLeft().Child(_leftArea),
            titleText);
        _titleBar = new AlphaTextPanel
        {
            MinHeight = DefaultTitleBarHeight,
            Content = titleBarContent
        };
        _titleBar.SetBinding(BackgroundProperty, this, BackgroundProperty);

        _titleBar.MouseDoubleClick += e =>
        {
            if (e.Button == MouseButton.Left && CanMaximize)
            {
                if (WindowState == WindowState.Maximized) Restore();
                else Maximize();
                e.Handled = true;
            }
        };

        _contentArea = new Border { Padding = new Thickness(16) };

        _chromeBorder = new Border
        {
            BorderThickness = 0,
            Child = new DockPanel().Children(
                _titleBar.DockTop(),
                _contentArea
            ),
        };
        _chromeBorder.SetBinding(Border.BorderBrushProperty, this, BorderBrushProperty);
        base.Content = _chromeBorder;

        ClientSizeChanged += _ =>
        {
            OnWindowStateVisualUpdate();
            UpdateChromeButtonVisibility();
        };

        Activated += UpdateChromeAppearance;
        Deactivated += UpdateChromeAppearance;
        Loaded += OnLoaded;
        this.WithTheme((_, _) => UpdateChromeAppearance());
    }

    void OnLoaded()
    {
        if (BorderBrush.A > 0 && !ChromeCapabilities.HasFlag(WindowChromeCapabilities.NativeBorderColor)
                               && !ChromeCapabilities.HasFlag(WindowChromeCapabilities.NativeWindowBorder))
        {
            _chromeBorder.BorderThickness = 1;
        }
    }

    public StackPanel TitleBarLeft => _leftArea;
    public StackPanel TitleBarRight => _rightArea;

    public new UIElement? Content
    {
        get => _contentArea.Child;
        set => _contentArea.Child = value;
    }

    public new Thickness Padding
    {
        get => _contentArea.Padding;
        set => _contentArea.Padding = value;
    }

    void UpdateChromeAppearance()
    {
        var p = Theme.Palette;
        var accentBorder = IsActive ? p.Accent : p.ControlBorder;

        BorderBrush = accentBorder;
        _titleText.Foreground = IsActive ? p.WindowText : p.DisabledText;
    }

    void UpdateChromeButtonVisibility()
    {
        bool hasExtend = ChromeCapabilities.HasFlag(WindowChromeCapabilities.ExtendClientArea);
        _titleBar.IsVisible = hasExtend;
        _controlButtons.IsVisible = !HasNativeChromeButtons;
        _titleBar.Padding = NativeChromeButtonInset;
    }

    void OnWindowStateVisualUpdate()
    {
        bool maximized = WindowState == WindowState.Maximized;
        if (_maximizeBtn.Content is GlyphElement glyph)
            glyph.Kind = maximized ? GlyphKind.WindowRestore : GlyphKind.WindowMaximize;
    }

    static Button CreateChromeButton(GlyphKind kind, bool isClose = false)
    {
        var glyph = new GlyphElement().Kind(kind).GlyphSize(ChromeButtonSize);
        return CreateChromeButton(glyph, isClose);
    }

    static Button CreateChromeButton(Element content, bool isClose = false)
    {
        return new Button
        {
            Content = content,
            MinWidth = ButtonWidth,
            MinHeight = DefaultTitleBarHeight,
            StyleName = isClose ? "close" : "chrome",
        };
    }

    internal sealed class AlphaTextPanel : ContentControl
    {
        protected override void RenderSubtree(IGraphicsContext context)
        {
            context.EnableAlphaTextHint = true;
            try
            {
                base.RenderSubtree(context);
            }
            finally
            {
                context.EnableAlphaTextHint = false;
            }
        }
    }
}

internal static class NativeCustomWindowExtensions
{
    public static NativeCustomWindow Content(this NativeCustomWindow w, UIElement? content)
    {
        w.Content = content;
        return w;
    }
}

// ═══════════════════════════════════════════════════════════════════════
// ConfettiOverlay (from Gallery — port of WpfConfetti by caefale)
// ═══════════════════════════════════════════════════════════════════════

sealed class ConfettiOverlay : FrameworkElement
{
    enum PShape { Rect, Ellipse, Tri }
    struct Particle
    {
        public double X, Y, BaseX, BaseY, VX, VY, Size, Drag;
        public double WobbleAmp, WobblePhase, WobbleFreq, Age, Rotation, RotationSpeed, Gravity;
        public Color Color; public PShape Shape; public bool IsWide;
    }
    struct CannonBatch { public int Remaining; public double MinSpeed, MaxSpeed, Gravity, MinSize, MaxSize, Spread, Rate; public Color[]? Colors; }

    static readonly Color[] DefaultColors = [
        new(255,255,107,107), new(255,255,213,0), new(255,164,212,0),
        new(255,62,223,211), new(255,84,175,255), new(255,200,156,255)];

    readonly List<Particle> _p = new();
    readonly Queue<CannonBatch> _cq = new();
    AnimationClock? _clock; long _lastTs; double _cannonAcc;
    bool _isRaining; double _rainAcc, _rainRate = 80, _rainMinSpd = 60, _rainMaxSpd = 120, _rainMinSz = 2, _rainMaxSz = 5, _rainGrav = 85;
    Color[]? _rainColors;
    static readonly Random Rng = new();

    public ConfettiOverlay() { IsHitTestVisible = false; }

    public void Burst(int n = 75, Point? pos = null, double minSpd = 50, double maxSpd = 300, double minSz = 3, double maxSz = 5, double g = 85, Color[]? c = null)
    {
        var b = Bounds;
        var p = pos ?? new Point(b.Width / 2, b.Height / 2);
        for (int i = 0; i < n; i++) Spawn(p, 0, 360, minSpd, maxSpd, g, minSz, maxSz, 90, c);
        EnsureTimer();
    }

    public void Cannons(int n = 500, double rate = 75, double spread = 15, double minSpd = 300, double maxSpd = 500, double minSz = 2, double maxSz = 5, double g = 120, Color[]? c = null)
    {
        _cq.Enqueue(new CannonBatch { Remaining = n, MinSpeed = minSpd, MaxSpeed = maxSpd, Gravity = g, MinSize = minSz, MaxSize = maxSz, Spread = spread, Rate = rate, Colors = c });
        EnsureTimer();
    }

    public void StartRain(double rate = 80, double minSpd = 60, double maxSpd = 120, double minSz = 2, double maxSz = 5, double g = 85, Color[]? c = null)
    { _isRaining = true; _rainRate = rate; _rainMinSpd = minSpd; _rainMaxSpd = maxSpd; _rainMinSz = minSz; _rainMaxSz = maxSz; _rainGrav = g; _rainColors = c; EnsureTimer(); }

    public void StopRain() => _isRaining = false;
    public void StopCannons() { _cq.Clear(); _cannonAcc = 0; }
    public new void Clear() { _isRaining = false; _cq.Clear(); _cannonAcc = 0; _p.Clear(); StopTimer(); InvalidateVisual(); }

    protected override void OnRender(IGraphicsContext ctx)
    {
        var path = new PathGeometry();
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_p);
        for (int i = 0; i < span.Length; i++)
        {
            ref readonly var p = ref span[i];
            double w = p.IsWide ? p.Size * 2 : p.Size / 2, h = p.IsWide ? p.Size / 2 : p.Size * 2;
            double cx = p.X + w / 2, cy = p.Y + h / 2, rad = p.Rotation * Math.PI / 180, cos = Math.Cos(rad), sin = Math.Sin(rad);
            switch (p.Shape)
            {
                case PShape.Rect: path.Clear(); RotRect(path, cx, cy, w, h, cos, sin); ctx.FillPath(path, p.Color); break;
                case PShape.Ellipse: double r = p.Size / 2; ctx.FillEllipse(new Rect(cx - r, cy - r, r * 2, r * 2), p.Color); break;
                case PShape.Tri: path.Clear(); RotTri(path, cx, cy, p.Size, cos, sin); ctx.FillPath(path, p.Color); break;
            }
        }
    }

    void EnsureTimer() { if (_clock != null) return; _lastTs = System.Diagnostics.Stopwatch.GetTimestamp(); _clock = new AnimationClock(TimeSpan.FromSeconds(1)) { RepeatCount = -1 }; _clock.TickCallback = OnTick; _clock.Start(); }
    void StopTimer() { if (_clock == null) return; _clock.TickCallback = null; _clock.Stop(); _clock = null; }

    void OnTick(double _)
    {
        long now = System.Diagnostics.Stopwatch.GetTimestamp();
        double dt = System.Diagnostics.Stopwatch.GetElapsedTime(_lastTs, now).TotalSeconds; _lastTs = now;
        if (dt <= 0 || dt > 0.5) dt = 0.016;
        var b = Bounds; double w = b.Width, h = b.Height;
        if (w <= 0 || h <= 0) return;
        if (_isRaining) { _rainAcc += dt; double iv = 1.0 / _rainRate; while (_rainAcc >= iv) { Spawn(new Point(Rng.NextDouble() * w, -10), 85, 95, _rainMinSpd, _rainMaxSpd, _rainGrav, _rainMinSz, _rainMaxSz, 0, _rainColors); _rainAcc -= iv; } }
        if (_cq.Count > 0) { _cannonAcc += dt; while (_cq.Count > 0) { var batch = _cq.Peek(); double iv = 1.0 / batch.Rate; if (_cannonAcc < iv) break; SpawnCannon(new Point(0, h), batch, w, h); SpawnCannon(new Point(w, h), batch, w, h); _cannonAcc -= iv; batch.Remaining -= 2; if (batch.Remaining <= 0) { _cq.Dequeue(); _cannonAcc = 0; } } }
        Update(dt, h);
        if (_p.Count == 0 && !_isRaining && _cq.Count == 0) StopTimer();
        InvalidateVisual();
    }

    void Update(double dt, double areaH)
    {
        var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_p); double killY = areaH + 50; int alive = span.Length;
        for (int i = 0; i < alive; i++) { ref var p = ref span[i]; p.Age += dt; p.BaseX += p.VX * dt; p.BaseY += p.VY * dt; p.VY += p.Gravity * dt; double drag = Math.Pow(p.Drag, dt); p.VX *= drag; p.VY *= drag; p.RotationSpeed *= drag; double ws = Math.Clamp(p.Age * 1.5, 0, 1); p.X = p.BaseX + Math.Sin(p.Age * p.WobbleFreq + p.WobblePhase) * p.WobbleAmp * ws; p.Y = p.BaseY; p.Rotation += p.RotationSpeed * dt; if (p.Y > killY) { alive--; if (i < alive) { span[i] = span[alive]; i--; } } }
        if (alive < _p.Count) _p.RemoveRange(alive, _p.Count - alive);
    }

    void SpawnCannon(Point pos, CannonBatch batch, double aw, double ah)
    { double tx = aw / 2 + (Rng.NextDouble() - 0.5) * 80, ty = ah * 0.35, dx = tx - pos.X, dy = ty - pos.Y, len = Math.Sqrt(dx * dx + dy * dy); if (len > 0) { dx /= len; dy /= len; } double ba = Math.Atan2(dy, dx) * 180 / Math.PI, ss = ah / 400; Spawn(pos, ba - batch.Spread, ba + batch.Spread, batch.MinSpeed * ss, batch.MaxSpeed * ss, batch.Gravity, batch.MinSize, batch.MaxSize, 0, batch.Colors); }

    void Spawn(Point pos, double minA, double maxA, double minSpd, double maxSpd, double g, double minSz, double maxSz, int adj = 0, Color[]? c = null)
    { double a = (minA + Rng.NextDouble() * (maxA - minA) - adj) * Math.PI / 180; double spd = minSpd + Rng.NextDouble() * (maxSpd - minSpd); double sr = Rng.NextDouble(); var cl = c ?? DefaultColors; _p.Add(new Particle { X = pos.X, Y = pos.Y, BaseX = pos.X, BaseY = pos.Y, VX = Math.Cos(a) * spd, VY = Math.Sin(a) * spd, Size = minSz + Rng.NextDouble() * (maxSz - minSz), Color = cl[Rng.Next(cl.Length)], Shape = sr < 0.7 ? PShape.Rect : sr < 0.95 ? PShape.Ellipse : PShape.Tri, Drag = 0.65 + Rng.NextDouble() * 0.3, IsWide = Rng.Next(2) == 0, WobbleAmp = 2 + Rng.NextDouble() * 6, WobbleFreq = 1 + Rng.NextDouble() * 3, WobblePhase = Rng.NextDouble() * Math.PI * 2, Rotation = Rng.NextDouble() * 360, RotationSpeed = (Rng.NextDouble() - 0.5) * 2 * (10 + Rng.NextDouble() * 300), Gravity = g }); }

    static void RotRect(PathGeometry p, double cx, double cy, double w, double h, double cos, double sin)
    { double hw = w / 2, hh = h / 2; Span<double> lx = stackalloc double[] { -hw, hw, hw, -hw }; Span<double> ly = stackalloc double[] { -hh, -hh, hh, hh }; for (int i = 0; i < 4; i++) { double rx = lx[i] * cos - ly[i] * sin + cx, ry = lx[i] * sin + ly[i] * cos + cy; if (i == 0) p.MoveTo(rx, ry); else p.LineTo(rx, ry); } p.Close(); }

    static void RotTri(PathGeometry p, double cx, double cy, double sz, double cos, double sin)
    { Span<double> lx = stackalloc double[] { 0, sz, -sz }; Span<double> ly = stackalloc double[] { -sz, sz, sz }; for (int i = 0; i < 3; i++) { double rx = lx[i] * cos - ly[i] * sin + cx, ry = lx[i] * sin + ly[i] * cos + cy; if (i == 0) p.MoveTo(rx, ry); else p.LineTo(rx, ry); } p.Close(); }
}
