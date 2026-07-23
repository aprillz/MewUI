#:sdk Microsoft.NET.Sdk
#:property OutputType=WinExe
#:property TargetFramework=net10.0
#:property PublishAot=true
#:property TrimMode=full
#:package Aprillz.MewUI@0.19.1

using System.Collections.ObjectModel;
using System.Numerics;
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
var soondukResource = new ObservableValue<IImageSource?>(null);
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
    new("soonduk.jpg", AssetsBase + "images/soonduk.jpg", soondukResource),
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
var themeMode = new ObservableValue<ThemeVariant>(ThemeVariant.System);
var fpsText = new ObservableValue<string>("FPS: -");
var cullText = new ObservableValue<string>("Cull: -");
var fpsStopwatch = new System.Diagnostics.Stopwatch();
var fpsFrames = 0;
var backendText = new TextBlock();
var themeText = new TextBlock();
var maxFpsEnabled = new ObservableValue<bool>(false);
var cardBorders = new List<Border>();

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
        .Padding(0)
        .Apply(_ => window.AllowDrop = true)
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
            Application.Current.ThemeModeChanged += () => themeMode.Value = Application.Current.ThemeMode;
            StartResourceLoading();
            UpdateTopBar();
            timer.Start();
        })
        .OnClosed(() => maxFpsEnabled.Value = false)
        .OnFrameRendered(() =>
        {
            if (!fpsStopwatch.IsRunning) { fpsStopwatch.Restart(); fpsFrames = 0; return; }
            fpsFrames++;
            var stats = window.LastFrameStats;
            cullText.Value = $"Draw: {stats.DrawCalls} | Cull: {stats.CullCount} ({stats.CullRatio:P0})";
        })
        .Content(new DockPanel().Children(
            TopBar().DockTop(),
            BuildNavigationShell())));

// ═══════════════════════════════════════════════════════════════════════
// Gallery
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement TopBar()
{
    var logoImage = BindResourceImage(
        new Image().ImageScaleQuality(ImageScaleQuality.HighQuality).Width(200).CenterVertical(),
        logoResource);

    return new Border().Padding(12, 10).BorderThickness(1).Child(
        new DockPanel().Spacing(12).Children(
            new StackPanel().Horizontal().CenterVertical().Spacing(8).Children(
                logoImage,
                new StackPanel().Vertical().Spacing(2).Children(
                    new TextBlock().Text("MewUI Gallery (FBA)").WithTheme((t, c) => c.Foreground(t.Palette.Accent)).FontSize(18).SemiBold(),
                    backendText,
                    new TextBlock().BindText(resourceStatus).FontSize(11))).DockLeft(),
            new StackPanel().Horizontal().CenterVertical().Spacing(12).Children(
                new CheckBox().Content("Max FPS").BindIsChecked(maxFpsEnabled).OnCheckedChanged(_ => EnsureMaxFpsLoop()).CenterVertical(),
                new TextBlock().BindText(fpsText).CenterVertical(),
                new TextBlock().BindText(cullText).CenterVertical())));
}

FrameworkElement BuildNavigationShell()
{
    var entries = NavEntries();
    var navigation = new NavigationView { PaneWidth = 220 };

    Element? PageContent(NavEntry entry) => entry.Page is not null
        ? new ScrollViewer()
            .VerticalScroll(ScrollMode.Auto)
            .Padding(8)
            .Content(entry.Page())
        : null;

    navigation.Items(
        entries,
        entry => entry.Title,
        icon: entry => entry.Icon,
        content: PageContent,
        kind: entry => entry.Kind);

    var footer = new[]
    {
        new NavEntry(
            NavigationItemKind.Item,
            "Settings",
            NavigationIcons.Settings,
            SettingsPage),
    };
    navigation.FooterItems(
        footer,
        entry => entry.Title,
        icon: entry => entry.Icon,
        content: PageContent,
        kind: entry => entry.Kind);

    navigation.SelectedIndex = Array.FindIndex(entries, entry => entry.Kind == NavigationItemKind.Item);

    return new Border()
        .BorderThickness(new Thickness(0, 1, 0, 0))
        .WithTheme((theme, border) => border.BorderBrush(
            theme.Palette.WindowBackground.Lerp(theme.Palette.ControlBorder, 0.45)))
        .Child(navigation);
}

FrameworkElement SettingsPage() => new StackPanel()
    .Vertical()
    .Margin(16)
    .Spacing(16)
    .Children(
        new TextBlock().Text("Settings").FontSize(22).Bold(),

        new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().Text("Theme").FontSize(14).Bold(),
            new StackPanel().Horizontal().CenterVertical().Spacing(12).Children(
                ThemeModePicker(),
                themeText.CenterVertical())),

        new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().Text("Accent").FontSize(14).Bold(),
            AccentPicker()),

        new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().Text("Rendering").FontSize(14).Bold(),
            new CheckBox()
                .Content("Cached")
                .IsChecked(true)
                .OnCheckedChanged(value => SetCardsCached(value == true))
                .CenterVertical()),

        new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().Text("Resources").FontSize(14).Bold(),
            new TextBlock().BindText(resourceDetail).TextWrapping(TextWrapping.Wrap)));

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
            .OnChecked(() => Application.Current.SetTheme(ThemeVariant.Dark)));

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
    .WithTheme((theme, button) => button.Background(accent.GetAccentColor(theme.IsDark)))
    .ToolTip(accent.ToString())
    .OnClick(() =>
    {
        currentAccent = accent;
        Application.Current.SetAccent(accent);
        UpdateTopBar();
    });

NavEntry[] NavEntries()
{
    NavEntry Group(string title) => new(NavigationItemKind.Header, title, null, null);
    NavEntry Page(string title, Func<FrameworkElement> page, PathGeometry icon) =>
        new(NavigationItemKind.Item, title, icon, page);


    return
    [
        Group("Basics"),
        Page("Buttons", ButtonsPage, NavigationIcons.Buttons),
        Page("Inputs", InputsPage, NavigationIcons.Inputs),
        Page("Drag & Drop", DragDropPage, AdditionalNavigationIcons.DragDrop),
        Page("Selection", SelectionPage, NavigationIcons.Selection),
        Page("Typography", TypographyPage, NavigationIcons.Typography),

        Group("Collections"),
        Page("Lists", ListsPage, NavigationIcons.Lists),
        Page("TreeView", TreeViewPage, AdditionalNavigationIcons.TreeView),
        Page("GridView", GridViewPage, NavigationIcons.GridView),
        Page("ItemsControl", ItemsControlPage, AdditionalNavigationIcons.ItemsControl),

        Group("Layout"),
        Page("Panels", PanelsPage, NavigationIcons.Panels),
        Page("Layout", LayoutPage, NavigationIcons.Layout),
        Page("Transform", TransformPage, AdditionalNavigationIcons.Transform),

        Group("Graphics"),
        Page("Shapes", ShapesPage, NavigationIcons.Shapes),
        Page("Icons", IconsPage, NavigationIcons.Icons),
        Page("Media", MediaPage, NavigationIcons.Media),
        Page("Custom Rendering", CustomRenderingPage, AdditionalNavigationIcons.CustomRendering),
        Page("Transitions", TransitionsPage, NavigationIcons.Transitions),

        Group("Windowing"),
        Page("Window", WindowPage, NavigationIcons.WindowMenu),
        Page("Menu", MenuPage, AdditionalNavigationIcons.Menu),
        Page("MessageBox", MessageBoxPage, NavigationIcons.MessageBox),
        Page("File Dialog", FileDialogPage, AdditionalNavigationIcons.FileDialog),
        Page("ShowDialog", ShowDialogPage, AdditionalNavigationIcons.ShowDialog),
        Page("Overlay", OverlayPage, NavigationIcons.Overlay),
    ];
}

// ── Helpers ──
void UpdateTopBar()
{
    backendText.Text($"Backend: {Application.Current.GraphicsFactory.Backend}");
    themeText.WithTheme((t, c) => c.Text($"Theme: {t.Name}"));
}

void SetCardsCached(bool cached)
{
    foreach (var border in cardBorders)
    {
        border.CacheMode = cached ? new BitmapCache() : null;
    }
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

FrameworkElement Card(string title, FrameworkElement content, double minWidth = 320)
{
    var border = new Border()
        .MinWidth(minWidth)
        .Padding(14)
        .CornerRadius(10)
        .Cached()
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
    cardBorders.Add(border);
    return border;
}

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

        Card("ButtonGroup", new StackPanel().Vertical().Spacing(8).Children(
            SegmentRow("Text", new ButtonGroup().Items("Cut", "Copy", "Paste").Left()),
            SegmentRow("Text + Icon", SegmentIconTextGroup(
                new SegmentItem("cut_regular", "Cut"),
                new SegmentItem("copy_regular", "Copy"),
                new SegmentItem("clipboard_paste_regular", "Paste")).Left()),
            SegmentRow("Icon", SegmentIconGroup(
                new SegmentItem("text_align_left_regular", "Left"),
                new SegmentItem("text_align_center_regular", "Center"),
                new SegmentItem("text_align_right_regular", "Right")).Left()),
            SegmentRow("Toggle", new ButtonGroup()
                .Items("Bold", "Italic", "Underline")
                .PrepareContainer<string>((segment, name, _) =>
                {
                    segment.IsCheckable = true;
                    if (name == "Italic") segment.IsChecked = true;
                }).Left()),
            SegmentRow("Uniform", new ButtonGroup()
                .Sizing(SegmentSizing.Uniform)
                .Items("Left", "Center", "Right").Left()),
            SegmentRow("Disabled", SegmentIconGroup(
                new SegmentItem("text_align_left_regular", "Left"),
                new SegmentItem("text_align_center_regular", "Center"),
                new SegmentItem("text_align_right_regular", "Right"))
                .Disable().Left()))),

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

        Card("Emoji", new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().Text("🍫 😀 🎉 🚀 🌈 🐍 🎵 ❤️ 🔥 💡").FontSize(24),
            new TextBlock().Text("🍫 😀 🎉 🚀 🌈 🐍 🎵 ❤️ 🔥 💡").FontSize(20),
            new TextBlock().Text("🍫 😀 🎉 🚀 🌈 🐍 🎵 ❤️ 🔥 💡").FontSize(16),
            new TextBlock().Text("🍫 😀 🎉 🚀 🌈 🐍 🎵 ❤️ 🔥 💡").FontSize(12),
            new TextBox().Placeholder("Type or paste emoji here...").Text("🍫😀🎉"),
            new TextBlock().Text("Mixed: Hello 🌍 World 🚀!").FontSize(14))),

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
                        .Item("Disabled", isEnabled: false))))
    );
}

FrameworkElement DragDropPage()
{
    const string dragFormat = "application/x-mewui-gallery.drag-card";
    var dragLog = new ObservableValue<string>("(no drag yet)");

    Border DragChip(string label)
    {
        var chip = new Border()
            .Padding(8, 4)
            .CornerRadius(4)
            .WithTheme((theme, border) => border.Background(
                theme.Palette.ButtonFace.Lerp(theme.Palette.Accent, 0.5)))
            .Child(new TextBlock().Text(label).FontSize(11));
        chip.CanDrag = true;
        chip.DragStarting += e =>
        {
            var data = new DataObject();
            data.SetData(dragFormat, label);
            data.SetText(label);
            e.Data = data;
            e.AllowedEffects = DragDropEffects.Move | DragDropEffects.Copy;
            e.Preview = new DragPreviewContent
            {
                Scope = DragPreviewScope.CrossWindow,
                Element = chip,
                Opacity = 0.7,
            };
            dragLog.Value = $"Starting drag for '{label}'";
        };
        chip.DragCompleted += e =>
        {
            dragLog.Value = $"Drag of '{label}' completed: {e.FinalEffect}" +
                (e.WasCanceled ? " (canceled)" : "");
            if (e.FinalEffect == DragDropEffects.Move && !e.WasCanceled && chip.Parent is StackPanel parent)
            {
                parent.Remove(chip);
            }
        };
        return chip;
    }

    Border DragSlot(string title, StackPanel content)
    {
        var insertionLine = new Border()
            .Height(2)
            .CornerRadius(1)
            .WithTheme((theme, border) => border.Background(theme.Palette.Accent));
        insertionLine.IsVisible = false;
        Canvas.SetLeft(insertionLine, 0);
        Canvas.SetRight(insertionLine, 0);

        Canvas.SetLeft(content, 0);
        Canvas.SetTop(content, 0);
        Canvas.SetRight(content, 0);

        var dropCanvas = new Canvas().MinHeight(100);
        dropCanvas.Add(content);
        dropCanvas.Add(insertionLine);
        var pendingInsertIndex = 0;

        var slot = new Border()
            .MinWidth(140)
            .Padding(8)
            .CornerRadius(6)
            .BorderThickness(1)
            .WithTheme((theme, border) => border
                .Background(theme.Palette.ContainerBackground)
                .BorderBrush(theme.Palette.ControlBorder))
            .Child(new StackPanel().Vertical().Spacing(6).Children(
                new TextBlock().Text(title).Bold().FontSize(11),
                dropCanvas));
        slot.AllowDrop = true;

        void UpdateInsertionLine(Point cursorInWindow)
        {
            pendingInsertIndex = ComputeInsertIndex(content, cursorInWindow.Y);
            var lineY = ResolveInsertionLineY(content, dropCanvas, pendingInsertIndex);
            Canvas.SetTop(insertionLine, lineY - 1);
            insertionLine.IsVisible = true;
        }

        slot.DragEnter += e =>
        {
            e.Accepted = true;
            e.Effect = DragDropEffects.Move;
            UpdateInsertionLine(e.Position);
        };
        slot.DragOver += e =>
        {
            e.Accepted = true;
            e.Effect = DragDropEffects.Move;
            UpdateInsertionLine(e.Position);
        };
        slot.DragLeave += _ => insertionLine.IsVisible = false;
        slot.Drop += e =>
        {
            insertionLine.IsVisible = false;
            if (e.Data.TryGetData<string>(dragFormat, out var label) && label is not null)
            {
                var index = Math.Clamp(pendingInsertIndex, 0, content.Children.Count);
                content.Insert(index, DragChip(label));
                e.Accepted = true;
                e.Effect = DragDropEffects.Move;
                e.Handled = true;
                dragLog.Value = $"Dropped '{label}' on {title} at index {index}";
            }
        };
        return slot;
    }

    static int ComputeInsertIndex(StackPanel content, double cursorY)
    {
        var children = content.Children;
        for (var i = 0; i < children.Count; i++)
        {
            if (children[i] is UIElement child && cursorY < child.Bounds.Y + child.Bounds.Height / 2)
                return i;
        }
        return children.Count;
    }

    static double ResolveInsertionLineY(StackPanel content, Canvas canvas, int insertIndex)
    {
        var children = content.Children;
        var halfSpacing = content.Spacing / 2;
        double windowY;
        if (children.Count == 0)
            windowY = content.Bounds.Y;
        else if (insertIndex <= 0)
            windowY = ((UIElement)children[0]).Bounds.Y - halfSpacing;
        else if (insertIndex >= children.Count)
        {
            var last = (UIElement)children[^1];
            windowY = last.Bounds.Y + last.Bounds.Height + halfSpacing;
        }
        else
        {
            var above = (UIElement)children[insertIndex - 1];
            var below = (UIElement)children[insertIndex];
            windowY = (above.Bounds.Y + above.Bounds.Height + below.Bounds.Y) / 2;
        }
        return windowY - canvas.Bounds.Y;
    }

    var source = new StackPanel().Vertical().Spacing(4);
    foreach (var label in new[] { "Alpha", "Bravo", "Charlie" }) source.Add(DragChip(label));
    var target = new StackPanel().Vertical().Spacing(4);

    void OpenCrossWindowTarget()
    {
        var crossTarget = new StackPanel().Vertical().Spacing(4);
        crossTarget.Add(DragChip("Yellow"));
        Window second = null!;
        new Window().Ref(out second)
            .Resizable(360, 300)
            .StartCenterOwner()
            .Title("Cross-Window Drag & Drop")
            .Padding(16)
            .Content(new StackPanel().Vertical().Spacing(8).Children(
                new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
                    .Text("Drag chips here from the main window - or drag 'Yellow' back."),
                DragSlot("Drop here", crossTarget)))
            .Show(window);
    }

    var crossSource = new StackPanel().Vertical().Spacing(4);
    foreach (var label in new[] { "Red", "Green", "Blue" }) crossSource.Add(DragChip(label));

    return CardGrid(
        Card("Window Drag and Drop", new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().Text("Window-level file drop:").FontSize(11),
            new MultiLineTextBox().Height(80).BindText(_dropSummary)), minWidth: 360),
        Card("Element Drag and Drop", new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().FontSize(11)
                .Text("Internal element drag (drag a chip between the two slots):"),
            new StackPanel().Horizontal().Spacing(8).Children(DragSlot("Slot A", source), DragSlot("Slot B", target)),
            new TextBlock().BindText(dragLog).FontSize(11)), minWidth: 360),
        Card("Cross-Window Drag and Drop", new StackPanel().Width(320).Vertical().Spacing(8).Children(
            new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
                .Text("Open a second window, then drag a chip from here into its slot (and back). Same-process routing - no OS clipboard, the .NET payload is passed by reference."),
            new Button().Content("Open second window").OnClick(OpenCrossWindowTarget),
            DragSlot("Drag from here", crossSource)), minWidth: 320));
}

// ═══════════════════════════════════════════════════════════════════════
// Selection
// ═══════════════════════════════════════════════════════════════════════

FrameworkElement SelectionPage()
{
    var items = Enumerable.Range(1, 20).Select(i => $"Item {i}").Append("Item Long Long Long Long Long Long Long").ToArray();
    Calendar calendar = null!;
    var doneEnabled = new ObservableValue<bool>(false);

    FrameworkElement TabPlacementSample(string title, TabPlacement placement, Rotation? headerRotation = null)
    {
        Element Header(string text)
        {
            var label = new TextBlock().Text(text);
            return headerRotation is { } rotation
                ? new RotationDecorator().Rotation(rotation).Child(label)
                : label;
        }

        return new StackPanel().Vertical().Spacing(4).Children(
            new TextBlock().Text(title).FontSize(11),
            new TabControl()
                .Height(headerRotation is null ? 140 : 180)
                .TabPlacement(placement)
                .TabItems(
                    new TabItem().Header(Header("Home"))
                        .Content(new TextBlock().Text("Home tab content")),
                    new TabItem().Header(Header("Settings"))
                        .Content(new TextBlock().Text("Settings tab content")),
                    new TabItem().Header(Header("About"))
                        .Content(new TextBlock().Text("About tab content"))));
    }

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

        Card("SegmentedControl", new StackPanel().Vertical().Spacing(12).Children(
            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("Text").FontSize(11),
                new SegmentedControl().Items("Day", "Week", "Month").SelectedIndex(0)),

            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("Text + Icon").FontSize(11),
                new SegmentedControl()
                    .Items(new[]
                    {
                        new SegmentItem("apps_list_regular", "List"),
                        new SegmentItem("table_regular", "Table"),
                        new SegmentItem("data_pie_regular", "Chart"),
                    }, item => item.Label)
                    .ItemTemplate<SegmentItem>(
                        build: context =>
                        {
                            var icon = SegmentIconShape(16).CenterVertical();
                            var label = new TextBlock().CenterVertical();
                            context.Register("icon", icon);
                            context.Register("label", label);
                            return new StackPanel().Horizontal().Spacing(6).Center().Children(icon, label);
                        },
                        bind: (_, item, _, context) =>
                        {
                            context.Get<PathShape>("icon").Data = SegmentIcon(item.Icon);
                            context.Get<TextBlock>("label").Text = item.Label;
                        })
                    .SelectedIndex(0)),

            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("Icon").FontSize(11),
                new SegmentedControl()
                    .Items(new[]
                    {
                        new SegmentItem("home_regular", "Home"),
                        new SegmentItem("settings_regular", "Settings"),
                        new SegmentItem("calendar_regular", "Calendar"),
                    }, item => item.Label)
                    .ItemTemplate<SegmentItem>(
                        build: _ => SegmentIconShape(18).Center(),
                        bind: (view, item, _, _) => ((PathShape)view).Data = SegmentIcon(item.Icon))
                    .SelectedIndex(1)),

            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("Disabled segment (bound)").FontSize(11),
                new SegmentedControl()
                    .Items("All", "Active", "Done")
                    .PrepareContainer<string>((segment, item, _) =>
                    {
                        if (item == "Done") segment.BindIsEnabled(doneEnabled);
                    })
                    .SelectedIndex(0),
                new CheckBox().Content("Enable ‘Done’").BindIsChecked(doneEnabled)),

            new StackPanel().Vertical().Spacing(4).Children(
                new TextBlock().Text("Disabled").FontSize(11),
                new SegmentedControl().Items("Day", "Week", "Month")
                    .SelectedIndex(0).Disable())), minWidth: 320),

        Card("Calendar", new StackPanel().Vertical().Spacing(8).Children(
            new Calendar().Ref(out calendar),
            new TextBlock().Bind(TextBlock.TextProperty, calendar, Calendar.SelectedDateProperty, x => $"Selected: {x:yyyy-MM-dd}"))),

        Card("DatePicker", new StackPanel().Vertical().Spacing(8).Children(
            new DatePicker { Placeholder = "Select a date..." },
            new DatePicker { SelectedDate = DateTime.Today },
            new DatePicker { Placeholder = "Disabled" }.Disable()), minWidth: 250),

        Card("ColorPicker", new Grid()
            .Rows("Auto,Auto,Auto,Auto,Auto").Columns("Auto,*").Spacing(8).Children(
                new TextBlock().Text("Both"),
                new ColorPicker().SelectedColor(Color.FromRgb(255, 0, 0)),
                new TextBlock().Text("Wheel"),
                new ColorPicker().SelectedColor(Color.FromRgb(0, 128, 255)).Kind(ColorPickerKind.Wheel),
                new TextBlock().Text("Panel"),
                new ColorPicker().SelectedColor(Color.FromRgb(0, 200, 100)).Kind(ColorPickerKind.Panel),
                new TextBlock().Text("Alpha"),
                new ColorPicker().SelectedColor(Color.FromArgb(180, 255, 128, 0)).ShowAlpha(),
                new TextBlock().Text("Disabled"),
                new ColorPicker().SelectedColor(Color.FromRgb(80, 80, 80)).Disable()), minWidth: 250),

        Card("TabControl", new UniformGrid().Columns(2).Spacing(8).Children(
            TabPlacementSample("Top", TabPlacement.Top),
            TabPlacementSample("Bottom", TabPlacement.Bottom),
            TabPlacementSample("Left", TabPlacement.Left),
            TabPlacementSample("Right", TabPlacement.Right)), minWidth: 700),

        Card("TabControl Overflow", new TabControl().Width(260).Height(140).TabItems(
            new TabItem().Header("Overview").Content(new TextBlock().Text("Overview content")),
            new TabItem().Header("Rendering Pipeline").Content(new TextBlock().Text("Rendering Pipeline content")),
            new TabItem().Header("Input Routing").Content(new TextBlock().Text("Input Routing content")),
            new TabItem().Header("Property Binding").Content(new TextBlock().Text("Property Binding content")),
            new TabItem().Header("Disabled Diagnostics")
                .Content(new TextBlock().Text("Disabled Diagnostics content")).IsEnabled(false),
            new TabItem().Header("Final Review").Content(new TextBlock().Text("Final Review content")))
            .SelectedIndex(5), minWidth: 340),

        Card("TabControl + RotationDecorator", new UniformGrid().Columns(2).Spacing(8).Children(
            TabPlacementSample("Left", TabPlacement.Left, Rotation.CounterClockwise90),
            TabPlacementSample("Right", TabPlacement.Right, Rotation.Clockwise90)), minWidth: 700)
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

        Card("Expander", new StackPanel().Vertical().Spacing(8).Children(
            new Expander().Header("Details").Content(
                new StackPanel().Vertical().Spacing(6).Children(
                    new TextBlock().Text("Click the header to collapse."),
                    new Button().Content("Action"))),
            new Expander { IsExpanded = false }
                .Header("More options")
                .Content(new TextBlock().Text("Hidden until expanded.")))),

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

        Card("Border Top + Wrap Growth", new Border()
            .Width(260).Top().Padding(8).BorderThickness(1).CornerRadius(8)
            .WithTheme((theme, border) => border
                .Background(theme.Palette.ContainerBackground)
                .BorderBrush(theme.Palette.ControlBorder))
            .Child(new TextBlock().TextWrapping(TextWrapping.Wrap)
                .Text("Top-aligned border should grow with wrapped text. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog."))),

        Card("StackPanel Wrap Growth", new Border()
            .Width(260).Top().Padding(8).BorderThickness(1).CornerRadius(8)
            .WithTheme((theme, border) => border
                .Background(theme.Palette.ContainerBackground)
                .BorderBrush(theme.Palette.ControlBorder))
            .Child(new StackPanel().Vertical().Spacing(6).Children(
                new TextBlock().TextWrapping(TextWrapping.Wrap)
                    .Text("First wrapped label. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog."),
                new TextBlock().TextWrapping(TextWrapping.Wrap)
                    .Text("Second wrapped label. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog.")))),

        Card("Wrap + Button", new Border()
            .Width(260).Top().Padding(8).BorderThickness(1).CornerRadius(8)
            .WithTheme((theme, border) => border
                .Background(theme.Palette.ContainerBackground)
                .BorderBrush(theme.Palette.ControlBorder))
            .Child(new StackPanel().Vertical().Spacing(6).Children(
                new TextBlock().TextWrapping(TextWrapping.Wrap)
                    .Text("Wrapped label followed by a button. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog."),
                new Border()
                    .WithTheme((theme, border) => border
                        .Background(theme.Palette.ContainerBackground)
                        .BorderBrush(theme.Palette.ControlBorder))
                    .Child(new TextBlock().Center().Text("After Wrap"))))),

        Card("TextTrimming", new StackPanel().Vertical().Spacing(8).Children(
            new Border().Width(200).Padding(6).BorderThickness(1).CornerRadius(6)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .Child(new TextBlock().Text("No trimming: The quick brown fox jumps over the lazy dog")),
            new Border().Width(200).Padding(6).BorderThickness(1).CornerRadius(6)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .Child(new TextBlock().Text("CharacterEllipsis: The quick brown fox jumps over the lazy dog").TextTrimming(TextTrimming.CharacterEllipsis)),
            new Border().Width(200).Padding(6).BorderThickness(1).CornerRadius(6)
                .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                .Child(new TextBlock().Text("Ellipsis + Center: The quick brown fox jumps over the lazy dog")
                    .TextTrimming(TextTrimming.CharacterEllipsis).TextAlignment(TextAlignment.Center)),
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

FrameworkElement TransformPage()
{
    Image DemoImage(double width = 120, double height = 120) => BindResourceImage(
        new Image().Width(width).Height(height).StretchMode(Stretch.Uniform)
            .ImageScaleQuality(ImageScaleQuality.HighQuality),
        aprilResource);

    TextBlock ValueLabel(MewObject source, MewProperty<double> property, string format) =>
        new TextBlock().Bind(TextBlock.TextProperty, source, property, value => value.ToString(format))
            .Width(54).CenterVertical();

    var translate = new TransformBox().Center().Child(DemoImage());
    var tx = new Slider().Range(-100, 100).Width(160);
    var ty = new Slider().Range(-100, 100).Width(160);
    translate.Bind(TransformBox.TranslateXProperty, tx, RangeBase.ValueProperty);
    translate.Bind(TransformBox.TranslateYProperty, ty, RangeBase.ValueProperty);

    var rotate = new TransformBox().Center().Child(DemoImage());
    var angle = new Slider().Range(-180, 180).Width(180);
    rotate.Bind(TransformBox.RotationDegreesProperty, angle, RangeBase.ValueProperty);

    var scale = new TransformBox().Center().Child(DemoImage());
    var scaleValue = new Slider().Range(10, 300).Value(100).Width(180);
    scale.Bind(TransformBox.ScaleXProperty, scaleValue, RangeBase.ValueProperty,
        value => value / 100.0, value => value * 100.0);
    scale.Bind(TransformBox.ScaleYProperty, scaleValue, RangeBase.ValueProperty,
        value => value / 100.0, value => value * 100.0);

    var combined = new TransformBox().Center().Child(BindResourceImage(
        new Image().Width(200).Height(90).StretchMode(Stretch.Uniform), logoResource));
    var combinedAngle = new Slider().Range(-180, 180).Width(150);
    var combinedScale = new Slider().Range(10, 300).Value(100).Width(150);
    combined.Bind(TransformBox.RotationDegreesProperty, combinedAngle, RangeBase.ValueProperty);
    combined.Bind(TransformBox.ScaleXProperty, combinedScale, RangeBase.ValueProperty,
        value => value / 100.0, value => value * 100.0);
    combined.Bind(TransformBox.ScaleYProperty, combinedScale, RangeBase.ValueProperty,
        value => value / 100.0, value => value * 100.0);

    var origin = new TransformBox().Center().Child(DemoImage());
    var originX = new Slider().Range(0, 100).Value(50).Width(120);
    var originY = new Slider().Range(0, 100).Value(50).Width(120);
    var originAngle = new Slider().Range(-180, 180).Value(30).Width(120);
    origin.Bind(TransformBox.OriginXProperty, originX, RangeBase.ValueProperty,
        value => value / 100.0, value => value * 100.0);
    origin.Bind(TransformBox.OriginYProperty, originY, RangeBase.ValueProperty,
        value => value / 100.0, value => value * 100.0);
    origin.Bind(TransformBox.RotationDegreesProperty, originAngle, RangeBase.ValueProperty);

    void OpenZoomPanWindow()
    {
        var image = BindResourceImage(
            new Image().StretchMode(Stretch.None).ImageScaleQuality(ImageScaleQuality.HighQuality),
            soondukResource);
        var canvas = new ZoomPanCanvas { Child = image, CenterContent = true };
        var viewer = new ScrollViewer()
            .Padding(0)
            .HorizontalScroll(ScrollMode.Auto)
            .VerticalScroll(ScrollMode.Auto);
        viewer.Content = canvas;

        var logRatio = Math.Log(ZoomPanCanvas.MaxZoom / ZoomPanCanvas.MinZoom);
        var zoom = new Slider().Width(150).CenterVertical().Range(0, 1).SmallChange(0.01)
            .Value(Math.Log(1.0 / ZoomPanCanvas.MinZoom) / logRatio)
            .Bind(Slider.ValueProperty, canvas, ZoomPanCanvas.ZoomProperty,
                value => Math.Log(value / ZoomPanCanvas.MinZoom) / logRatio,
                value => ZoomPanCanvas.MinZoom * Math.Exp(value * logRatio));
        var zoomLabel = new TextBlock().Text("100%").CenterVertical().Width(70);
        canvas.ZoomChanged += value => zoomLabel.Text = $"{value * 100:0}%";

        new Window()
            .Resizable(900, 650)
            .Title("Zoom & Pan Canvas")
            .Content(new DockPanel().Children(
                new StackPanel().DockTop().Horizontal().Margin(8).Spacing(8).Children(
                    new TextBlock().Text("Zoom:").CenterVertical(),
                    zoom,
                    zoomLabel,
                    new Button().Content("Reset").Width(70)
                        .OnClick(() => canvas.AnimateZoomWithViewCenter(viewer, 1.0)),
                    new Button().Content("Fit").Width(70).OnClick(() =>
                    {
                        if (viewer.ViewportWidth <= 0 || viewer.ViewportHeight <= 0) return;
                        var size = canvas.ChildNaturalSize;
                        if (size.Width <= 0 || size.Height <= 0) return;
                        var fit = Math.Min(viewer.ViewportWidth / size.Width, viewer.ViewportHeight / size.Height);
                        canvas.AnimateZoomWithViewCenter(viewer,
                            Math.Clamp(fit, ZoomPanCanvas.MinZoom, ZoomPanCanvas.MaxZoom));
                    }),
                    new CheckBox().Content("Center").Check().CenterVertical()
                        .OnCheckedChanged(value => canvas.CenterContent = value),
                    new TextBlock().Text("Wheel to zoom, Drag or Scrollbar to pan")
                        .Foreground(Color.FromRgb(120, 120, 120)).CenterVertical()),
                viewer))
            .Show(window);
    }

    FrameworkElement TransformSurface(FrameworkElement content) =>
        new Border().Height(170).ClipToBounds(true).StretchHorizontal().Child(content);

    return CardGrid(
        Card("Translate", new StackPanel().Vertical().Spacing(8).Children(
            TransformSurface(translate),
            new StackPanel().Horizontal().Spacing(8).Children(new TextBlock().Text("X:").Width(48), tx, ValueLabel(translate, TransformBox.TranslateXProperty, "F0")),
            new StackPanel().Horizontal().Spacing(8).Children(new TextBlock().Text("Y:").Width(48), ty, ValueLabel(translate, TransformBox.TranslateYProperty, "F0")))),
        Card("Rotate", new StackPanel().Vertical().Spacing(8).Children(
            TransformSurface(rotate),
            new StackPanel().Horizontal().Spacing(8).Children(new TextBlock().Text("Angle:").Width(48), angle, ValueLabel(rotate, TransformBox.RotationDegreesProperty, "F0")))),
        Card("Scale", new StackPanel().Vertical().Spacing(8).Children(
            TransformSurface(scale),
            new StackPanel().Horizontal().Spacing(8).Children(new TextBlock().Text("Scale:").Width(48), scaleValue, ValueLabel(scale, TransformBox.ScaleXProperty, "F2")))),
        Card("Rotate + Scale", new StackPanel().Vertical().Spacing(8).Children(
            TransformSurface(combined),
            new StackPanel().Horizontal().Spacing(8).Children(new TextBlock().Text("Angle:").Width(48), combinedAngle, ValueLabel(combined, TransformBox.RotationDegreesProperty, "F0")),
            new StackPanel().Horizontal().Spacing(8).Children(new TextBlock().Text("Scale:").Width(48), combinedScale, ValueLabel(combined, TransformBox.ScaleXProperty, "F2")))),
        Card("Transform Origin", new StackPanel().Vertical().Spacing(8).Children(
            TransformSurface(origin),
            new StackPanel().Horizontal().Spacing(8).Children(new TextBlock().Text("X:").Width(48), originX, ValueLabel(origin, TransformBox.OriginXProperty, "F2")),
            new StackPanel().Horizontal().Spacing(8).Children(new TextBlock().Text("Y:").Width(48), originY, ValueLabel(origin, TransformBox.OriginYProperty, "F2")),
            new StackPanel().Horizontal().Spacing(8).Children(new TextBlock().Text("Angle:").Width(48), originAngle, ValueLabel(origin, TransformBox.RotationDegreesProperty, "F0")))),
        Card("Zoom & Pan Canvas", new StackPanel().Vertical().Spacing(8).Children(
            new Button().Content("Open Zoom & Pan Window").OnClick(OpenZoomPanWindow),
            new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
                .Text("Wheel to zoom (anchored to cursor),\ndrag to pan. Animated zoom with\nReset/Fit controls."))));
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

    FrameworkElement ImageBlock(int colorIndex)
    {
        var color = colors[colorIndex % colors.Length];
        const int width = 80, height = 50;
        var bgra = new byte[width * height * 4];
        for (var i = 0; i < width * height; i++)
        {
            bgra[i * 4] = color.B;
            bgra[i * 4 + 1] = color.G;
            bgra[i * 4 + 2] = color.R;
            bgra[i * 4 + 3] = 255;
        }
        return new Image().Source(ImageSource.FromBgraPixels(width, height, bgra)).StretchMode(Stretch.Fill);
    }

    FrameworkElement Block(string text, int ci) =>
        new Border().Background(colors[ci % colors.Length]).CornerRadius(6).Padding(12, 8)
            .Child(new TextBlock().Text(text).Foreground(Color.White).Bold().Center());

    // Fade
    int fadeIdx = 0;
    string[] fadeItems = ["Hello, World!", "MewUI Transitions", "Fade Effect", "Smooth & Simple"];
    var fadeView = new TransitionContentControl { Transition = ContentTransition.CreateFade(durationMs: 300) };
    fadeView.Content = Block(fadeItems[0], 0);

    int fadeImageIndex = 0;
    var fadeImageView = new TransitionContentControl
        { Transition = ContentTransition.CreateFade(durationMs: 300) };
    fadeImageView.Content = ImageBlock(0);

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

        Card("Fade (Image)", new StackPanel().Vertical().Spacing(8).Children(
            new Border().Height(60).Child(fadeImageView),
            new Button().Content("Next").OnClick(() =>
            {
                fadeImageIndex = (fadeImageIndex + 1) % colors.Length;
                fadeImageView.Content = ImageBlock(fadeImageIndex);
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

FrameworkElement CustomRenderingPage() =>
    CardGrid(Card("Offscreen", new SampleOffscreenControl { Height = 300, Width = 280 }));

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
        grid.ItemsSource = ItemsView.Create(view);
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

    return CardGrid(simpleList, classList, templateCard, wrapCard);
}

FrameworkElement TreeViewPage()
{
    TreeViewNode[] Nodes(params string[] labels) => labels.Select(text => new TreeViewNode(text)).ToArray();
    var treeItems = new[]
    {
        new TreeViewNode("src", [
            new TreeViewNode("MewUI", [
                new TreeViewNode("Controls", Nodes("Button.cs", "TextBox.cs", "TreeView.cs"))]),
            new TreeViewNode("Rendering", [
                new TreeViewNode("Gdi", Nodes("GdiMeasurementContext.cs", "GdiGrapchisContext.cs", "GdiGraphicsFactory.cs")),
                new TreeViewNode("Direct2D", Nodes("Direct2DMeasurementContext.cs", "Direct2DGrapchisContext.cs", "Direct2DGraphicsFactory.cs")),
                new TreeViewNode("OpenGL", Nodes("OpenGLMeasurementContext.cs", "OpenGLGrapchisContext.cs", "OpenGLGraphicsFactory.cs"))])]),
        new TreeViewNode("README.md"),
        new TreeViewNode("assets", Nodes("logo.png", "icon.ico")),
    };
    TextBlock selectedNode = null!;
    var treeView = new TreeView()
        .Width(200)
        .ItemsSource(treeItems)
        .ExpandTrigger(TreeViewExpandTrigger.DoubleClickNode)
        .OnSelectionChanged(item => selectedNode.Text = item is TreeViewNode node
            ? $"Selected: {node.Text}" : "Selected: (none)");
    treeView.ItemTemplate<TreeViewNode>(
        build: context => new StackPanel().Horizontal().Spacing(6).Children(
            new Image().Register(context, "Icon").Size(16, 16).StretchMode(Stretch.None).CenterVertical(),
            new TextBlock().Register(context, "Text").CenterVertical()),
        bind: (_, item, _, context) =>
        {
            var source = item.HasChildren
                ? (treeView.IsExpanded(item) ? iconFolderOpenResource : iconFolderCloseResource)
                : iconFileResource;
            context.Get<Image>("Icon").SetBinding(Image.SourceProperty, source, BindingMode.OneWay);
            context.Get<TextBlock>("Text").Text(item.Text);
        });
    treeView.Expand(treeItems[0]);
    treeView.Expand(treeItems[0].Children[0]);

    var remoteRoot = new LazyTreeNode("remote", 0, true);
    void CancelAndClear(LazyTreeNode node)
    {
        foreach (var child in node.Children) CancelAndClear(child);
        node.LoadCancellation?.Cancel();
        node.IsLoading.Value = false;
        node.IsLoaded = false;
        node.Children.Clear();
    }

    var asyncTree = new TreeView().Width(240)
        .Items([remoteRoot], node => node.Children, node => node.Name, node => node,
            node => node.Children.Count > 0 || node.CanLoadChildren)
        .OnExpanding(async e =>
        {
            if (e.Item is not LazyTreeNode node || !node.CanLoadChildren ||
                node.IsLoaded || node.LoadCancellation is not null) return;
            var cancellation = new CancellationTokenSource();
            node.LoadCancellation = cancellation;
            node.IsLoading.Value = true;
            try
            {
                await Task.Delay(1200 + node.Depth * 300, cancellation.Token);
                if (node.Depth < 3)
                    for (var i = 1; i <= 6; i++)
                        node.Children.Add(new LazyTreeNode($"folder-{node.Depth + 1}-{i:00}", node.Depth + 1, true));
                for (var i = 1; i <= 18; i++)
                    node.Children.Add(new LazyTreeNode($"file-{node.Depth}-{i:00}.dat", node.Depth + 1, false));
                node.IsLoaded = true;
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
            finally
            {
                if (ReferenceEquals(node.LoadCancellation, cancellation)) node.LoadCancellation = null;
                node.IsLoading.Value = false;
                cancellation.Dispose();
            }
        })
        .OnCollapsing(e => { if (e.Item is LazyTreeNode node) CancelAndClear(node); });

    asyncTree.ItemTemplate<LazyTreeNode>(
        build: context => new StackPanel().Horizontal().Spacing(6).Children(
            new Grid().Width(16).Height(16).CenterVertical().Children(
                new Image().Register(context, "Icon").Size(16, 16).StretchMode(Stretch.None),
                new ProgressRing().Register(context, "Loading").Size(16, 16)
                    .WithTheme((theme, control) => control.Foreground(theme.Palette.Accent))),
            new TextBlock().Register(context, "Text").CenterVertical()),
        bind: (_, item, index, context) =>
        {
            var isFolder = item.Children.Count > 0 || item.CanLoadChildren;
            var source = isFolder
                ? (asyncTree.ItemsSource.GetIsExpanded(index) ? iconFolderOpenResource : iconFolderCloseResource)
                : iconFileResource;
            var icon = context.Get<Image>("Icon");
            icon.SetBinding(Image.SourceProperty, source, BindingMode.OneWay);
            context.Get<TextBlock>("Text").Text(item.Name);
            var loading = context.Get<ProgressRing>("Loading");
            icon.BindIsVisible(item.IsLoading, value => !value);
            loading.Bind(ProgressRing.IsActiveProperty, item.IsLoading);
            loading.BindIsVisible(item.IsLoading);
        });

    return CardGrid(
        Card("TreeView", new DockPanel().Height(240).Spacing(6).Children(
            new TextBlock().DockBottom().Ref(out selectedNode).FontSize(11).Text("Selected: (none)"),
            treeView)),
        Card("TreeView (Async children)", new DockPanel().Height(240).Children(asyncTree)));
}

FrameworkElement ItemsControlPage()
{
    var colors = new[]
    {
        Color.FromRgb(230, 100, 100), Color.FromRgb(100, 180, 230),
        Color.FromRgb(100, 200, 130), Color.FromRgb(220, 180, 80),
        Color.FromRgb(180, 120, 220), Color.FromRgb(240, 140, 100),
        Color.FromRgb(130, 200, 200), Color.FromRgb(200, 140, 170),
    };
    var tiles = Enumerable.Range(0, 4800).Select(i => $"Tile {i + 1}").ToArray();

    return CardGrid(
        Card("ItemsControl (WrapPresenter)", new ItemsControl()
            .ItemPadding(new(2))
            .Height(240)
            .Width(402)
            .WrapPresenter(80, 80)
            .ItemsSource(ItemsView.Create(tiles))
            .ItemTemplate(new DelegateTemplate<string>(
                build: context => new Border().Register(context, "Bg").CornerRadius(6)
                    .Child(new TextBlock().Register(context, "Label").Center().FontSize(11)),
                bind: (_, item, index, context) =>
                {
                    context.Get<Border>("Bg").Background(colors[index % colors.Length].WithAlpha(120));
                    context.Get<TextBlock>("Label").Text(item ?? string.Empty);
                }))),
        ChatVariableHeightCard());
}

FrameworkElement ChatVariableHeightCard()
{
    long nextId = 1;
    var messages = new ObservableCollection<ChatMessage>();
    void Add(bool mine, string sender, string text) =>
        messages.Add(new ChatMessage(nextId++, sender, text, mine, DateTimeOffset.Now));
    static string SampleText(int seed) => (seed % 7) switch
    {
        0 => "Short.",
        1 => "A longer message that wraps across multiple lines in a narrow bubble.",
        2 => "Multi-line:\n- first item\n- second item",
        3 => "Lorem ipsum dolor sit amet, consectetur adipiscing elit.",
        4 => "Symbols: !@#$%^&*() and 한글/日本語/emoji 🙂",
        5 => "The quick brown fox jumps over the lazy dog.",
        _ => "superlongword_superlongword_superlongword_superlongword"
    };

    Add(false, "Bot", "This is a chat-style ItemsControl sample.\nVariable-height virtualization is enabled.");
    Add(true, "You", "Try scrolling, then click 'Prepend 20' to insert older messages at the top.");
    for (var i = 0; i < 47; i++) Add(i % 3 == 0, i % 3 == 0 ? "You" : "Bot", SampleText(i));

    var view = new ItemsView<ChatMessage>(messages, message => message.Text, message => message.Id);
    ItemsControl list = null!;
    var input = new ObservableValue<string>(string.Empty);
    var status = new ObservableValue<string>($"Messages: {messages.Count}");
    void UpdateStatus() => status.Value = $"Messages: {messages.Count}";
    void ScrollToBottom() => list.ScrollIntoView(messages.Count - 1);
    void Send()
    {
        var text = input.Value.Trim();
        if (text.Length == 0) return;
        Add(true, "You", text);
        input.Value = string.Empty;
        UpdateStatus();
        Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render, ScrollToBottom);
    }

    return Card("ItemsControl (chat / variable height)",
        new DockPanel().MinWidth(640).MaxWidth(960).Height(320).Spacing(6).Children(
            new StackPanel().DockTop().Horizontal().Spacing(8).Children(
                new Button().Content("Prepend 20").OnClick(() =>
                {
                    for (var i = 19; i >= 0; i--)
                        messages.Insert(0, new ChatMessage(nextId++, "Bot", SampleText(i), false, DateTimeOffset.Now.AddMinutes(-i)));
                    UpdateStatus();
                }),
                new Button().Content("To bottom").OnClick(ScrollToBottom),
                new TextBlock().BindText(status).FontSize(11).CenterVertical()),
            new DockPanel().DockBottom().Spacing(6).Children(
                new Button().DockRight().Content("Send").OnClick(Send),
                new TextBox().Placeholder("Message...").BindText(input).OnKeyDown(e =>
                {
                    if (e.Key == Key.Enter) { e.Handled = true; Send(); }
                })),
            new ItemsControl().Ref(out list).HorizontalAlignment(HorizontalAlignment.Stretch)
                .VariableHeightPresenter().ItemPadding(Thickness.Zero).ItemsSource(view)
                .WithTheme((theme, _) => list.BorderBrush(theme.Palette.ControlBorder)
                    .BorderThickness(theme.Metrics.ControlBorderThickness))
                .ItemTemplate(new DelegateTemplate<ChatMessage>(
                    build: context => new Border().Register(context, "Bubble")
                        .BorderThickness(1).CornerRadius(10).Margin(16, 8).Padding(10, 6)
                        .Child(new StackPanel().Vertical().Spacing(2).Children(
                            new TextBlock().Register(context, "Sender").FontSize(10).Bold(),
                            new TextBlock().Register(context, "Text").TextWrapping(TextWrapping.Wrap))),
                    bind: (_, message, _, context) =>
                    {
                        var bubble = context.Get<Border>("Bubble");
                        context.Get<TextBlock>("Sender").Text = message.Sender;
                        context.Get<TextBlock>("Sender").IsVisible = !message.Mine;
                        context.Get<TextBlock>("Text").Text = message.Text;
                        bubble.HorizontalAlignment = message.Mine ? HorizontalAlignment.Right : HorizontalAlignment.Left;
                        bubble.WithTheme((theme, border) =>
                        {
                            border.Background(message.Mine
                                ? theme.Palette.Accent.Lerp(theme.Palette.WindowBackground, 0.85)
                                : theme.Palette.ControlBackground);
                            border.BorderBrush(message.Mine
                                ? theme.Palette.Accent.Lerp(theme.Palette.WindowText, 0.15)
                                : theme.Palette.ControlBorder);
                        });
                    }))), minWidth: 420);
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
        var v = rows.ToList(); cg.ItemsSource = ItemsView.Create(v); sumText.Value = $"Rows:{v.Count}/{allRows.Count} Err:{v.Count(r => r.HasError.Value)} Sum:{v.Sum(r => r.Amount.Value):0.##}";
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

    return CardGrid(simpleCard, complexCard, TemplateGridViewCard(), VariableHeightGridViewCard());
}

FrameworkElement TemplateGridViewCard()
{
    var users = Enumerable.Range(1, 250).Select(i => new TemplateComplexPersonRow(
        $"User {i:0000}", i % 3, i % 5 != 0, i % 101, (i * 7.3) % 100)).ToList();

    var grid = new GridView().RowHeight(44).ZebraStriping().ItemsSource(users).Columns(
        new GridViewColumn<TemplateComplexPersonRow>().Header("").Width(36).Bind(
            build: _ => new CheckBox().Padding(0).Center(),
            bind: (view, item) => ((CheckBox)view).BindIsChecked(item.IsSelected)),
        new GridViewColumn<TemplateComplexPersonRow>().Header("User").Width(240).Bind(
            build: context => new StackPanel().Vertical().Spacing(2).Padding(6, 2).Children(
                new TextBlock().Register(context, "Name").Bold(),
                new StackPanel().Horizontal().Spacing(8).Children(
                    new TextBlock().Register(context, "Role").FontSize(11),
                    new TextBlock().Register(context, "Online").FontSize(11),
                    new TextBlock().Register(context, "Score").FontSize(11))),
            bind: (_, item, _, context) =>
            {
                context.Get<TextBlock>("Name").BindText(item.Name);
                context.Get<TextBlock>("Role").BindText(item.RoleIndex,
                    role => role switch { 1 => "Admin", 2 => "Guest", _ => "User" });
                context.Get<TextBlock>("Online").BindText(item.IsOnline,
                    value => value ? "Online" : "Offline");
                context.Get<TextBlock>("Score").BindText(item.Score, value => $"Score: {value:0.#}");
            }),
        new GridViewColumn<TemplateComplexPersonRow>().Header("Role").Width(120).Bind(
            build: _ => new ComboBox().Items(["User", "Admin", "Guest"]).CenterVertical(),
            bind: (view, item) => ((ComboBox)view).BindSelectedIndex(item.RoleIndex)),
        new GridViewColumn<TemplateComplexPersonRow>().Header("Progress").Width(200).Bind(
            build: _ => new ProgressBar().Minimum(0).Maximum(100).Height(10).Margin(6, 0).CenterVertical(),
            bind: (view, item) => ((ProgressBar)view).BindValue(item.Progress)),
        new GridViewColumn<TemplateComplexPersonRow>().Header("Online").Width(80).Bind(
            build: _ => new ToggleSwitch().Center(),
            bind: (view, item) => ((ToggleSwitch)view).BindIsChecked(item.IsOnline)));

    return Card("GridView (complex cell templates)",
        new DockPanel().Width(600).Height(240).Spacing(8).Children(
            new TextBlock().DockTop()
                .Text("Shows delegate-based complex cell templates (nested layout + multiple bound controls)")
                .TextWrapping(TextWrapping.Wrap),
            grid));
}

FrameworkElement VariableHeightGridViewCard()
{
    static string SampleNoteText(int seed)
    {
        var length = 30 + (seed * 37) % 240;
        const string lorem = "Lorem ipsum dolor sit amet, consectetur adipiscing elit. " +
            "Sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
            "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi.";
        return $"#{seed}: " + lorem[..Math.Min(length, lorem.Length)];
    }

    var notes = new ObservableCollection<VariableHeightGridRow>(
        Enumerable.Range(1, 200).Select(i =>
            new VariableHeightGridRow(i, $"User {i:000}", SampleNoteText(i))));
    long nextId = notes.Count + 1;
    void Prepend(int count)
    {
        for (var i = count - 1; i >= 0; i--)
        {
            var id = (int)(nextId + i);
            notes.Insert(0, new VariableHeightGridRow(id, $"Bot {id}", SampleNoteText(id * 3)));
        }
        nextId += count;
    }

    return Card("GridView (variable row height + scroll anchor)",
        new DockPanel().Width(640).Height(320).Spacing(6).Children(
            new TextBlock().DockTop()
                .Text("Each row sizes to the tallest cell. Scroll into the list, then 'Prepend 20' to verify the viewport stays anchored on the same content.")
                .TextWrapping(TextWrapping.Wrap).FontSize(11),
            new StackPanel().DockTop().Horizontal().Spacing(8).Children(
                new Button().Content("Prepend 20").OnClick(() => Prepend(20)),
                new Button().Content("Add to end").OnClick(() =>
                {
                    var id = (int)nextId++;
                    notes.Add(new VariableHeightGridRow(id, $"User {id}", SampleNoteText(id)));
                })),
            new GridView().VariableHeightPresenter().ItemsSource(notes).ZebraStriping().Columns(
                new GridViewColumn<VariableHeightGridRow>().Header("#").Width(50).Text(row => row.Id.ToString()),
                new GridViewColumn<VariableHeightGridRow>().Header("Author").Width(110).Text(row => row.Author),
                new GridViewColumn<VariableHeightGridRow>().Header("Note").Width(440).Bind(
                    build: _ => new TextBlock().TextWrapping(TextWrapping.Wrap),
                    bind: (view, row) => ((TextBlock)view).Text(row.Note)))));
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

FrameworkElement WindowPage() => CardGrid(
    Card("Native Custom Chrome", new StackPanel().Vertical().Spacing(8).Children(
        new Button().Content("Open Native Chrome Window")
            .OnClick(() => new NativeCustomWindowSample().Show(window)),
        new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
            .Text("Hides the default title bar while keeping\nthe native frame (rounded corners, shadow)."))),
    CustomChromeWindowCard(),
    Card("Hot-reload", new StackPanel().Vertical().Spacing(8).Children(
        new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
            .Text("Modify the code and save to see hot-reload in action.\nThis card will update with the current time."),
        new TextBlock().Text($"Loaded: {DateTime.Now}"))),
    WindowShowDialogCard(),
    TransparentWindowCard(),
    ManualPositionCard(),
    AsyncCloseCard(),
    PromptDialogCard(),
    NativeMessageHookCard(),
    DevToolsCard());

FrameworkElement MenuPage()
{
    var shortcutLog = new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
        .Text("Press a shortcut key (e.g. Ctrl+N, Ctrl+S, ...)");
    void OnShortcut(string action) => shortcutLog.Text = $"[{DateTime.Now:HH:mm:ss.fff}] {action}";

    return CardGrid(
        Card("MenuBar (Multi-depth, AccessKeys + Shortcuts)", new StackPanel().Width(290).Vertical().Spacing(8).Children(
            CreateMenu(OnShortcut),
            new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
                .Text("Hover to switch menus while a popup is open. Submenus supported."),
            shortcutLog)),
        Card("AccessKey & Shortcuts", AccessKeyCard()));
}

FrameworkElement CustomChromeWindowCard() =>
    Card("Custom Chrome Window", new StackPanel().Vertical().Spacing(8).Children(
        new Button().Content("Open CustomWindow").OnClick(() =>
        {
            Window custom = null!;
            new Window().Ref(out custom).Resizable(640, 420).StartCenterOwner()
                .OnBuild(x =>
                {
                    x.Title = "CustomWindow";
                    x.AllowsTransparency = true;
                    x.Background = Color.Transparent;
                    x.Padding = Thickness.Zero;
                    x.Content = new Border().CornerRadius(12).BorderThickness(1)
                        .WithTheme((theme, border) => border
                            .Background(theme.Palette.WindowBackground)
                            .BorderBrush(theme.Palette.ControlBorder))
                        .Child(new DockPanel().Children(
                            new Border().DockTop().Height(42).Padding(12, 0)
                                .WithTheme((theme, border) => border.Background(theme.Palette.ControlBackground))
                                .Apply(element => EnableWindowDrag(custom, element))
                                .Child(new DockPanel().Children(
                                    new Button().DockRight().Content("Close").OnClick(() => custom.Close()),
                                    new TextBlock().Text("CustomWindow").Bold().CenterVertical())),
                            new TextBlock().Text("AllowsTransparency-based custom chrome.").Center()));
                })
                .Show(window);
        }),
        new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
            .Text("AllowsTransparency-based custom chrome.\nProvides rounded borders on Win10 and earlier.\nWin32: higher overhead. Prefer NativeCustomWindow.")));

FrameworkElement WindowShowDialogCard()
{
    var status = new ObservableValue<string>("Dialog: -");
    async void ShowDialogSample()
    {
        status.Value = "Dialog: opening...";
        Window dialog = null!;
        new Window().Ref(out dialog).Resizable(420, 220).StartCenterScreen()
            .OnBuild(x => x.Title("ShowDialog sample").Padding(16)
                .Content(new StackPanel().Vertical().Spacing(10).Children(
                    new TextBlock().Text("This is a modal window."),
                    new Button().Content("Close").OnClick(() => dialog.Close()))));
        await dialog.ShowDialogAsync(window);
        status.Value = "Dialog: closed";
    }
    return Card("ShowDialogAsync", new StackPanel().Vertical().Spacing(8).Children(
        new Button().Content("Open dialog").OnClick(ShowDialogSample),
        new TextBlock().BindText(status).FontSize(11)));
}

FrameworkElement AsyncCloseCard()
{
    var status = new ObservableValue<string>("CloseAsync: -");
    Window? sample = null;
    void OpenSample()
    {
        if (sample is not null) { sample.Activate(); return; }
        var count = new ObservableValue<int>(3);
        var sampleWindow = new Window().Title("Async close sample")
            .FitContentHeight(340, 300).Padding(12)
            .Content(new StackPanel().Vertical().Spacing(8).Children(
                new TextBlock().TextWrapping(TextWrapping.Wrap)
                    .Text("Closing takes a deferral and asks for confirmation asynchronously. Try the title-bar close button too - close requests made while the confirmation is open join the pending decision."),
                new TextBlock().BindText(count, value => $"Countdown: {value}")));
        sampleWindow.Closing += async args =>
        {
            using (args.GetDeferral())
            {
                if (!await MessageBox.ConfirmAsync("Close this window?", owner: sampleWindow)) args.Cancel = true;
                else while (count.Value > 0) { await Task.Delay(1000); count.Value--; }
            }
        };
        sampleWindow.Closed += () => sample = null;
        sample = sampleWindow;
        sampleWindow.Show(window);
    }
    return Card("Async Close (CloseAsync + Closing deferral)",
        new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().FontSize(11)
                .Text("The sample window defers its Closing decision to an async confirmation.\nCloseAsync reports whether it actually closed."),
            new WrapPanel().Spacing(6).Children(
                new Button().Content("Open sample window").OnClick(OpenSample),
                new Button().Content("CloseAsync").OnClick(async () =>
                {
                    if (sample is null) { status.Value = "CloseAsync: no sample window"; return; }
                    var closed = await sample.CloseAsync();
                    status.Value = closed ? "CloseAsync: closed" : "CloseAsync: cancelled";
                })),
            new TextBlock().BindText(status).FontSize(11)));
}

FrameworkElement DevToolsCard()
{
    var shortcuts = new TextBlock().FontSize(11)
        .Text("Shortcuts:\n- Inspector: Ctrl/Cmd+Shift+I\n- Visual Tree: Ctrl/Cmd+Shift+T");
    return Card("DevTools", new StackPanel().Vertical().Spacing(8).Children(
        new TextBlock().FontSize(11)
            .Text("DevTools are available only when the MewUI library itself is built in Debug mode."),
        shortcuts));
}

FrameworkElement FileDialogPage()
{
    var filters = new[]
    {
        new FileFilter("Text/Markdown", "*.txt", "*.md"),
        new FileFilter("Images", "*.png", "*.jpg", "*.jpeg", "*.gif"),
        new FileFilter("All files", "*.*"),
    };

    FrameworkElement BackendCard(string title, FileDialogBackend backend)
    {
        var status = new ObservableValue<string>("Result: -");
        void OpenFile()
        {
            var path = FileDialog.OpenFile(new OpenFileDialogOptions
            {
                Owner = window, Title = "Open File", Filters = filters, Backend = backend,
            });
            status.Value = path is null ? "Result: canceled" : $"Result: {path}";
        }
        void OpenFiles()
        {
            var files = FileDialog.OpenFiles(new OpenFileDialogOptions
            {
                Owner = window, Title = "Open Files", Filters = filters, Backend = backend,
            });
            status.Value = files is null || files.Length == 0
                ? "Result: canceled"
                : $"Result: {files.Length} selected\n{string.Join("\n", files)}";
        }
        void SaveFile()
        {
            var path = FileDialog.SaveFile(new SaveFileDialogOptions
            {
                Owner = window, Title = "Save File", Filters = filters,
                FileName = "untitled.txt", DefaultExtension = "txt", Backend = backend,
            });
            status.Value = path is null ? "Result: canceled" : $"Result: {path}";
        }
        void SelectFolder()
        {
            var folder = FileDialog.SelectFolder(new FolderDialogOptions
            {
                Owner = window, Title = "Select Folder", Backend = backend,
            });
            status.Value = folder is null ? "Result: canceled" : $"Result: {folder}";
        }

        return Card(title, new StackPanel().Vertical().Spacing(8).Children(
            new Button().Content("Open File...").OnClick(OpenFile),
            new Button().Content("Open Files...").OnClick(OpenFiles),
            new Button().Content("Save File...").OnClick(SaveFile),
            new Button().Content("Select Folder...").OnClick(SelectFolder),
            new TextBlock().BindText(status).FontSize(11).TextWrapping(TextWrapping.Wrap)));
    }

    return CardGrid(
        BackendCard("Managed", FileDialogBackend.Managed),
        BackendCard("Native", FileDialogBackend.Native));
}

FrameworkElement ShowDialogPage()
{
    var syncStatus = new ObservableValue<string>("Result: -");
    var asyncStatus = new ObservableValue<string>("Result: -");

    Window CreateDialog(string title)
    {
        Window dialog = null!;
        return new Window()
            .Ref(out dialog)
            .Resizable(420, 220)
            .StartCenterScreen()
            .OnBuild(x => x
                .Title(title)
                .Padding(16)
                .Content(new StackPanel().Vertical().Spacing(10).Children(
                    new TextBlock().Text("This is a modal window."),
                    new StackPanel().Horizontal().Spacing(8).Children(
                        new Button().Content("Close").OnClick(() => dialog.Close())))));
    }

    return CardGrid(
        Card("Synchronous ShowDialog", new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
                .Text("ShowDialog() blocks this click handler while a nested loop keeps the UI responsive."),
            new Button().Content("Show (sync)").OnClick(() =>
            {
                syncStatus.Value = "Result: opening...";
                try { CreateDialog("Synchronous ShowDialog").ShowDialog(window); syncStatus.Value = "Result: closed"; }
                catch (Exception ex) { syncStatus.Value = $"Result: error ({ex.GetType().Name})"; }
            }),
            new TextBlock().BindText(syncStatus).FontSize(11))),
        Card("Asynchronous ShowDialogAsync", new StackPanel().Vertical().Spacing(8).Children(
            new TextBlock().FontSize(11).TextWrapping(TextWrapping.Wrap)
                .Text("ShowDialogAsync() returns a Task and is awaited on the same UI loop."),
            new Button().Content("Show (async)").OnClick(async () =>
            {
                asyncStatus.Value = "Result: opening...";
                try { await CreateDialog("Asynchronous ShowDialogAsync").ShowDialogAsync(window); asyncStatus.Value = "Result: closed"; }
                catch (Exception ex) { asyncStatus.Value = $"Result: error ({ex.GetType().Name})"; }
            }),
            new TextBlock().BindText(asyncStatus).FontSize(11))));
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
        // ClientToScreen now returns top-left, Y-down pixels on every platform.
        var screen = dragWindow.ClientToScreen(e.GetPosition(dragWindow));
        var scale = Math.Max(1.0, dragWindow.DpiScale);
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

FrameworkElement SegmentRow(string name, FrameworkElement content) =>
    new StackPanel().Vertical().Spacing(4).Children(
        new TextBlock().Text(name).FontSize(11), content);

ButtonGroup SegmentIconTextGroup(params SegmentItem[] items) =>
    new ButtonGroup()
        .Items(items, item => item.Label)
        .ItemTemplate<SegmentItem>(
            build: context =>
            {
                var icon = SegmentIconShape(16).CenterVertical();
                var label = new TextBlock().CenterVertical();
                context.Register("icon", icon);
                context.Register("label", label);
                return new StackPanel().Horizontal().Spacing(6).Center().Children(icon, label);
            },
            bind: (_, item, _, context) =>
            {
                context.Get<PathShape>("icon").Data = SegmentIcon(item.Icon);
                context.Get<TextBlock>("label").Text = item.Label;
            });

ButtonGroup SegmentIconGroup(params SegmentItem[] items) =>
    new ButtonGroup()
        .Items(items, item => item.Label)
        .ItemTemplate<SegmentItem>(
            build: _ => SegmentIconShape(18).Center(),
            bind: (view, item, _, _) => ((PathShape)view).Data = SegmentIcon(item.Icon));

PathShape SegmentIconShape(double size)
{
    var shape = new PathShape().Stretch(Stretch.Uniform).Width(size).Height(size);
    shape.Bind(Shape.FillProperty, shape, Control.ForegroundProperty,
        (Color color) => new SolidColorBrush(color));
    return shape;
}

PathGeometry SegmentIcon(string name)
    => SegmentIcons.Get(name);

// ═══════════════════════════════════════════════════════════════════════
// Custom classes
// ═══════════════════════════════════════════════════════════════════════

sealed class TransformBox : FrameworkElement, IVisualTreeHost
{
    public static readonly MewProperty<UIElement?> ChildProperty =
        MewProperty<UIElement?>.Register<TransformBox>(nameof(Child), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) => self.OnChildChanged(oldValue, newValue));
    public static readonly MewProperty<double> TranslateXProperty =
        MewProperty<double>.Register<TransformBox>(nameof(TranslateX), 0.0, MewPropertyOptions.AffectsRender);
    public static readonly MewProperty<double> TranslateYProperty =
        MewProperty<double>.Register<TransformBox>(nameof(TranslateY), 0.0, MewPropertyOptions.AffectsRender);
    public static readonly MewProperty<double> RotationDegreesProperty =
        MewProperty<double>.Register<TransformBox>(nameof(RotationDegrees), 0.0, MewPropertyOptions.AffectsRender);
    public static readonly MewProperty<double> ScaleXProperty =
        MewProperty<double>.Register<TransformBox>(nameof(ScaleX), 1.0, MewPropertyOptions.AffectsRender);
    public static readonly MewProperty<double> ScaleYProperty =
        MewProperty<double>.Register<TransformBox>(nameof(ScaleY), 1.0, MewPropertyOptions.AffectsRender);
    public static readonly MewProperty<double> OriginXProperty =
        MewProperty<double>.Register<TransformBox>(nameof(OriginX), 0.5, MewPropertyOptions.AffectsRender);
    public static readonly MewProperty<double> OriginYProperty =
        MewProperty<double>.Register<TransformBox>(nameof(OriginY), 0.5, MewPropertyOptions.AffectsRender);

    public UIElement? Child { get => GetValue(ChildProperty); set => SetValue(ChildProperty, value); }
    public double TranslateX { get => GetValue(TranslateXProperty); set => SetValue(TranslateXProperty, value); }
    public double TranslateY { get => GetValue(TranslateYProperty); set => SetValue(TranslateYProperty, value); }
    public double RotationDegrees { get => GetValue(RotationDegreesProperty); set => SetValue(RotationDegreesProperty, value); }
    public double ScaleX { get => GetValue(ScaleXProperty); set => SetValue(ScaleXProperty, value); }
    public double ScaleY { get => GetValue(ScaleYProperty); set => SetValue(ScaleYProperty, value); }
    public double OriginX { get => GetValue(OriginXProperty); set => SetValue(OriginXProperty, value); }
    public double OriginY { get => GetValue(OriginYProperty); set => SetValue(OriginYProperty, value); }

    void OnChildChanged(UIElement? oldValue, UIElement? newValue)
    {
        if (oldValue is not null) DetachChild(oldValue);
        if (newValue is not null) AttachChild(newValue);
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (Child is null) return Size.Empty;
        Child.Measure(availableSize);
        return Child.DesiredSize;
    }

    protected override void ArrangeContent(Rect bounds) => Child?.Arrange(bounds);

    protected override UIElement? OnHitTest(Point point)
    {
        if (!IsVisible || !IsHitTestVisible) return null;
        if (Child?.HitTest(point) is UIElement hit) return hit;
        return Bounds.Contains(point) ? this : null;
    }

    Matrix3x2 BuildTransformMatrix()
    {
        var bounds = Bounds;
        var cx = (float)(bounds.X + bounds.Width * OriginX);
        var cy = (float)(bounds.Y + bounds.Height * OriginY);
        var matrix = Matrix3x2.CreateTranslation(-cx, -cy);
        if (ScaleX != 1 || ScaleY != 1)
            matrix *= Matrix3x2.CreateScale((float)ScaleX, (float)ScaleY);
        if (RotationDegrees != 0)
            matrix *= Matrix3x2.CreateRotation((float)(RotationDegrees * Math.PI / 180.0));
        return matrix * Matrix3x2.CreateTranslation(cx + (float)TranslateX, cy + (float)TranslateY);
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (Child is null) return;
        var transformed = TranslateX != 0 || TranslateY != 0 || RotationDegrees != 0 || ScaleX != 1 || ScaleY != 1;
        if (!transformed)
        {
            Child.Render(context);
            return;
        }
        var combined = BuildTransformMatrix() * context.GetTransform();
        context.Save();
        context.SetTransform(combined);
        Child.Render(context);
        context.Restore();
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => Child is null || visitor(Child);
}

static class TransformBoxExtensions
{
    public static TransformBox Child(this TransformBox box, UIElement? child)
    {
        box.Child = child;
        return box;
    }
}

sealed class ZoomPanCanvas : FrameworkElement, IVisualTreeHost
{
    public const double MinZoom = 0.1;
    public const double MaxZoom = 20.0;

    public static readonly MewProperty<UIElement?> ChildProperty =
        MewProperty<UIElement?>.Register<ZoomPanCanvas>(nameof(Child), null,
            MewPropertyOptions.AffectsLayout,
            static (self, oldValue, newValue) =>
            {
                if (oldValue is not null)
                {
                    oldValue.SkipViewportCull = false;
                    self.DetachChild(oldValue);
                }
                if (newValue is not null)
                {
                    self.AttachChild(newValue);
                    newValue.SkipViewportCull = true;
                }
            });

    public static readonly MewProperty<double> ZoomProperty =
        MewProperty<double>.Register<ZoomPanCanvas>(nameof(Zoom), 1.0,
            MewPropertyOptions.AffectsLayout | MewPropertyOptions.AffectsRender,
            static (self, oldZoom, newZoom) =>
            {
                self.ZoomChanged?.Invoke(self.Zoom);
                if (!self.isAnimatingZoom) self.ScrollToKeepViewCenter(oldZoom, newZoom);
            });

    public static readonly MewProperty<bool> CenterContentProperty =
        MewProperty<bool>.Register<ZoomPanCanvas>(nameof(CenterContent), false,
            MewPropertyOptions.AffectsRender);

    bool isPanning;
    bool isAnimatingZoom;
    Point panStart;
    double panStartScrollX;
    double panStartScrollY;
    AnimationClock? zoomClock;
    Tween<double>? zoomTween;
    Action<double>? scrollOnZoomTick;
    ImageScaleQuality? savedImageQuality;

    public event Action<double>? ZoomChanged;
    public UIElement? Child { get => GetValue(ChildProperty); set => SetValue(ChildProperty, value); }
    public double Zoom { get => GetValue(ZoomProperty); set => SetValue(ZoomProperty, Math.Clamp(value, MinZoom, MaxZoom)); }
    public bool CenterContent { get => GetValue(CenterContentProperty); set => SetValue(CenterContentProperty, value); }

    public Size ChildNaturalSize
    {
        get
        {
            if (Child is null) return Size.Empty;
            Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            return Child.DesiredSize;
        }
    }

    public void AnimateZoomWithViewCenter(ScrollViewer viewer, double targetZoom, int durationMs = 250)
    {
        if (!CenterContent || viewer.ViewportWidth <= 0)
        {
            AnimateZoomTo(targetZoom, durationMs);
            return;
        }
        var viewportWidth = viewer.ViewportWidth;
        var viewportHeight = viewer.ViewportHeight;
        var startZoom = Zoom;
        var natural = Child?.DesiredSize ?? Size.Empty;
        var oldCenterX = Math.Max(0, (viewportWidth - natural.Width * startZoom) * 0.5);
        var oldCenterY = Math.Max(0, (viewportHeight - natural.Height * startZoom) * 0.5);
        var worldCenterX = (viewer.HorizontalOffset + viewportWidth * 0.5 - oldCenterX) / startZoom;
        var worldCenterY = (viewer.VerticalOffset + viewportHeight * 0.5 - oldCenterY) / startZoom;
        scrollOnZoomTick = zoom =>
        {
            var centerX = Math.Max(0, (viewportWidth - natural.Width * zoom) * 0.5);
            var centerY = Math.Max(0, (viewportHeight - natural.Height * zoom) * 0.5);
            viewer.SetScrollOffsets(
                Math.Max(0, worldCenterX * zoom + centerX - viewportWidth * 0.5),
                Math.Max(0, worldCenterY * zoom + centerY - viewportHeight * 0.5));
        };
        AnimateZoomTo(targetZoom, durationMs);
    }

    public void AnimateZoomTo(double targetZoom, int durationMs = 250)
    {
        targetZoom = Math.Clamp(targetZoom, MinZoom, MaxZoom);
        zoomClock?.Stop();
        isAnimatingZoom = true;
        var image = Child as Image;
        if (image is not null && savedImageQuality is null)
        {
            savedImageQuality = image.ImageScaleQuality;
            image.ImageScaleQuality = ImageScaleQuality.Fast;
        }
        var scrollAction = scrollOnZoomTick;
        zoomClock = new AnimationClock(TimeSpan.FromMilliseconds(durationMs), Easing.EaseOutCubic);
        zoomClock.CompletedCallback = () =>
        {
            isAnimatingZoom = false;
            scrollOnZoomTick = null;
            if (image is not null && savedImageQuality.HasValue)
            {
                image.ImageScaleQuality = savedImageQuality.Value;
                savedImageQuality = null;
            }
            scrollAction?.Invoke(targetZoom);
        };
        zoomTween = new Tween<double>(Zoom, targetZoom, Lerp.Double);
        zoomTween.ValueChanged += value =>
        {
            Zoom = value;
            Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render, () => scrollAction?.Invoke(value));
        };
        zoomTween.Bind(zoomClock);
        zoomClock.Start();
    }

    protected override Size MeasureContent(Size availableSize)
    {
        if (Child is null) return Size.Empty;
        Child.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var natural = Child.DesiredSize;
        var dpiScale = GetDpi() / 96.0;
        return new Size(
            Math.Floor(natural.Width * Zoom * dpiScale) / dpiScale,
            Math.Floor(natural.Height * Zoom * dpiScale) / dpiScale);
    }

    protected override void ArrangeContent(Rect bounds)
    {
        if (Child is not null)
            Child.Arrange(new Rect(bounds.X, bounds.Y, Child.DesiredSize.Width, Child.DesiredSize.Height));
    }

    protected override void RenderSubtree(IGraphicsContext context)
    {
        if (Child is null) return;
        var bounds = Bounds;
        var zoom = (float)Zoom;
        var natural = Child.DesiredSize;
        var centerX = CenterContent ? (float)Math.Max(0, (bounds.Width - natural.Width * zoom) * 0.5) : 0;
        var centerY = CenterContent ? (float)Math.Max(0, (bounds.Height - natural.Height * zoom) * 0.5) : 0;
        context.Save();
        context.SetClip(bounds);
        context.SetTransform(
            Matrix3x2.CreateTranslation(-(float)bounds.X, -(float)bounds.Y) *
            Matrix3x2.CreateScale(zoom) *
            Matrix3x2.CreateTranslation((float)bounds.X + centerX, (float)bounds.Y + centerY) *
            context.GetTransform());
        Child.Render(context);
        context.Restore();
    }

    protected override UIElement? OnHitTest(Point point) =>
        IsVisible && IsHitTestVisible && Bounds.Contains(point) ? this : null;

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (e.Handled || e.Delta.Y == 0 || Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y)) return;
        var viewer = FindParentScrollViewer();
        if (viewer is null || Child is null) return;
        var oldZoom = Zoom;
        var newZoom = Math.Clamp(oldZoom * Math.Pow(1.15, e.Delta.Y), MinZoom, MaxZoom);
        if (Math.Abs(newZoom - oldZoom) < 1e-9) { e.Handled = true; return; }

        var position = e.GetPosition(this);
        var viewportX = position.X - viewer.HorizontalOffset;
        var viewportY = position.Y - viewer.VerticalOffset;
        var natural = Child.DesiredSize;
        var oldCenterX = CenterContent ? Math.Max(0, (Bounds.Width - natural.Width * oldZoom) * 0.5) : 0;
        var oldCenterY = CenterContent ? Math.Max(0, (Bounds.Height - natural.Height * oldZoom) * 0.5) : 0;
        var ratio = newZoom / oldZoom;
        var newCenterX = CenterContent ? Math.Max(0, (viewer.ViewportWidth - natural.Width * newZoom) * 0.5) : 0;
        var newCenterY = CenterContent ? Math.Max(0, (viewer.ViewportHeight - natural.Height * newZoom) * 0.5) : 0;
        var scrollX = Math.Max(0, (position.X - oldCenterX) * ratio + newCenterX - viewportX);
        var scrollY = Math.Max(0, (position.Y - oldCenterY) * ratio + newCenterY - viewportY);
        isAnimatingZoom = true;
        Zoom = newZoom;
        isAnimatingZoom = false;
        viewer.SetScrollOffsets(scrollX, scrollY);
        e.Handled = true;
        Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render,
            () => viewer.SetScrollOffsets(scrollX, scrollY));
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var viewer = FindParentScrollViewer();
        var root = FindVisualRoot() as UIElement;
        if (e.Handled || e.Button != MouseButton.Left || viewer is null || root is null) return;
        isPanning = true;
        panStart = e.GetPosition(root);
        panStartScrollX = viewer.HorizontalOffset;
        panStartScrollY = viewer.VerticalOffset;
        if (root is Window rootWindow) rootWindow.CaptureMouse(this);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var viewer = FindParentScrollViewer();
        var root = FindVisualRoot() as UIElement;
        if (!isPanning || viewer is null || root is null) return;
        var position = e.GetPosition(root);
        viewer.SetScrollOffsets(
            Math.Max(0, panStartScrollX - (position.X - panStart.X)),
            Math.Max(0, panStartScrollY - (position.Y - panStart.Y)));
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (!isPanning) return;
        isPanning = false;
        if (FindVisualRoot() is Window rootWindow) rootWindow.ReleaseMouseCapture();
    }

    void ScrollToKeepViewCenter(double oldZoom, double newZoom)
    {
        var viewer = FindParentScrollViewer();
        if (!CenterContent || viewer is null || Child is null ||
            viewer.ViewportWidth <= 0 || viewer.ViewportHeight <= 0) return;
        var natural = Child.DesiredSize;
        var oldCenterX = Math.Max(0, (viewer.ViewportWidth - natural.Width * oldZoom) * 0.5);
        var oldCenterY = Math.Max(0, (viewer.ViewportHeight - natural.Height * oldZoom) * 0.5);
        var worldCenterX = (viewer.HorizontalOffset + viewer.ViewportWidth * 0.5 - oldCenterX) / oldZoom;
        var worldCenterY = (viewer.VerticalOffset + viewer.ViewportHeight * 0.5 - oldCenterY) / oldZoom;
        var newCenterX = Math.Max(0, (viewer.ViewportWidth - natural.Width * newZoom) * 0.5);
        var newCenterY = Math.Max(0, (viewer.ViewportHeight - natural.Height * newZoom) * 0.5);
        var scrollX = Math.Max(0, worldCenterX * newZoom + newCenterX - viewer.ViewportWidth * 0.5);
        var scrollY = Math.Max(0, worldCenterY * newZoom + newCenterY - viewer.ViewportHeight * 0.5);
        viewer.SetScrollOffsets(scrollX, scrollY);
        Application.Current.Dispatcher?.BeginInvoke(DispatcherPriority.Render,
            () => viewer.SetScrollOffsets(scrollX, scrollY));
    }

    ScrollViewer? FindParentScrollViewer()
    {
        var current = Parent;
        while (current is not null)
        {
            if (current is ScrollViewer viewer) return viewer;
            current = current.Parent;
        }
        return null;
    }

    bool IVisualTreeHost.VisitChildren(Func<Element, bool> visitor) => Child is null || visitor(Child);
}

abstract class OffscreenControl : Control
{
    IRenderSurface? surface;
    IGraphicsContext? offscreenContext;
    IImage? view;
    int surfaceWidth;
    int surfaceHeight;
    double surfaceDpiScale;
    bool dirty = true;

    protected abstract void RenderOffscreen(IGraphicsContext context, Rect bounds);

    protected void InvalidateOffscreen()
    {
        dirty = true;
        InvalidateVisual();
    }

    void EnsureSurface()
    {
        var dpiScale = GetDpi() / 96.0;
        var pixelWidth = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpiScale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpiScale));

        if (surface is not null && surfaceWidth == pixelWidth &&
            surfaceHeight == pixelHeight && surfaceDpiScale == dpiScale)
            return;

        offscreenContext?.Dispose();
        view?.Dispose();
        surface?.Dispose();

        var factory = GetGraphicsFactory();
        surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(pixelWidth, pixelHeight, dpiScale));
        view = factory.CreateImageView(surface);
        offscreenContext = factory.CreateContext(surface);
        surfaceWidth = pixelWidth;
        surfaceHeight = pixelHeight;
        surfaceDpiScale = dpiScale;
        dirty = true;
    }

    protected override void OnRender(IGraphicsContext context)
    {
        base.OnRender(context);
        EnsureSurface();
        if (surface is null || offscreenContext is null || view is null)
            return;

        if (dirty)
        {
            offscreenContext.BeginFrame(surface);
            RenderOffscreen(offscreenContext, new Rect(0, 0, ActualWidth, ActualHeight));
            offscreenContext.EndFrame();
            dirty = false;
        }
        context.DrawImage(view, Bounds);
    }

    protected override void OnDispose()
    {
        base.OnDispose();
        offscreenContext?.Dispose();
        view?.Dispose();
        surface?.Dispose();
    }
}

sealed class SampleOffscreenControl : OffscreenControl
{
    bool alternate;

    protected override void RenderOffscreen(IGraphicsContext context, Rect bounds)
    {
        Point firstStart, firstEnd, secondStart, secondEnd;
        if (alternate)
        {
            (firstStart, firstEnd, secondStart, secondEnd) =
                (bounds.TopLeft, bounds.BottomRight, bounds.TopRight, bounds.BottomLeft);
        }
        else
        {
            (firstStart, firstEnd, secondStart, secondEnd) =
                (bounds.TopRight, bounds.BottomLeft, bounds.TopLeft, bounds.BottomRight);
        }
        context.DrawLine(firstStart, firstEnd, Color.Green, 1);
        context.DrawLine(secondStart, secondEnd, Color.Blue, 3);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        alternate = !alternate;
        InvalidateOffscreen();
    }
}

static class SegmentIcons
{
    // ButtonGroup / SegmentedControl icons are intentionally code-owned PathGeometry values.
    // They do not depend on the asynchronously downloaded Icons.xaml resource.
    static readonly PathGeometry Cut = PathGeometry.Parse(
        "M12.1409 9.34138L12.14 9.34274L7.37017 2.32828C7.13725 1.98575 6.67077 1.8969 6.32824 2.12982C5.98572 2.36273 5.89687 2.82922 6.12978 3.17174L11.2606 10.7169L8.86478 14.4604C8.30797 14.1664 7.67342 14 7 14C4.79086 14 3 15.7909 3 18C3 20.2091 4.79086 22 7 22C9.20914 22 11 20.2091 11 18C11 17.0088 10.6395 16.1018 10.0424 15.403L12.178 12.0661L14.2426 15.1023C13.4771 15.8309 13 16.8597 13 18C13 20.2091 14.7909 22 17 22C19.2091 22 21 20.2091 21 18C21 15.7909 19.2091 14 17 14C16.471 14 15.9659 14.1027 15.5037 14.2893L13.0575 10.6919L13.0588 10.6899L12.1409 9.34138ZM4.5 18C4.5 16.6193 5.61929 15.5 7 15.5C8.38071 15.5 9.5 16.6193 9.5 18C9.5 19.3807 8.38071 20.5 7 20.5C5.61929 20.5 4.5 19.3807 4.5 18ZM14.5 18C14.5 16.6193 15.6193 15.5 17 15.5C18.3807 15.5 19.5 16.6193 19.5 18C19.5 19.3807 18.3807 20.5 17 20.5C15.6193 20.5 14.5 19.3807 14.5 18Z M13.9381 9.31594L17.8815 3.15426C18.1048 2.80538 18.003 2.34155 17.6541 2.11827C17.3053 1.89498 16.8414 1.9968 16.6181 2.34568L13.0202 7.96744L13.9381 9.31594Z");
    static readonly PathGeometry Copy = PathGeometry.Parse(
        "M5.50280381,4.62704038 L5.5,6.75 L5.5,17.2542087 C5.5,19.0491342 6.95507456,20.5042087 8.75,20.5042087 L17.3662868,20.5044622 C17.057338,21.3782241 16.2239751,22.0042087 15.2444057,22.0042087 L8.75,22.0042087 C6.12664744,22.0042087 4,19.8775613 4,17.2542087 L4,6.75 C4,5.76928848 4.62744523,4.93512464 5.50280381,4.62704038 Z M17.75,2 C18.9926407,2 20,3.00735931 20,4.25 L20,17.25 C20,18.4926407 18.9926407,19.5 17.75,19.5 L8.75,19.5 C7.50735931,19.5 6.5,18.4926407 6.5,17.25 L6.5,4.25 C6.5,3.00735931 7.50735931,2 8.75,2 L17.75,2 Z M17.75,3.5 L8.75,3.5 C8.33578644,3.5 8,3.83578644 8,4.25 L8,17.25 C8,17.6642136 8.33578644,18 8.75,18 L17.75,18 C18.1642136,18 18.5,17.6642136 18.5,17.25 L18.5,4.25 C18.5,3.83578644 18.1642136,3.5 17.75,3.5 Z");
    static readonly PathGeometry Paste = PathGeometry.Parse(
        "M12.7533481,2 C13.9109409,2 14.8640519,2.87549091 14.9866651,4.00045683 L16.75,4 C17.940864,4 18.9156449,4.92516159 18.9948092,6.09595119 L19,6.25 C19,6.6291895 18.7182223,6.94256631 18.3526349,6.99216251 L18.249,6.999 C17.8698105,6.999 17.5564337,6.71722232 17.5068375,6.35163486 L17.5,6.25 C17.5,5.87030423 17.2178461,5.55650904 16.8517706,5.50684662 L16.75,5.5 L14.6176299,5.50081624 C14.2140619,6.09953034 13.5296904,6.49330383 12.7533481,6.49330383 L9.24665191,6.49330383 C8.47030963,6.49330383 7.78593808,6.09953034 7.38237013,5.50081624 L5.25,5.5 C4.87030423,5.5 4.55650904,5.78215388 4.50684662,6.14822944 L4.5,6.25 L4.5,19.754591 C4.5,20.1342868 4.78215388,20.448082 5.14822944,20.4977444 L5.25,20.504591 L8.25000001,20.5041182 C8.62963593,20.5040584 8.94342614,20.7861183 8.99313842,21.1521284 L9,21.254 C9,21.6682327 8.66423269,22.0040529 8.25000001,22.0041182 L5.25,22.004591 C4.05913601,22.004591 3.08435508,21.0794294 3.00519081,19.9086398 L3,19.754591 L3,6.25 C3,5.05913601 3.92516159,4.08435508 5.09595119,4.00519081 L5.25,4 L7.01333493,4.00045683 C7.13594814,2.87549091 8.0890591,2 9.24665191,2 L12.7533481,2 Z M18.75,8 C19.940864,8 20.9156449,8.92516159 20.9948092,10.0959512 L21,10.25 L21,19.75 C21,20.940864 20.0748384,21.9156449 18.9040488,21.9948092 L18.75,22 L12.25,22 C11.059136,22 10.0843551,21.0748384 10.0051908,19.9040488 L10,19.75 L10,10.25 C10,9.05913601 10.9251616,8.08435508 12.0959512,8.00519081 L12.25,8 L18.75,8 Z M18.75,9.5 L12.25,9.5 C11.8703042,9.5 11.556509,9.78215388 11.5068466,10.1482294 L11.5,10.25 L11.5,19.75 C11.5,20.1296958 11.7821539,20.443491 12.1482294,20.4931534 L12.25,20.5 L18.75,20.5 C19.1296958,20.5 19.443491,20.2178461 19.4931534,19.8517706 L19.5,19.75 L19.5,10.25 C19.5,9.87030423 19.2178461,9.55650904 18.8517706,9.50684662 L18.75,9.5 Z M12.7533481,3.5 L9.24665191,3.5 C8.83428745,3.5 8.5,3.83428745 8.5,4.24665191 C8.5,4.65901638 8.83428745,4.99330383 9.24665191,4.99330383 L12.7533481,4.99330383 C13.1657126,4.99330383 13.5,4.65901638 13.5,4.24665191 C13.5,3.83428745 13.1657126,3.5 12.7533481,3.5 Z");
    static readonly PathGeometry AlignLeft = PathGeometry.Parse(
        "M13.25,18 C13.6642,18 14,18.3358 14,18.75 C14,19.1642 13.6642,19.5 13.25,19.5 L2.75,19.5 C2.33579,19.5 2,19.1642 2,18.75 C2,18.3358 2.33579,18 2.75,18 L13.25,18 Z M21.25,11.5 C21.6642,11.5 22,11.8358 22,12.25 C22,12.6296833 21.7178347,12.9434889 21.3517677,12.9931531 L21.25,13 L2.75,13 C2.33579,13 2,12.6642 2,12.25 C2,11.8703167 2.28215688,11.5565111 2.64823019,11.5068469 L2.75,11.5 L21.25,11.5 Z M18.25,5 C18.6642,5 19,5.33579 19,5.75 C19,6.16421 18.6642,6.5 18.25,6.5 L2.75,6.5 C2.33579,6.5 2,6.16421 2,5.75 C2,5.33579 2.33579,5 2.75,5 L18.25,5 Z");
    static readonly PathGeometry AlignCenter = PathGeometry.Parse(
        "M17.25,18 C17.6642,18 18,18.3358 18,18.75 C18,19.1642 17.6642,19.5 17.25,19.5 L6.75,19.5 C6.33579,19.5 6,19.1642 6,18.75 C6,18.3358 6.33579,18 6.75,18 L17.25,18 Z M21.25,11.5 C21.6642,11.5 22,11.8358 22,12.25 C22,12.6296833 21.7178347,12.9434889 21.3517677,12.9931531 L21.25,13 L2.75,13 C2.33579,13 2,12.6642 2,12.25 C2,11.8703167 2.28215688,11.5565111 2.64823019,11.5068469 L2.75,11.5 L21.25,11.5 Z M19.25,5 C19.6642,5 20,5.33579 20,5.75 C20,6.16421 19.6642,6.5 19.25,6.5 L4.75,6.5 C4.33579,6.5 4,6.16421 4,5.75 C4,5.33579 4.33579,5 4.75,5 L19.25,5 Z");
    static readonly PathGeometry AlignRight = PathGeometry.Parse(
        "M21.25,18 C21.6642,18 22,18.3358 22,18.75 C22,19.1642 21.6642,19.5 21.25,19.5 L10.75,19.5 C10.3358,19.5 10,19.1642 10,18.75 C10,18.3358 10.3358,18 10.75,18 L21.25,18 Z M21.25,11.5 C21.6642,11.5 22,11.8358 22,12.25 C22,12.6296833 21.7178347,12.9434889 21.3517677,12.9931531 L21.25,13 L2.75,13 C2.33579,13 2,12.6642 2,12.25 C2,11.8703167 2.28215688,11.5565111 2.64823019,11.5068469 L2.75,11.5 L21.25,11.5 Z M21.25,5 C21.6642,5 22,5.33579 22,5.75 C22,6.16421 21.6642,6.5 21.25,6.5 L5.75,6.5 C5.33579,6.5 5,6.16421 5,5.75 C5,5.33579 5.33579,5 5.75,5 L21.25,5 Z");
    static readonly PathGeometry AppsList = PathGeometry.Parse(
        "M6.24787561,16.0021244 C7.21437393,16.0021244 7.99787561,16.7856261 7.99787561,17.7521244 L7.99787561,20.25 C7.99787561,21.2164983 7.21437393,22 6.24787561,22 L3.75,22 C2.78350169,22 2,21.2164983 2,20.25 L2,17.7521244 C2,16.7856261 2.78350169,16.0021244 3.75,16.0021244 L6.24787561,16.0021244 Z M6.24787561,17.5021244 L3.75,17.5021244 C3.61192881,17.5021244 3.5,17.6140532 3.5,17.7521244 L3.5,20.25 C3.5,20.3880712 3.61192881,20.5 3.75,20.5 L6.24787561,20.5 C6.3859468,20.5 6.49787561,20.3880712 6.49787561,20.25 L6.49787561,17.7521244 C6.49787561,17.6140532 6.3859468,17.5021244 6.24787561,17.5021244 Z M9.74809326,18 L21.2528964,18 C21.66711,18 22.0028964,18.3357864 22.0028964,18.75 C22.0028964,19.1296958 21.7207425,19.443491 21.354667,19.4931534 L21.2528964,19.5 L9.74809326,19.5 C9.3338797,19.5 8.99809326,19.1642136 8.99809326,18.75 C8.99809326,18.3703042 9.28024715,18.056509 9.64632271,18.0068466 L9.74809326,18 L21.2528964,18 L9.74809326,18 Z M6.24787561,9.00106219 C7.21437393,9.00106219 7.99787561,9.78456388 7.99787561,10.7510622 L7.99787561,13.2489378 C7.99787561,14.2154361 7.21437393,14.9989378 6.24787561,14.9989378 L3.75,14.9989378 C2.78350169,14.9989378 2,14.2154361 2,13.2489378 L2,10.7510622 C2,9.78456388 2.78350169,9.00106219 3.75,9.00106219 L6.24787561,9.00106219 Z M6.24787561,10.5010622 L3.75,10.5010622 C3.61192881,10.5010622 3.5,10.612991 3.5,10.7510622 L3.5,13.2489378 C3.5,13.387009 3.61192881,13.4989378 3.75,13.4989378 L6.24787561,13.4989378 C6.3859468,13.4989378 6.49787561,13.387009 6.49787561,13.2489378 L6.49787561,10.7510622 C6.49787561,10.612991 6.3859468,10.5010622 6.24787561,10.5010622 Z M9.74809326,11 L21.2528964,11 C21.66711,11 22.0028964,11.3357864 22.0028964,11.75 C22.0028964,12.1296958 21.7207425,12.443491 21.354667,12.4931534 L21.2528964,12.5 L9.74809326,12.5 C9.3338797,12.5 8.99809326,12.1642136 8.99809326,11.75 C8.99809326,11.3703042 9.28024715,11.056509 9.64632271,11.0068466 L9.74809326,11 L21.2528964,11 L9.74809326,11 Z M6.24787561,2 C7.21437393,2 7.99787561,2.78350169 7.99787561,3.75 L7.99787561,6.24787561 C7.99787561,7.21437393 7.21437393,7.99787561 6.24787561,7.99787561 L3.75,7.99787561 C2.78350169,7.99787561 2,7.21437393 2,6.24787561 L2,3.75 C2,2.78350169 2.78350169,2 3.75,2 L6.24787561,2 Z M6.24787561,3.5 L3.75,3.5 C3.61192881,3.5 3.5,3.61192881 3.5,3.75 L3.5,6.24787561 C3.5,6.3859468 3.61192881,6.49787561 3.75,6.49787561 L6.24787561,6.49787561 C6.3859468,6.49787561 6.49787561,6.3859468 6.49787561,6.24787561 L6.49787561,3.75 C6.49787561,3.61192881 6.3859468,3.5 6.24787561,3.5 Z M9.74809326,4 L21.2528964,4 C21.66711,4 22.0028964,4.33578644 22.0028964,4.75 C22.0028964,5.12969577 21.7207425,5.44349096 21.354667,5.49315338 L21.2528964,5.5 L9.74809326,5.5 C9.3338797,5.5 8.99809326,5.16421356 8.99809326,4.75 C8.99809326,4.37030423 9.28024715,4.05650904 9.64632271,4.00684662 L9.74809326,4 L21.2528964,4 L9.74809326,4 Z");
    static readonly PathGeometry Table = PathGeometry.Parse(
        "M17.75,3 C19.5449,3 21,4.45507 21,6.25 L21,17.75 C21,19.5449 19.5449,21 17.75,21 L6.25,21 C4.45507,21 3,19.5449 3,17.75 L3,6.25 C3,4.45507 4.45507,3 6.25,3 L17.75,3 Z M14,15.5 L10,15.5 L10,19.5 L14,19.5 L14,15.5 Z M19.5,15.5 L15.5,15.5 L15.5,19.5 L17.75,19.5 C18.7165,19.5 19.5,18.7165 19.5,17.75 L19.5,17.75 L19.5,15.5 Z M8.5,15.5 L4.5,15.5 L4.5,17.75 C4.5,18.7165 5.2835,19.5 6.25,19.5 L6.25,19.5 L8.5,19.5 L8.5,15.5 Z M8.5,10 L4.5,10 L4.5,14 L8.5,14 L8.5,10 Z M14,10 L10,10 L10,14 L14,14 L14,10 Z M19.5,10 L15.5,10 L15.5,14 L19.5,14 L19.5,10 Z M8.5,4.5 L6.25,4.5 C5.2835,4.5 4.5,5.2835 4.5,6.25 L4.5,8.5 L8.5,8.5 L8.5,4.5 Z M17.75,4.5 L15.5,4.5 L15.5,8.5 L19.5,8.5 L19.5,6.25 C19.5,5.2835 18.7165,4.5 17.75,4.5 L17.75,4.5 Z M14,4.5 L10,4.5 L10,8.5 L14,8.5 L14,4.5 Z");
    static readonly PathGeometry DataPie = PathGeometry.Parse(
        "M10.25,4.25 C10.6642136,4.25 11,4.58578644 11,5 L11,13 L19,13 C19.3796958,13 19.693491,13.2821535 19.7431534,13.648229 L19.75,13.75 C19.75,18.7205627 15.7205627,22.25 10.75,22.25 C5.77943725,22.25 1.75,18.2205627 1.75,13.25 C1.75,8.27943725 5.27943725,4.25 10.25,4.25 Z M9.5,5.787 L9.291095,5.81072583 C5.60179657,6.28154825 3.25,9.43273776 3.25,13.25 C3.25,17.3921356 6.60786438,20.75 10.75,20.75 C14.5672622,20.75 17.7184517,18.3982034 18.1892742,14.708905 L18.212,14.5 L10.25,14.5 C9.8703042,14.5 9.5565094,14.2178465 9.506847,13.851771 L9.5,13.75 L9.5,5.787 Z M13.25,1.75 C18.2205627,1.75 22.25,5.77943725 22.25,10.75 C22.25,11.1642136 21.9142136,11.5 21.5,11.5 L21.5,11.5 L13.25,11.5 C12.8357864,11.5 12.5,11.1642136 12.5,10.75 L12.5,10.75 L12.5,2.5 C12.5,2.08578644 12.8357864,1.75 13.25,1.75 Z M14,3.287 L14,10 L20.712,10 L20.6892742,9.79109482 C20.2585217,6.41577923 17.5842208,3.74147826 14.2089052,3.31072583 L14.2089052,3.31072583 L14,3.287 Z");
    static readonly PathGeometry Home = PathGeometry.Parse(
        "M21.6062 5.85517C23.0048 4.71494 24.9952 4.71494 26.3938 5.85517L39.5688 16.5966C40.4736 17.3342 41 18.4492 41 19.628V39.1134C41 41.2599 39.2875 43 37.175 43H32.075C29.9625 43 28.25 41.2599 28.25 39.1134V29.7492C28.25 29.0337 27.6792 28.4536 26.975 28.4536H21.025C20.3208 28.4536 19.75 29.0337 19.75 29.7492V39.1134C19.75 41.2599 18.0375 43 15.925 43H10.825C8.71251 43 7 41.2599 7 39.1134V19.628C7 18.4493 7.52645 17.3342 8.43124 16.5966L21.6062 5.85517ZM24.7979 7.87612C24.3317 7.49604 23.6683 7.49604 23.2021 7.87612L10.0271 18.6175C9.72548 18.8634 9.55 19.2351 9.55 19.628V39.1134C9.55 39.8289 10.1208 40.4089 10.825 40.4089H15.925C16.6292 40.4089 17.2 39.8289 17.2 39.1134V29.7492C17.2 27.6027 18.9125 25.8626 21.025 25.8626H26.975C29.0875 25.8626 30.8 27.6027 30.8 29.7492V39.1134C30.8 39.8289 31.3708 40.4089 32.075 40.4089H37.175C37.8792 40.4089 38.45 39.8289 38.45 39.1134V19.628C38.45 19.2351 38.2745 18.8634 37.9729 18.6175L24.7979 7.87612Z");
    static readonly PathGeometry Calendar = PathGeometry.Parse(
        "M21.75,3 C23.5449254,3 25,4.45507456 25,6.25 L25,21.75 C25,23.5449254 23.5449254,25 21.75,25 L6.25,25 C4.45507456,25 3,23.5449254 3,21.75 L3,6.25 C3,4.45507456 4.45507456,3 6.25,3 L21.75,3 Z M23.5,9.503 L4.5,9.503 L4.5,21.75 C4.5,22.7164983 5.28350169,23.5 6.25,23.5 L21.75,23.5 C22.7164983,23.5 23.5,22.7164983 23.5,21.75 L23.5,9.503 Z M8.74877715,17.5014095 C9.43913308,17.5014095 9.99877715,18.0610536 9.99877715,18.7514095 C9.99877715,19.4417655 9.43913308,20.0014095 8.74877715,20.0014095 C8.05842121,20.0014095 7.49877715,19.4417655 7.49877715,18.7514095 C7.49877715,18.0610536 8.05842121,17.5014095 8.74877715,17.5014095 Z M14.0032585,17.5014095 C14.6936145,17.5014095 15.2532585,18.0610536 15.2532585,18.7514095 C15.2532585,19.4417655 14.6936145,20.0014095 14.0032585,20.0014095 C13.3129026,20.0014095 12.7532585,19.4417655 12.7532585,18.7514095 C12.7532585,18.0610536 13.3129026,17.5014095 14.0032585,17.5014095 Z M8.74877715,12.5014095 C9.43913308,12.5014095 9.99877715,13.0610536 9.99877715,13.7514095 C9.99877715,14.4417655 9.43913308,15.0014095 8.74877715,15.0014095 C8.05842121,15.0014095 7.49877715,14.4417655 7.49877715,13.7514095 C7.49877715,13.0610536 8.05842121,12.5014095 8.74877715,12.5014095 Z M14.0032585,12.5014095 C14.6936145,12.5014095 15.2532585,13.0610536 15.2532585,13.7514095 C15.2532585,14.4417655 14.6936145,15.0014095 14.0032585,15.0014095 C13.3129026,15.0014095 12.7532585,14.4417655 12.7532585,13.7514095 C12.7532585,13.0610536 13.3129026,12.5014095 14.0032585,12.5014095 Z M19.2577399,12.5014095 C19.9480958,12.5014095 20.5077399,13.0610536 20.5077399,13.7514095 C20.5077399,14.4417655 19.9480958,15.0014095 19.2577399,15.0014095 C18.567384,15.0014095 18.0077399,14.4417655 18.0077399,13.7514095 C18.0077399,13.0610536 18.567384,12.5014095 19.2577399,12.5014095 Z M21.75,4.5 L6.25,4.5 C5.28350169,4.5 4.5,5.28350169 4.5,6.25 L4.5,8.003 L23.5,8.003 L23.5,6.25 C23.5,5.28350169 22.7164983,4.5 21.75,4.5 Z");

    public static PathGeometry Get(string name) => name switch
    {
        "cut_regular" => Cut,
        "copy_regular" => Copy,
        "clipboard_paste_regular" => Paste,
        "text_align_left_regular" => AlignLeft,
        "text_align_center_regular" => AlignCenter,
        "text_align_right_regular" => AlignRight,
        "apps_list_regular" => AppsList,
        "table_regular" => Table,
        "data_pie_regular" => DataPie,
        "home_regular" => Home,
        "settings_regular" => NavigationIcons.Settings,
        "calendar_regular" => Calendar,
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown segment icon."),
    };
}

static class AdditionalNavigationIcons
{
    public static readonly PathGeometry DragDrop = PathGeometry.Parse("M12,16.5 C12.3796958,16.5 12.693491,16.7821539 12.7431534,17.1482294 L12.75,17.25 L12.75,19.438 L13.4696699,18.7196699 C13.7359365,18.4534034 14.1526002,18.4291973 14.4462117,18.6470518 L14.5303301,18.7196699 C14.7965966,18.9859365 14.8208027,19.4026002 14.6029482,19.6962117 L14.5303301,19.7803301 L12.5303301,21.7803301 L12.4921785,21.8159302 L12.4921785,21.8159302 L12.42005,21.8715034 L12.42005,21.8715034 L12.3251715,21.9260696 L12.3251715,21.9260696 L12.2386647,21.9611871 L12.2386647,21.9611871 L12.136298,21.9875819 L12.136298,21.9875819 L12.0514189,21.9982462 L12.0514189,21.9982462 L11.9484646,21.9982462 L11.9484646,21.9982462 L11.828633,21.9802653 L11.828633,21.9802653 L11.7607164,21.9610197 L11.7607164,21.9610197 L11.7018614,21.9383984 L11.7018614,21.9383984 L11.6309547,21.9031392 L11.6309547,21.9031392 L11.5796709,21.8712371 L11.5796709,21.8712371 L11.5483807,21.8488352 C11.5205706,21.8278288 11.4942698,21.8049299 11.4696699,21.7803301 L9.46966991,19.7803301 C9.1767767,19.4874369 9.1767767,19.0125631 9.46966991,18.7196699 C9.73593648,18.4534034 10.1526002,18.4291973 10.4462117,18.6470518 L10.5303301,18.7196699 L11.25,19.439 L11.25,17.25 C11.25,16.8703042 11.5321539,16.556509 11.8982294,16.5068466 L12,16.5 Z M12,9 C13.6568542,9 15,10.3431458 15,12 C15,13.6568542 13.6568542,15 12,15 C10.3431458,15 9,13.6568542 9,12 C9,10.3431458 10.3431458,9 12,9 Z M18.7196699,9.46966991 C18.9859365,9.20340335 19.4026002,9.1791973 19.6962117,9.39705176 L19.7803301,9.46966991 L21.7803301,11.4696699 L21.8159302,11.5078215 L21.8159302,11.5078215 L21.8715034,11.57995 L21.8715034,11.57995 L21.9260696,11.6748285 L21.9260696,11.6748285 L21.9611871,11.7613353 L21.9611871,11.7613353 L21.9875819,11.863702 L21.9875819,11.863702 L21.9982462,11.9485764 L21.9982462,11.9485764 L21.9982462,12.0514422 L21.9982462,12.0514422 L21.9802653,12.171367 L21.9802653,12.171367 L21.9610197,12.2392836 L21.9610197,12.2392836 L21.9383984,12.2981386 L21.9383984,12.2981386 L21.9031392,12.3690453 L21.9031392,12.3690453 L21.8712371,12.4203291 L21.8712371,12.4203291 L21.8437281,12.4583026 L21.8437281,12.4583026 L21.7803301,12.5303301 L19.7803301,14.5303301 C19.4874369,14.8232233 19.0125631,14.8232233 18.7196699,14.5303301 C18.4534034,14.2640635 18.4291973,13.8473998 18.6470518,13.5537883 L18.7196699,13.4696699 L19.438,12.75 L17.25,12.75 C16.8703042,12.75 16.556509,12.4678461 16.5068466,12.1017706 L16.5,12 C16.5,11.6203042 16.7821539,11.306509 17.1482294,11.2568466 L17.25,11.25 L19.439,11.25 L18.7196699,10.5303301 C18.4534034,10.2640635 18.4291973,9.84739984 18.6470518,9.55378835 L18.7196699,9.46966991 Z M4.21966991,9.46966991 C4.51256313,9.1767767 4.98743687,9.1767767 5.28033009,9.46966991 C5.54659665,9.73593648 5.5708027,10.1526002 5.35294824,10.4462117 L5.28033009,10.5303301 L4.56,11.25 L6.75,11.25 C7.12969577,11.25 7.44349096,11.5321539 7.49315338,11.8982294 L7.5,12 C7.5,12.3796958 7.21784612,12.693491 6.85177056,12.7431534 L6.75,12.75 L4.561,12.75 L5.28033009,13.4696699 C5.54659665,13.7359365 5.5708027,14.1526002 5.35294824,14.4462117 L5.28033009,14.5303301 C5.01406352,14.7965966 4.59739984,14.8208027 4.30378835,14.6029482 L4.21966991,14.5303301 L2.21966991,12.5303301 L2.12849655,12.42005 L2.12849655,12.42005 L2.0739304,12.3251715 L2.0739304,12.3251715 L2.03881291,12.2386647 L2.03881291,12.2386647 L2.01241813,12.136298 L2.01241813,12.136298 L2.00136967,12.0454463 L2.00136967,12.0454463 L2.00146424,11.952946 L2.00146424,11.952946 L2.01973472,11.828633 L2.01973472,11.828633 L2.03898031,11.7607164 L2.03898031,11.7607164 L2.06160159,11.7018614 L2.06160159,11.7018614 L2.09686081,11.6309547 L2.09686081,11.6309547 L2.1287629,11.5796709 L2.1287629,11.5796709 L2.15116475,11.5483807 C2.17217116,11.5205706 2.19507007,11.4942698 2.21966991,11.4696699 L4.21966991,9.46966991 Z M12,10.5 C11.1715729,10.5 10.5,11.1715729 10.5,12 C10.5,12.8284271 11.1715729,13.5 12,13.5 C12.8284271,13.5 13.5,12.8284271 13.5,12 C13.5,11.1715729 12.8284271,10.5 12,10.5 Z M11.863702,2.01241813 L11.9306376,2.00316285 L11.9306376,2.00316285 L12.0173828,2.00019748 L12.0173828,2.00019748 L12.077404,2.0039807 L12.077404,2.0039807 L12.171367,2.01973472 L12.171367,2.01973472 L12.2392836,2.03898031 L12.2392836,2.03898031 L12.2981386,2.06160159 L12.2981386,2.06160159 L12.3690453,2.09686081 L12.3690453,2.09686081 L12.4203291,2.1287629 L12.4203291,2.1287629 L12.4583026,2.15627192 L12.4583026,2.15627192 L12.5303301,2.21966991 L14.5303301,4.21966991 C14.8232233,4.51256313 14.8232233,4.98743687 14.5303301,5.28033009 C14.2640635,5.54659665 13.8473998,5.5708027 13.5537883,5.35294824 L13.4696699,5.28033009 L12.75,4.561 L12.75,6.75 C12.75,7.12969577 12.4678461,7.44349096 12.1017706,7.49315338 L12,7.5 C11.6203042,7.5 11.306509,7.21784612 11.2568466,6.85177056 L11.25,6.75 L11.25,4.56 L10.5303301,5.28033009 C10.2640635,5.54659665 9.84739984,5.5708027 9.55378835,5.35294824 L9.46966991,5.28033009 C9.20340335,5.01406352 9.1791973,4.59739984 9.39705176,4.30378835 L9.46966991,4.21966991 L11.4696699,2.21966991 L11.57995,2.12849655 L11.57995,2.12849655 L11.6748285,2.0739304 L11.6748285,2.0739304 L11.7613353,2.03881291 L11.7613353,2.03881291 L11.863702,2.01241813 L11.863702,2.01241813 Z");
    public static readonly PathGeometry TreeView = PathGeometry.Parse("M7.24924 16C7.93921 16 8.49853 16.5593 8.49853 17.2493C8.49853 17.9393 7.93921 18.4986 7.24924 18.4986C6.55928 18.4986 5.99995 17.9393 5.99995 17.2493C5.99995 16.5593 6.55928 16 7.24924 16ZM10.75 16.5H21.25C21.6642 16.5 22 16.8358 22 17.25C22 17.6297 21.7178 17.9435 21.3517 17.9932L21.25 18H10.75C10.3357 18 9.99995 17.6642 9.99995 17.25C9.99995 16.8703 10.2821 16.5565 10.6482 16.5068L10.75 16.5ZM3.24904 11C3.93901 11 4.49833 11.5593 4.49833 12.2493C4.49833 12.9393 3.93901 13.4986 3.24904 13.4986C2.55908 13.4986 1.99976 12.9393 1.99976 12.2493C1.99976 11.5593 2.55908 11 3.24904 11ZM6.74976 11.5H21.25C21.6642 11.5 22 11.8358 22 12.25C22 12.6297 21.7178 12.9435 21.3517 12.9932L21.25 13H6.74976C6.33554 13 5.99976 12.6642 5.99976 12.25C5.99976 11.8703 6.28191 11.5565 6.64799 11.5068L6.74976 11.5ZM3.24924 6C3.93921 6 4.49853 6.55933 4.49853 7.24929C4.49853 7.93925 3.93921 8.49858 3.24924 8.49858C2.55928 8.49858 1.99995 7.93925 1.99995 7.24929C1.99995 6.55933 2.55928 6 3.24924 6ZM6.74995 6.5H21.25C21.6642 6.5 22 6.83579 22 7.25C22 7.6297 21.7178 7.94349 21.3517 7.99315L21.25 8H6.74995C6.33574 8 5.99995 7.66421 5.99995 7.25C5.99995 6.8703 6.28211 6.55651 6.64818 6.50685L6.74995 6.5Z");
    public static readonly PathGeometry ItemsControl = PathGeometry.Parse("M11.0656 8.00389L11.25 7.99875H18.75C20.483 7.99875 21.8992 9.3552 21.9949 11.0643L22 11.2487V18.7487C22 20.4818 20.6435 21.898 18.9344 21.9936L18.75 21.9987H11.25C9.51697 21.9987 8.10075 20.6423 8.00515 18.9332L8 18.7487V11.2487C8 9.51571 9.35646 8.0995 11.0656 8.00389ZM18.75 9.49875H11.25C10.3318 9.49875 9.57881 10.2059 9.5058 11.1052L9.5 11.2487V18.7487C9.5 19.6669 10.2071 20.4199 11.1065 20.4929L11.25 20.4987H18.75C19.6682 20.4987 20.4212 19.7916 20.4942 18.8923L20.5 18.7487V11.2487C20.5 10.2822 19.7165 9.49875 18.75 9.49875ZM15 11C15.4142 11 15.75 11.3358 15.75 11.75V14.248L18.25 14.2487C18.6642 14.2487 19 14.5845 19 14.9987C19 15.413 18.6642 15.7487 18.25 15.7487L15.75 15.748V18.25C15.75 18.6642 15.4142 19 15 19C14.5858 19 14.25 18.6642 14.25 18.25V15.748L11.75 15.7487C11.3358 15.7487 11 15.413 11 14.9987C11 14.5845 11.3358 14.2487 11.75 14.2487L14.25 14.248V11.75C14.25 11.3358 14.5858 11 15 11ZM15.5818 4.23284L15.6345 4.40964L16.327 6.998H14.774L14.1856 4.79787C13.9355 3.86431 12.9759 3.31029 12.0423 3.56044L4.79787 5.50158C3.91344 5.73856 3.36966 6.61227 3.52756 7.49737L3.56044 7.64488L5.50158 14.8893C5.69372 15.6064 6.30445 16.0996 7.00045 16.1764L7.00056 17.6816C5.69932 17.6051 4.52962 16.7445 4.10539 15.4544L4.05269 15.2776L2.11155 8.03311C1.66301 6.35913 2.6067 4.6401 4.23284 4.10539L4.40964 4.05269L11.6541 2.11155C13.3281 1.66301 15.0471 2.6067 15.5818 4.23284Z");
    public static readonly PathGeometry Transform = PathGeometry.Parse("M2.5,11 C2.77614,11 3,11.2239 3,11.5 L3,15 C3,16.1046 3.89543,17 5,17 L8.5,17 C8.77614,17 9,17.2239 9,17.5 C9,17.7761 8.77614,18 8.5,18 L5,18 C3.34315,18 2,16.6569 2,15 L2,11.5 C2,11.2239 2.22386,11 2.5,11 Z M17.5,11 C17.7454222,11 17.9496,11.1769086 17.9919429,11.4101355 L18,11.5 L18,15 C18,16.597725 16.7511226,17.903664 15.1762773,17.9949075 L15,18 L11.5,18 C11.2239,18 11,17.7761 11,17.5 C11,17.2545778 11.1769086,17.0504 11.4101355,17.0080571 L11.5,17 L15,17 C16.0543909,17 16.9181678,16.18415 16.9945144,15.1492661 L17,15 L17,11.5 C17,11.2239 17.2239,11 17.5,11 Z M8.5,2 C8.77614,2 9,2.22386 9,2.5 C9,2.74545778 8.82312296,2.9496079 8.58987501,2.99194425 L8.5,3 L5,3 C3.94563773,3 3.08183483,3.81587733 3.00548573,4.85073759 L3,5 L3,8.5 C3,8.77614 2.77614,9 2.5,9 C2.25454222,9 2.0503921,8.82312296 2.00805575,8.58987501 L2,8.5 L2,5 C2,3.40232321 3.24892392,2.09633941 4.82372764,2.00509271 L5,2 L8.5,2 Z M15,2 C16.597725,2 17.903664,3.24892392 17.9949075,4.82372764 L18,5 L18,8.5 C18,8.77614 17.7761,9 17.5,9 C17.2545778,9 17.0504,8.82312296 17.0080571,8.58987501 L17,8.5 L17,5 C17,3.94563773 16.18415,3.08183483 15.1492661,3.00548573 L15,3 L11.5,3 C11.2239,3 11,2.77614 11,2.5 C11,2.25454222 11.1769086,2.0503921 11.4101355,2.00805575 L11.5,2 L15,2 Z");
    public static readonly PathGeometry CustomRendering = PathGeometry.Parse("M5.75024 2C5.33603 2 5.00024 2.33579 5.00024 2.75V14.2505C5.00024 15.4932 6.0076 16.5005 7.25024 16.5005H9.5V19.5C9.5 20.8807 10.6193 22 12 22C13.3807 22 14.5 20.8807 14.5 19.5V16.5005H16.7502C17.9929 16.5005 19.0002 15.4932 19.0002 14.2505V2.75C19.0002 2.33579 18.6645 2 18.2502 2H5.75024ZM6.50024 11.0003V3.5H12.5V5.25154C12.5 5.66576 12.8358 6.00154 13.25 6.00154C13.6642 6.00154 14 5.66576 14 5.25154V3.5H15V6.25112C15 6.66534 15.3358 7.00112 15.75 7.00112C16.1642 7.00112 16.5 6.66534 16.5 6.25112V3.5H17.5002V11.0003H6.50024ZM6.50024 14.2505V12.5003H17.5002V14.2505C17.5002 14.6647 17.1645 15.0005 16.7502 15.0005H13.75C13.3358 15.0005 13 15.3363 13 15.7505V19.5C13 20.0523 12.5523 20.5 12 20.5C11.4477 20.5 11 20.0523 11 19.5V15.7505C11 15.3363 10.6642 15.0005 10.25 15.0005H7.25024C6.83603 15.0005 6.50024 14.6647 6.50024 14.2505Z");
    public static readonly PathGeometry Menu = PathGeometry.Parse("M8.75 13.5C10.2862 13.5 11.5735 14.5658 11.9126 15.9983L21.25 16C21.6642 16 22 16.3358 22 16.75C22 17.1297 21.7178 17.4435 21.3518 17.4932L21.25 17.5L11.9129 17.5007C11.5741 18.9337 10.2866 20 8.75 20C7.21345 20 5.92594 18.9337 5.58712 17.5007L2.75 17.5C2.33579 17.5 2 17.1642 2 16.75C2 16.3703 2.28215 16.0565 2.64823 16.0068L2.75 16L5.58712 15.9993C5.92594 14.5663 7.21345 13.5 8.75 13.5ZM8.75 15C7.98586 15 7.33611 15.4898 7.09753 16.1725L7.07696 16.2352L7.03847 16.3834C7.01326 16.5016 7 16.6242 7 16.75C7 16.9048 7.02011 17.055 7.05785 17.1979L7.09766 17.3279L7.12335 17.3966C7.38055 18.0431 8.01191 18.5 8.75 18.5C9.51376 18.5 10.1632 18.0107 10.4021 17.3285L10.4422 17.1978L10.4251 17.2581C10.4738 17.0973 10.5 16.9267 10.5 16.75C10.5 16.6452 10.4908 16.5425 10.4731 16.4428L10.4431 16.3057L10.4231 16.2353L10.3763 16.1024C10.1188 15.4565 9.48771 15 8.75 15ZM15.25 4C16.7866 4 18.0741 5.06632 18.4129 6.49934L21.25 6.5C21.6642 6.5 22 6.83579 22 7.25C22 7.6297 21.7178 7.94349 21.3518 7.99315L21.25 8L18.4129 8.00066C18.0741 9.43368 16.7866 10.5 15.25 10.5C13.7134 10.5 12.4259 9.43368 12.0871 8.00066L2.75 8C2.33579 8 2 7.66421 2 7.25C2 6.8703 2.28215 6.55651 2.64823 6.50685L2.75 6.5L12.0874 6.49833C12.4265 5.06582 13.7138 4 15.25 4ZM15.25 5.5C14.4859 5.5 13.8361 5.98976 13.5975 6.6725L13.577 6.73515L13.5385 6.88337C13.5133 7.0016 13.5 7.12425 13.5 7.25C13.5 7.40483 13.5201 7.55497 13.5579 7.69794L13.5977 7.82787L13.6234 7.89664C13.8805 8.54307 14.5119 9 15.25 9C16.0138 9 16.6632 8.51073 16.9021 7.82852L16.9422 7.69781L16.9251 7.75808C16.9738 7.59729 17 7.4267 17 7.25C17 7.14518 16.9908 7.0425 16.9731 6.94275L16.9431 6.80565L16.9231 6.73529L16.8763 6.60236C16.6188 5.95647 15.9877 5.5 15.25 5.5Z");
    public static readonly PathGeometry FileDialog = PathGeometry.Parse("M20 9.50195V8.74985C20 7.50721 18.9926 6.49985 17.75 6.49985H12.0247L9.64368 4.51995C9.23959 4.18393 8.73063 3.99997 8.20509 3.99997H4.24957C3.00724 3.99997 2 5.00686 1.99957 6.24919L1.99561 17.7492C1.99518 18.9921 3.00266 20 4.24561 20H4.27196C4.27607 20 4.28019 20 4.28431 20H18.4693C19.2723 20 19.9723 19.4535 20.167 18.6745L21.9169 11.6765C22.1931 10.5719 21.3577 9.50195 20.2192 9.50195H20ZM4.24957 5.49997H8.20509C8.38027 5.49997 8.54993 5.56129 8.68462 5.6733L11.2741 7.82652C11.4088 7.93852 11.5784 7.99985 11.7536 7.99985H17.75C18.1642 7.99985 18.5 8.33563 18.5 8.74985V9.50195H6.42385C5.39136 9.50195 4.49137 10.2047 4.241 11.2064L3.49684 14.1837L3.49957 6.24971C3.49971 5.8356 3.83546 5.49997 4.24957 5.49997ZM5.69623 11.5701C5.77969 11.2362 6.07969 11.002 6.42385 11.002H20.2192C20.3819 11.002 20.5012 11.1548 20.4617 11.3126L18.7119 18.3107C18.684 18.4219 18.584 18.5 18.4693 18.5H4.28431C4.12167 18.5 4.00233 18.3472 4.04177 18.1894L5.69623 11.5701Z");
    public static readonly PathGeometry ShowDialog = PathGeometry.Parse("M11.2716 7.25C11.2716 6.83579 11.6073 6.5 12.0216 6.5H16.75C17.1642 6.5 17.5 6.83579 17.5 7.25V11.9785C17.5 12.3927 17.1642 12.7285 16.75 12.7285C16.3358 12.7285 16 12.3927 16 11.9785V9.06066L10.7803 14.2803C10.4874 14.5732 10.0126 14.5732 9.71967 14.2803C9.42678 13.9874 9.42678 13.5126 9.71967 13.2197L14.9393 8H12.0216C11.6073 8 11.2716 7.66421 11.2716 7.25Z M6.15675 5.25C6.57853 3.94437 7.80398 3 9.25 3H17.75C19.5449 3 21 4.45507 21 6.25V14.8382C21 16.1902 20.1745 17.3494 19 17.8392V17.9751C19 19.0247 18.4705 19.82 17.6913 20.3191C16.9414 20.7995 15.9737 21.0045 14.9977 20.9999L14.9963 20.9999L10.9191 20.9952L7 20.9951C5.84332 20.9951 4.83555 20.6327 4.11066 19.9496C3.38287 19.2639 3 18.3103 3 17.25V8.5C3 7.63484 3.21573 6.81725 3.73399 6.20358C4.26793 5.57135 5.04354 5.25 5.95588 5.25H6.15675ZM6 6.75H5.95588C5.41235 6.75 5.08501 6.92865 4.87998 7.17142C4.65927 7.43275 4.5 7.86516 4.5 8.5V17.25C4.5 17.9397 4.74213 18.4837 5.13934 18.8579C5.53944 19.2349 6.15669 19.4951 7 19.4951L10.9191 19.4952L15.0009 19.4999L15.0037 19.4999C15.7919 19.5038 16.4486 19.3338 16.8822 19.0561C17.254 18.8179 17.4641 18.5061 17.4958 18.0882H9.25C7.45508 18.0882 6 16.6332 6 14.8382V6.75ZM9.25 4.5C8.2835 4.5 7.5 5.2835 7.5 6.25V14.8382C7.5 15.8047 8.2835 16.5882 9.25 16.5882H17.75C18.7165 16.5882 19.5 15.8047 19.5 14.8382V6.25C19.5 5.2835 18.7165 4.5 17.75 4.5H9.25Z");
}
static class NavigationIcons
{
    public static readonly PathGeometry Buttons = PathGeometry.Parse("M11.75,6 C13.098658,6 13.9287944,6.96910845 13.995631,8.32894133 L14,8.50847912 L14,10.6239658 L16.2191671,11.0269265 C16.3059934,11.0426926 16.3921415,11.0619885 16.4773973,11.0847662 C18.1517032,11.5320892 19.1698052,13.2081266 18.8210232,14.8840108 L18.7783909,15.0635133 L17.7302473,18.9866586 C17.5121898,19.8028361 16.8573252,20.4232391 16.0413431,20.6030664 L15.8760801,20.6330849 L13.4578534,20.9800873 C12.5332557,21.1127621 11.6296196,20.6605333 11.1775403,19.856652 L11.0981723,19.7018603 L11.0690771,19.6393109 C10.834274,19.1345269 10.4818276,18.6948369 10.0427315,18.3563662 L9.84933278,18.2176097 L7.96560087,16.9617884 L7.87170895,16.903392 L7.87170895,16.903392 L7.77431661,16.8510415 L5.41140295,15.6755814 C5.16192093,15.5514735 5.00180973,15.2992831 4.99563339,15.0207048 C4.97105537,13.9121345 5.46115528,13.0567901 6.4145898,12.5800728 C7.11643491,12.2291503 8.04963171,12.2489716 9.24079301,12.5967061 L9.5,12.6762241 L9.5,8.50847912 C9.5,7.05521072 10.3427047,6 11.75,6 Z M11.75,7.5 C11.290032,7.5 11.0376066,7.77493989 11.0038926,8.36636053 L11,8.50847912 L11,13.7525154 C11,14.2865066 10.4577823,14.6494397 9.96410876,14.4458885 C8.50347817,13.843642 7.52268831,13.7030746 7.0854102,13.9217136 C6.83140559,14.0487159 6.66519107,14.2126417 6.57561324,14.4407321 L6.53715909,14.5602504 L8.44240897,15.5080402 L8.62328046,15.6052624 L8.62328046,15.6052624 L8.79765116,15.713713 L10.6813831,16.9695342 C11.3640898,17.424672 11.9221531,18.041075 12.3072464,18.7624149 L12.4291383,19.0066708 L12.4582335,19.0692202 C12.5822556,19.3358453 12.8485386,19.5022497 13.1361683,19.5029349 L13.2447939,19.4952959 L15.6630206,19.1482935 C15.9231839,19.1109615 16.1419003,18.9409604 16.2444544,18.7046345 L16.2810763,18.5994847 L17.32922,14.6763394 C17.5786886,13.7425919 17.0239709,12.7834058 16.0902234,12.5339372 L16.021017,12.5169491 L16.021017,12.5169491 L13.1160046,11.9879766 C12.7949778,11.9296839 12.5528355,11.672241 12.5076173,11.3570515 L12.5,11.2500435 L12.5,8.50847912 C12.5,7.8188652 12.2453502,7.5 11.75,7.5 Z M11.7488353,2.50021005 C14.924699,2.50021005 17.4992452,5.07475626 17.4992452,8.25061996 C17.4992452,8.95339473 17.3731759,9.62672444 17.142419,10.2492275 L16.9982668,10.196565 C16.8536359,10.1457147 16.6487249,10.0802277 16.3908844,10.0209198 C16.1502332,9.96556576 15.9077844,9.92827869 15.663538,9.9090586 C15.8796867,9.3994824 15.9992452,8.83901337 15.9992452,8.25061996 C15.9992452,5.90318338 14.0962719,4.00021005 11.7488353,4.00021005 C9.40139873,4.00021005 7.4984254,5.90318338 7.4984254,8.25061996 C7.4984254,9.2948245 7.87496974,10.2510824 8.49968221,10.9910174 C8.17617954,11.0189235 7.90146959,11.0600799 7.67555237,11.1144867 C7.32454793,11.1990177 7.08752441,11.2842255 6.92334742,11.3768722 C6.33817909,10.47867 5.9984254,9.40432184 5.9984254,8.25061996 C5.9984254,5.07475626 8.57297161,2.50021005 11.7488353,2.50021005 Z");
    public static readonly PathGeometry Inputs = PathGeometry.Parse("M18.25 3C19.7688 3 21 4.23122 21 5.75V18.25C21 19.7688 19.7688 21 18.25 21H5.75C4.23122 21 3 19.7688 3 18.25V5.75C3 4.23122 4.23122 3 5.75 3H18.25ZM18.25 4.5H5.75C5.05964 4.5 4.5 5.05964 4.5 5.75V18.25C4.5 18.9404 5.05964 19.5 5.75 19.5H18.25C18.9404 19.5 19.5 18.9404 19.5 18.25V5.75C19.5 5.05964 18.9404 4.5 18.25 4.5ZM14.25 11.5H6.75L6.64823 11.5068C6.28215 11.5565 6 11.8703 6 12.25C6 12.6642 6.33579 13 6.75 13H14.25L14.3518 12.9932C14.7178 12.9435 15 12.6297 15 12.25C15 11.8358 14.6642 11.5 14.25 11.5ZM6.75 15.5H17.25C17.6642 15.5 18 15.8358 18 16.25C18 16.6297 17.7178 16.9435 17.3518 16.9932L17.25 17H6.75C6.33579 17 6 16.6642 6 16.25C6 15.8703 6.28215 15.5565 6.64823 15.5068L6.75 15.5ZM17.25 7.5H6.75L6.64823 7.50685C6.28215 7.55651 6 7.8703 6 8.25C6 8.66421 6.33579 9 6.75 9H17.25L17.3518 8.99315C17.7178 8.94349 18 8.6297 18 8.25C18 7.83579 17.6642 7.5 17.25 7.5Z");
    public static readonly PathGeometry Selection = PathGeometry.Parse("M8.78394278,15.2232321 C9.04997223,15.4897356 9.0738074,15.9064206 8.85569168,16.1998381 L8.78299868,16.2838919 L5.27985783,19.7808019 C5.04225101,20.0179861 4.6802659,20.0655621 4.39272004,19.9095058 L4.29990115,19.8499258 L2.29990115,18.3494109 C1.96857125,18.1008282 1.90149142,17.6307161 2.15007415,17.2993862 C2.37605845,16.9981772 2.78512485,16.9153567 3.10656598,17.0895516 L3.20009885,17.1495592 L4.68,18.26 L7.72328303,15.222288 C8.01643684,14.9296556 8.49131038,14.9300783 8.78394278,15.2232321 Z M21.2375789,16.9994851 C21.6517925,16.9994851 21.9875789,17.3352715 21.9875789,17.7494851 C21.9875789,18.1291808 21.705425,18.442976 21.3393495,18.4926384 L21.2375789,18.4994851 L10.7375789,18.4994851 C10.3233654,18.4994851 9.98757893,18.1636986 9.98757893,17.7494851 C9.98757893,17.3697893 10.2697328,17.0559941 10.6358084,17.0063317 L10.7375789,16.9994851 L21.2375789,16.9994851 Z M21.25,11 C21.6642136,11 22,11.3357864 22,11.75 C22,12.1296958 21.7178461,12.443491 21.3517706,12.4931534 L21.25,12.5 L10.75,12.5 C10.3357864,12.5 10,12.1642136 10,11.75 C10,11.3703042 10.2821539,11.056509 10.6482294,11.0068466 L10.75,11 L21.25,11 Z M8.78469742,3.22398916 C9.05034677,3.49087151 9.07358803,3.90759013 8.85505434,4.20069642 L8.78224162,4.28464649 L5.26910076,7.78155657 C5.03103758,8.0185199 4.66876476,8.06549516 4.38139729,7.9087877 L4.28865553,7.84898929 L2.29865553,6.34950423 C1.96784288,6.10023356 1.90174004,5.62998312 2.15101071,5.29917047 C2.37762041,4.9984317 2.78685802,4.91646136 3.10793649,5.09132385 L3.20134447,5.15152565 L4.671,6.259 L7.72404009,3.22153336 C8.01761068,2.92931908 8.49248314,2.93041858 8.78469742,3.22398916 Z M21.2375789,5.00051494 C21.6517925,5.00051494 21.9875789,5.33630138 21.9875789,5.75051494 C21.9875789,6.13021071 21.705425,6.4440059 21.3393495,6.49366832 L21.2375789,6.50051494 L10.7657573,6.50051494 C10.3515438,6.50051494 10.0157573,6.1647285 10.0157573,5.75051494 C10.0157573,5.37081917 10.2979112,5.05702398 10.6639868,5.00736156 L10.7657573,5.00051494 L21.2375789,5.00051494 Z");
    public static readonly PathGeometry Typography = PathGeometry.Parse("M14.5033,5.99999992 C14.8108,5.99999992 15.0871,6.18799 15.2003,6.47391 L20.7552,20.5042 L21.2492,20.5042 C21.6634,20.5042 21.9992,20.84 21.9992,21.2542 C21.9992,21.6684 21.6634,22.0043 21.2492,22.0043 L18.75,22.0041 C18.3358,22.0041 18,21.6683 18,21.2541 C18,20.8399 18.3358,20.5041 18.75,20.5041 L19.1419,20.5041 L17.9525,17.4999999 L11.0426,17.4999999 L9.85007,20.5042 L10.2492,20.5042 C10.6634,20.5042 10.9992,20.84 10.9992,21.2542 C10.9992,21.6684 10.6634,22.0043 10.2492,22.0043 L7.74998,22.0041 C7.33577,22.0041 7,21.6683 7,21.2541 C7,20.8399 7.3358,20.5041 7.75002,20.5041 L8.23622,20.5041 L13.8059,6.47329 C13.9193,6.18746 14.1958,5.99999992 14.5033,5.99999992 Z M14.5021,8.78508 L11.638,16 L17.3586,16 L14.5021,8.78508 Z M7.00005,2 C7.31395,2 7.59464,2.19551 7.70349,2.48994 L10.8898,11.109 L10.0618,13.195 L9.43546,11.5008 L4.5638,11.5008 L3.45103,14.5101 C3.30737,14.8986 2.87596,15.0971 2.48746,14.9534 C2.09896,14.8098 1.90047,14.3784 2.04413,13.9899 L6.29657,2.48988 C6.40544,2.19546 6.68615,2 7.00005,2 Z M6.99993,4.91271 L5.11847,10.0008 L8.88093,10.0008 L6.99993,4.91271 Z");
    public static readonly PathGeometry Lists = PathGeometry.Parse("M16.25,21 C16.664,21 17,21.336 17,21.75 C17,22.164 16.664,22.5 16.25,22.5 L3.75,22.5 C3.336,22.5 3,22.164 3,21.75 C3,21.336 3.336,21 3.75,21 L16.25,21 Z M24.2484387,13.4983804 C24.6624387,13.4983804 24.9984387,13.8343804 24.9984387,14.2483804 C24.9984387,14.6623804 24.6624387,14.9983804 24.2484387,14.9983804 L3.74843873,14.9983804 C3.33443873,14.9983804 2.99843873,14.6623804 2.99843873,14.2483804 C2.99843873,13.8343804 3.33443873,13.4983804 3.74843873,13.4983804 L24.2484387,13.4983804 Z M20.25,6 C20.664,6 21,6.336 21,6.75 C21,7.164 20.664,7.5 20.25,7.5 L3.75,7.5 C3.336,7.5 3,7.164 3,6.75 C3,6.336 3.336,6 3.75,6 L20.25,6 Z");
    public static readonly PathGeometry GridView = PathGeometry.Parse("M10.75,15 C11.9926407,15 13,16.0073593 13,17.25 L13,22.75 C13,23.9926407 11.9926407,25 10.75,25 L5.25,25 C4.00735931,25 3,23.9926407 3,22.75 L3,17.25 C3,16.0073593 4.00735931,15 5.25,15 L10.75,15 Z M22.75,15 C23.9926407,15 25,16.0073593 25,17.25 L25,22.75 C25,23.9926407 23.9926407,25 22.75,25 L17.25,25 C16.0073593,25 15,23.9926407 15,22.75 L15,17.25 C15,16.0073593 16.0073593,15 17.25,15 L22.75,15 Z M10.75,16.5 L5.25,16.5 C4.83578644,16.5 4.5,16.8357864 4.5,17.25 L4.5,22.75 C4.5,23.1642136 4.83578644,23.5 5.25,23.5 L10.75,23.5 C11.1642136,23.5 11.5,23.1642136 11.5,22.75 L11.5,17.25 C11.5,16.8357864 11.1642136,16.5 10.75,16.5 Z M22.75,16.5 L17.25,16.5 C16.8357864,16.5 16.5,16.8357864 16.5,17.25 L16.5,22.75 C16.5,23.1642136 16.8357864,23.5 17.25,23.5 L22.75,23.5 C23.1642136,23.5 23.5,23.1642136 23.5,22.75 L23.5,17.25 C23.5,16.8357864 23.1642136,16.5 22.75,16.5 Z M10.75,3 C11.9926407,3 13,4.00735931 13,5.25 L13,10.75 C13,11.9926407 11.9926407,13 10.75,13 L5.25,13 C4.00735931,13 3,11.9926407 3,10.75 L3,5.25 C3,4.00735931 4.00735931,3 5.25,3 L10.75,3 Z M22.75,3 C23.9926407,3 25,4.00735931 25,5.25 L25,10.75 C25,11.9926407 23.9926407,13 22.75,13 L17.25,13 C16.0073593,13 15,11.9926407 15,10.75 L15,5.25 C15,4.00735931 16.0073593,3 17.25,3 L22.75,3 Z M10.75,4.5 L5.25,4.5 C4.83578644,4.5 4.5,4.83578644 4.5,5.25 L4.5,10.75 C4.5,11.1642136 4.83578644,11.5 5.25,11.5 L10.75,11.5 C11.1642136,11.5 11.5,11.1642136 11.5,10.75 L11.5,5.25 C11.5,4.83578644 11.1642136,4.5 10.75,4.5 Z M22.75,4.5 L17.25,4.5 C16.8357864,4.5 16.5,4.83578644 16.5,5.25 L16.5,10.75 C16.5,11.1642136 16.8357864,11.5 17.25,11.5 L22.75,11.5 C23.1642136,11.5 23.5,11.1642136 23.5,10.75 L23.5,5.25 C23.5,4.83578644 23.1642136,4.5 22.75,4.5 Z");
    public static readonly PathGeometry Panels = PathGeometry.Parse("M4.79354404,9.9967648 L9.49194198,9.9967648 C9.90615554,9.9967648 10.241942,10.3325512 10.241942,10.7467648 C10.241942,11.1264606 9.9597881,11.4402558 9.59371254,11.4899182 L9.49194198,11.4967648 L4.79354404,11.4967648 C4.12186769,11.4967648 3.59079463,11.9463924 3.52702849,12.4980996 L3.52060282,12.6096991 L3.50000697,17.3862243 C3.50000697,17.9469657 3.98909414,18.4316397 4.64057057,18.4900335 L4.77293425,18.4959248 L19.2270658,18.4959248 C19.8987421,18.4959248 20.4298213,18.0462972 20.4935825,17.49459 L20.500007,17.3829904 L20.5206098,12.6064652 C20.5206098,12.0457238 20.0315165,11.5610499 19.3800393,11.5026561 L19.2476755,11.4967648 L14.5527817,11.4967648 C14.1385682,11.4967648 13.8027817,11.1609784 13.8027817,10.7467648 C13.8027817,10.367069 14.0849356,10.0532738 14.4510112,10.0036114 L14.5527817,9.9967648 L19.2476755,9.9967648 C20.707307,9.9967648 21.923682,11.0633848 22.0150901,12.4427255 L22.0206028,12.6096991 L22.000007,17.3862243 C22.000007,18.7862668 20.8397587,19.9067347 19.4009975,19.9908534 L19.2270658,19.9959248 L4.77293425,19.9959248 C3.3133028,19.9959248 2.09692776,18.9293047 2.00551967,17.5499641 L2.00000697,17.3829904 L2.02060979,12.6064652 C2.02060979,11.2064227 3.1808516,10.0859549 4.61961229,10.0018361 L4.79354404,9.9967648 L9.49194198,9.9967648 L4.79354404,9.9967648 Z M12.4462117,3.14705176 L12.5303301,3.21966991 L16.4553927,7.14473252 C16.7482859,7.43762574 16.7482859,7.91249947 16.4553927,8.20539269 C16.1891261,8.47165926 15.7724624,8.49586531 15.478851,8.27801085 L15.3947325,8.20539269 L12.7383007,5.54580816 L12.7397098,15.2538857 C12.7397098,15.6680993 12.4039234,16.0038857 11.9897098,16.0038857 C11.5754962,16.0038857 11.2397098,15.6680993 11.2397098,15.2538857 L11.2409374,5.569 L8.60526748,8.20539269 C8.33900092,8.47165926 7.92233723,8.49586531 7.62872574,8.27801085 L7.54460731,8.20539269 C7.27834074,7.93912613 7.25413469,7.52246245 7.47198915,7.22885095 L7.54460731,7.14473252 L11.4696699,3.21966991 C11.7359365,2.95340335 12.1526002,2.9291973 12.4462117,3.14705176 Z");
    public static readonly PathGeometry Layout = PathGeometry.Parse("M9.28168207,8 C10.2481804,8 11.0316821,8.78350169 11.0316821,9.75 L11.0316821,14.25 C11.0316821,15.2164983 10.2481804,16 9.28168207,16 L3.75,16 C2.78350169,16 2,15.2164983 2,14.25 L2,9.75 C2,8.8318266 2.70711027,8.07880766 3.60647279,8.0058012 L3.75,8 L9.28168207,8 Z M20.25,8 C21.2164983,8 22,8.78350169 22,9.75 L22,14.25 C22,15.2164983 21.2164983,16 20.25,16 L14.7183179,16 C13.7518196,16 12.9683179,15.2164983 12.9683179,14.25 L12.9683179,9.75 C12.9683179,8.78350169 13.7518196,8 14.7183179,8 L20.25,8 Z M9.28168207,9.5 L3.75,9.5 L3.69267729,9.50660268 C3.58223341,9.53251318 3.5,9.63165327 3.5,9.75 L3.5,14.25 C3.5,14.3880712 3.61192881,14.5 3.75,14.5 L9.28168207,14.5 C9.41975326,14.5 9.53168207,14.3880712 9.53168207,14.25 L9.53168207,9.75 C9.53168207,9.61192881 9.41975326,9.5 9.28168207,9.5 Z M20.25,9.5 L14.7183179,9.5 C14.5802467,9.5 14.4683179,9.61192881 14.4683179,9.75 L14.4683179,14.25 C14.4683179,14.3880712 14.5802467,14.5 14.7183179,14.5 L20.25,14.5 C20.3880712,14.5 20.5,14.3880712 20.5,14.25 L20.5,9.75 C20.5,9.61192881 20.3880712,9.5 20.25,9.5 Z");
    public static readonly PathGeometry Shapes = PathGeometry.Parse("M18.75,9 C20.4830069,9 21.8992442,10.3564785 21.9948551,12.0655785 L22,12.25 L22,18.75 C22,20.4830069 20.6435215,21.8992442 18.9344215,21.9948551 L18.75,22 L12.25,22 C10.5169931,22 9.10075577,20.6435215 9.00514488,18.9344215 L9,18.75 L9,12.25 C9,10.5169931 10.3564785,9.10075577 12.0655785,9.00514488 L12.25,9 L18.75,9 Z M18.75,10.5 L12.25,10.5 C11.331825,10.5 10.5788075,11.2071088 10.5058012,12.1064726 L10.5,12.25 L10.5,18.75 C10.5,19.668175 11.2071087,20.4211925 12.1064726,20.4941988 L12.25,20.5 L18.75,20.5 C19.668175,20.5 20.4211925,19.7928913 20.4941988,18.8935274 L20.5,18.75 L20.5,12.25 C20.5,11.331825 19.7928912,10.5788075 18.8935274,10.5058012 L18.75,10.5 Z M8.75,2 C12.2244,2 15.0857,4.62504 15.4588,8 L13.9468,8 C13.5829,5.45578 11.3949,3.5 8.75,3.5 C5.85051,3.5 3.5,5.85051 3.5,8.75 C3.5,11.3949 5.45578,13.5829 8,13.9468 L8,15.4588 C4.62504,15.0857 2,12.2244 2,8.75 C2,5.02208 5.02208,2 8.75,2 Z");
    public static readonly PathGeometry Media = PathGeometry.Parse("M22.9932158,6.00782415 C23.8987635,6.58284797 24.5,7.59621083 24.5,8.75 L24.5,19.25 C24.5,22.1494949 22.1494949,24.5 19.25,24.5 L8.75,24.5 C7.59621083,24.5 6.58284797,23.8987635 6.00612306,22.9925021 L6.12827706,22.9982072 L6.25,23 L19.25,23 C21.3210678,23 23,21.3210678 23,19.25 L23,6.25 C23,6.16872164 22.9977184,6.08797617 22.9932158,6.00782415 Z M18.75,3 C20.5449254,3 22,4.45507456 22,6.25 L22,18.75 C22,20.5449254 20.5449254,22 18.75,22 L6.25,22 C4.45507456,22 3,20.5449254 3,18.75 L3,6.25 C3,4.45507456 4.45507456,3 6.25,3 L18.75,3 Z M19.331549,20.4010512 L13.024576,14.2231154 C12.7595029,13.963499 12.3499409,13.9398975 12.0585971,14.1523109 L11.9750032,14.2231154 L5.66845098,20.4010512 C5.85040089,20.4651384 6.04612926,20.5 6.25,20.5 L18.75,20.5 C18.9538707,20.5 19.1495991,20.4651384 19.331549,20.4010512 L13.024576,14.2231154 L19.331549,20.4010512 Z M18.75,4.5 L6.25,4.5 C5.28350169,4.5 4.5,5.28350169 4.5,6.25 L4.5,18.75 C4.5,18.9580237 4.53629637,19.1575698 4.60290153,19.342651 L10.9254305,13.1514825 C11.7585174,12.3355452 13.0673362,12.296691 13.9457309,13.03492 L14.0741487,13.1514825 L20.3967355,19.3436585 C20.4635718,19.1582941 20.5,18.9584012 20.5,18.75 L20.5,6.25 C20.5,5.28350169 19.7164983,4.5 18.75,4.5 Z M16.0004478,7.75115873 C16.6904111,7.75115873 17.2497368,8.3104845 17.2497368,9.00044779 C17.2497368,9.69041108 16.6904111,10.2497368 16.0004478,10.2497368 C15.3104845,10.2497368 14.7511587,9.69041108 14.7511587,9.00044779 C14.7511587,8.3104845 15.3104845,7.75115873 16.0004478,7.75115873 Z");
    public static readonly PathGeometry Icons = PathGeometry.Parse("M13 3.5C11.067 3.5 9.5 5.067 9.5 7C9.5 7.86428 9.8123 8.65369 10.3311 9.26444C10.5204 9.48721 10.5629 9.79962 10.4402 10.0649C10.3175 10.3302 10.0518 10.5 9.75952 10.5H3.75C3.61193 10.5 3.5 10.6119 3.5 10.75V11.75C3.5 14.6495 5.8505 17 8.75 17H11.9682C12.1177 17.1091 12.2869 17.196 12.4723 17.2545C12.1892 17.6624 11.9467 18.0803 11.7448 18.5H8.75C5.02208 18.5 2 15.4779 2 11.75V10.75C2 9.7835 2.7835 9 3.75 9H8.41626C8.1486 8.38736 8 7.71071 8 7C8 4.23858 10.2386 2 13 2C15.0503 2 16.8124 3.2341 17.584 5H19.25C20.2165 5 21 5.7835 21 6.75C21 8.26878 19.7688 9.5 18.25 9.5H17.3309C17.1948 9.7353 17.0401 9.95846 16.8689 10.1674C17.0257 10.3041 17.1735 10.4509 17.3113 10.6069C17.1351 10.6259 16.9548 10.6475 16.7702 10.6719C16.3015 10.734 15.8604 10.8387 15.4476 10.978C15.4151 10.958 15.3821 10.9385 15.3488 10.9197C15.1345 10.7983 14.9935 10.5794 14.9714 10.3342C14.9494 10.0889 15.0492 9.84841 15.2384 9.69079C15.7624 9.2543 16.1562 8.66903 16.3552 8H18.25C18.9404 8 19.5 7.44036 19.5 6.75C19.5 6.61193 19.3881 6.5 19.25 6.5H16.4646C16.2219 4.80385 14.7632 3.5 13 3.5Z M16.9015 11.6634C19.7024 11.2925 21.4398 11.6025 22.4644 11.8905C22.7467 11.9699 22.9569 12.2065 23.0024 12.4962C23.0479 12.7859 22.9203 13.0756 22.6759 13.2376C22.6186 13.2757 22.5268 13.3675 22.4157 13.5778C22.3059 13.7855 22.2028 14.0589 22.0963 14.4028C22.0082 14.6872 21.9253 14.9939 21.835 15.3281L21.7777 15.5399C21.6664 15.9497 21.5435 16.3898 21.3957 16.826C21.1036 17.6883 20.6917 18.6078 19.999 19.3139C19.2798 20.0471 18.3037 20.5037 16.9999 20.5037C15.671 20.5037 14.7425 19.9653 14.1496 19.3454C13.746 20.0852 13.5356 20.7821 13.5042 21.2963C13.479 21.7097 13.1234 22.0244 12.71 21.9992C12.2965 21.974 11.9818 21.6184 12.007 21.205C12.0765 20.0655 12.6715 18.6313 13.669 17.3613C14.678 16.0767 16.1497 14.8938 18.05 14.3212C18.4466 14.2017 18.865 14.4263 18.9845 14.8229C19.104 15.2195 18.8794 15.6379 18.4828 15.7574C17.0355 16.1935 15.8731 17.0688 15.0307 18.0646C15.3417 18.5067 15.9532 19.0037 16.9999 19.0037C17.9113 19.0037 18.4983 18.7017 18.9282 18.2635C19.3846 17.7983 19.7078 17.1336 19.9751 16.3447C20.107 15.9552 20.2198 15.553 20.3301 15.1468L20.3845 14.9457C20.4749 14.6111 20.5664 14.2721 20.6635 13.9589C20.7537 13.6677 20.8553 13.376 20.9775 13.1062C20.0845 12.9754 18.8272 12.9215 17.0984 13.1504C15.0995 13.4151 13.9738 14.8731 13.7231 15.7848C13.6133 16.1842 13.2005 16.419 12.8011 16.3092C12.4017 16.1994 12.167 15.7866 12.2768 15.3872C12.6734 13.9447 14.255 12.0138 16.9015 11.6634Z");
    public static readonly PathGeometry Transitions = PathGeometry.Parse("M12,2 C17.5228,2 22,6.47715 22,12 C22,17.5228 17.5228,22 12,22 C6.47715,22 2,17.5228 2,12 C2,6.47715 6.47715,2 12,2 Z M12,3.5 C7.30558,3.5 3.5,7.30558 3.5,12 C3.5,16.6944 7.30558,20.5 12,20.5 C16.6944,20.5 20.5,16.6944 20.5,12 C20.5,7.30558 16.6944,3.5 12,3.5 Z M16.75,12 C17.1296833,12 17.4434889,12.2821653 17.4931531,12.6482323 L17.5,12.75 L17.5,15.75 C17.5,16.1642 17.1642,16.5 16.75,16.5 C16.3703167,16.5 16.0565111,16.2178347 16.0068469,15.8517677 L16,15.75 L16,15 C15.0881,16.2143 13.6362,17 11.9999,17 C10.4748,17 9.09587,16.316 8.17857,15.237 C7.91028,14.9214 7.94862,14.4481 8.2642,14.1798 C8.57979,13.9115 9.05311,13.9499 9.3214,14.2655 C9.96322,15.0204 10.9293,15.5 11.9999,15.5 C13.32553,15.5 14.4803167,14.7625672 15.0742404,13.6746351 L15.1633,13.5 L14,13.5 C13.5858,13.5 13.25,13.1642 13.25,12.75 C13.25,12.3703167 13.5321653,12.0565111 13.8982323,12.0068469 L14,12 L16.75,12 Z M11.9999,7 C13.5368,7 14.9041,7.66036 15.8268,8.77062 C16.0915,9.08918 16.0479,9.56205 15.7294,9.8268 C15.4108,10.0916 14.9379,10.0479 14.6732,9.72938 C14.0368,8.96361 13.093,8.5 11.9999,8.5 C10.5754318,8.5 9.34895806,9.35140335 8.80281957,10.5730172 L8.72948,10.75 L10,10.75 C10.4142,10.75 10.75,11.0858 10.75,11.5 C10.75,11.8796833 10.4678347,12.1934889 10.1017677,12.2431531 L10,12.25 L7.25,12.25 C6.8703075,12.25 6.55650958,11.9678347 6.50684668,11.6017677 L6.5,11.5 L6.5,8.25 C6.5,7.83579 6.83579,7.5 7.25,7.5 C7.6296925,7.5 7.94349042,7.78215688 7.99315332,8.14823019 L8,8.25 L8,8.99955 C8.9121,7.78531 10.364,7 11.9999,7 Z");
    public static readonly PathGeometry WindowMenu = PathGeometry.Parse("M2.99707 5.5C2.99707 4.11929 4.11636 3 5.49707 3H14.4971C15.8778 3 16.9971 4.11929 16.9971 5.5V6H17V7H16.9971V14.5C16.9971 15.8807 15.8778 17 14.4971 17H5.49707C4.11636 17 2.99707 15.8807 2.99707 14.5V5.5ZM15.9971 6V5.5C15.9971 4.67157 15.3255 4 14.4971 4H5.49707C4.66864 4 3.99707 4.67157 3.99707 5.5V6H15.9971ZM3.99707 7V14.5C3.99707 15.3284 4.66864 16 5.49707 16H14.4971C15.3255 16 15.9971 15.3284 15.9971 14.5V7H3.99707Z");
    public static readonly PathGeometry MessageBox = PathGeometry.Parse("M12,1.99622391 C16.0499218,1.99622391 19.3566662,5.19096617 19.4958079,9.24527692 L19.5,9.49622391 L19.5,13.5931945 L20.8800025,16.7492056 C20.949058,16.9071328 20.9847056,17.0776351 20.9847056,17.25 C20.9847056,17.9403559 20.4250615,18.5 19.7347056,18.5 L15,18.5014962 C15,20.1583504 13.6568542,21.5014962 12,21.5014962 C10.4023191,21.5014962 9.09633912,20.2525762 9.00509269,18.6777689 L8.99954674,18.4992239 L4.27486429,18.5 C4.10352557,18.5 3.93401618,18.4647755 3.7768624,18.3965139 C3.14366026,18.121475 2.85331154,17.3852002 3.1283504,16.7519981 L4.5,13.594148 L4.50000001,9.4961162 C4.50059668,5.34132493 7.85208744,1.99622391 12,1.99622391 Z M13.4995467,18.4992239 L10.5,18.5014962 C10.5,19.3299233 11.1715729,20.0014962 12,20.0014962 C12.7796961,20.0014962 13.4204487,19.4066081 13.4931334,18.6459562 L13.4995467,18.4992239 Z M12,3.49622391 C8.67983848,3.49622391 6.00047762,6.17047646 6,9.49622391 L6,13.905852 L4.65602014,17 L19.3525351,17 L18,13.9068055 L18.0001102,9.5090803 L17.9963601,9.28387824 C17.8853006,6.05040449 15.2415749,3.49622391 12,3.49622391 Z M21,8.25 L23,8.25 C23.4142136,8.25 23.75,8.58578644 23.75,9 C23.75,9.37969577 23.4678461,9.69349096 23.1017706,9.74315338 L23,9.75 L21,9.75 C20.5857864,9.75 20.25,9.41421356 20.25,9 C20.25,8.62030423 20.5321539,8.30650904 20.8982294,8.25684662 L21,8.25 Z M1,8.25 L3,8.25 C3.41421356,8.25 3.75,8.58578644 3.75,9 C3.75,9.37969577 3.46784612,9.69349096 3.10177056,9.74315338 L3,9.75 L1,9.75 C0.585786438,9.75 0.25,9.41421356 0.25,9 C0.25,8.62030423 0.532153882,8.30650904 0.898229443,8.25684662 L1,8.25 Z M22.6,2.55 C22.8259347,2.85124623 22.7909723,3.26714548 22.5337844,3.52699676 L22.45,3.6 L20.45,5.1 C20.1186292,5.34852814 19.6485281,5.28137085 19.4,4.95 C19.1740653,4.64875377 19.2090277,4.23285452 19.4662156,3.97300324 L19.55,3.9 L21.55,2.4 C21.8813708,2.15147186 22.3514719,2.21862915 22.6,2.55 Z M2.45,2.4 L4.45,3.9 C4.78137085,4.14852814 4.84852814,4.61862915 4.6,4.95 C4.35147186,5.28137085 3.88137085,5.34852814 3.55,5.1 L1.55,3.6 C1.21862915,3.35147186 1.15147186,2.88137085 1.4,2.55 C1.64852814,2.21862915 2.11862915,2.15147186 2.45,2.4 Z");
    public static readonly PathGeometry Overlay = PathGeometry.Parse("M20.0256266,12.1919251 C19.8772338,12.4293536 19.6806426,12.6329794 19.4485757,12.7896246 L13.3986821,16.8733027 C12.5534904,17.4438072 11.4465096,17.4438072 10.6013179,16.8733027 L4.55142428,12.7896246 C3.79043588,12.2759574 3.49533538,11.3303569 3.77229147,10.5 L10.6132495,15.0595795 C11.4005138,15.5844224 12.4112447,15.617225 13.2264422,15.1579876 L13.3867505,15.0595795 L20.2270621,10.4994959 C20.4087649,11.0456562 20.3545192,11.665697 20.0256266,12.1919251 Z M20.2270621,13.7494959 C20.4087649,14.2956562 20.3545192,14.915697 20.0256266,15.4419251 C19.8772338,15.6793536 19.6806426,15.8829794 19.4485757,16.0396246 L13.3986821,20.1233027 C12.5534904,20.6938072 11.4465096,20.6938072 10.6013179,20.1233027 L4.55142428,16.0396246 C3.79043588,15.5259574 3.49533538,14.5803569 3.77229147,13.75 L10.6132495,18.3095795 C11.4005138,18.8344224 12.4112447,18.867225 13.2264422,18.4079876 L13.3867505,18.3095795 L20.2270621,13.7494959 Z M13.3867505,3.42450033 L19.7519246,7.66794971 C20.2114532,7.97430216 20.3356271,8.59517151 20.0292747,9.0547002 C19.9560398,9.16455248 19.8617768,9.25881544 19.7519246,9.33205029 L13.3867505,13.5754997 C12.547002,14.135332 11.452998,14.135332 10.6132495,13.5754997 L4.24807544,9.33205029 C3.78854675,9.02569784 3.66437288,8.40482849 3.97072534,7.9452998 C4.0439602,7.83544752 4.13822315,7.74118456 4.24807544,7.66794971 L10.6132495,3.42450033 C11.452998,2.86466797 12.547002,2.86466797 13.3867505,3.42450033 Z M11.560754,4.60622527 L11.4452998,4.67257577 L5.705,8.5 L11.4452998,12.3274242 C11.7438771,12.5264757 12.1228116,12.5485926 12.439246,12.3937747 L12.5547002,12.3274242 L18.294,8.5 L12.5547002,4.67257577 C12.2561229,4.47352427 11.8771884,4.45140743 11.560754,4.60622527 Z");
    public static readonly PathGeometry Settings = PathGeometry.Parse("M14 9.50006C11.5147 9.50006 9.5 11.5148 9.5 14.0001C9.5 16.4853 11.5147 18.5001 14 18.5001C15.3488 18.5001 16.559 17.9066 17.3838 16.9666C18.0787 16.1746 18.5 15.1365 18.5 14.0001C18.5 13.5401 18.431 13.0963 18.3028 12.6784C17.7382 10.8381 16.0253 9.50006 14 9.50006ZM11 14.0001C11 12.3432 12.3431 11.0001 14 11.0001C15.6569 11.0001 17 12.3432 17 14.0001C17 15.6569 15.6569 17.0001 14 17.0001C12.3431 17.0001 11 15.6569 11 14.0001Z M21.7093 22.3948L19.9818 21.6364C19.4876 21.4197 18.9071 21.4515 18.44 21.7219C17.9729 21.9924 17.675 22.4693 17.6157 23.0066L17.408 24.8855C17.3651 25.273 17.084 25.5917 16.7055 25.682C14.9263 26.1061 13.0725 26.1061 11.2933 25.682C10.9148 25.5917 10.6336 25.273 10.5908 24.8855L10.3834 23.0093C10.3225 22.4731 10.0112 21.9976 9.54452 21.7281C9.07783 21.4586 8.51117 21.4269 8.01859 21.6424L6.29071 22.4009C5.93281 22.558 5.51493 22.4718 5.24806 22.1859C4.00474 20.8536 3.07924 19.2561 2.54122 17.5137C2.42533 17.1384 2.55922 16.7307 2.8749 16.4977L4.40219 15.3703C4.83721 15.0501 5.09414 14.5415 5.09414 14.0007C5.09414 13.4598 4.83721 12.9512 4.40162 12.6306L2.87529 11.5051C2.55914 11.272 2.42513 10.8638 2.54142 10.4882C3.08038 8.74734 4.00637 7.15163 5.24971 5.82114C5.51684 5.53528 5.93492 5.44941 6.29276 5.60691L8.01296 6.36404C8.50793 6.58168 9.07696 6.54881 9.54617 6.27415C10.0133 6.00264 10.3244 5.52527 10.3844 4.98794L10.5933 3.11017C10.637 2.71803 10.9245 2.39704 11.3089 2.31138C12.19 2.11504 13.0891 2.01071 14.0131 2.00006C14.9147 2.01047 15.8128 2.11485 16.6928 2.31149C17.077 2.39734 17.3643 2.71823 17.4079 3.11017L17.617 4.98937C17.7116 5.85221 18.4387 6.50572 19.3055 6.50663C19.5385 6.507 19.769 6.45838 19.9843 6.36294L21.7048 5.60568C22.0626 5.44818 22.4807 5.53405 22.7478 5.81991C23.9912 7.1504 24.9172 8.74611 25.4561 10.487C25.5723 10.8623 25.4386 11.2703 25.1228 11.5035L23.5978 12.6297C23.1628 12.95 22.9 13.4586 22.9 13.9994C22.9 14.5403 23.1628 15.0489 23.5988 15.3698L25.1251 16.4965C25.441 16.7296 25.5748 17.1376 25.4586 17.5131C24.9198 19.2536 23.9944 20.8492 22.7517 22.1799C22.4849 22.4657 22.0671 22.5518 21.7093 22.3948ZM16.263 22.1966C16.4982 21.4685 16.9889 20.8288 17.6884 20.4238C18.5702 19.9132 19.6536 19.8547 20.5841 20.2627L21.9281 20.8526C22.791 19.8538 23.4593 18.7013 23.8981 17.4552L22.7095 16.5778L22.7086 16.5771C21.898 15.98 21.4 15.0277 21.4 13.9994C21.4 12.9719 21.8974 12.0195 22.7073 11.4227L22.7085 11.4218L23.8957 10.545C23.4567 9.2988 22.7881 8.14636 21.9248 7.1477L20.5922 7.73425L20.5899 7.73527C20.1844 7.91463 19.7472 8.00722 19.3039 8.00663C17.6715 8.00453 16.3046 6.77431 16.1261 5.15465L16.1259 5.15291L15.9635 3.69304C15.3202 3.57328 14.6677 3.50872 14.013 3.50017C13.3389 3.50891 12.6821 3.57367 12.0377 3.69328L11.8751 5.15452C11.7625 6.16272 11.1793 7.05909 10.3019 7.56986C9.41937 8.0856 8.34453 8.14844 7.40869 7.73694L6.07273 7.14893C5.20949 8.14751 4.54092 9.29983 4.10196 10.5459L5.29181 11.4233C6.11115 12.0269 6.59414 12.9837 6.59414 14.0007C6.59414 15.0173 6.11142 15.9742 5.29237 16.5776L4.10161 17.4566C4.54002 18.7044 5.2085 19.8585 6.07205 20.8587L7.41742 20.2682C8.34745 19.8613 9.41573 19.9215 10.2947 20.4292C11.174 20.937 11.7593 21.832 11.8738 22.84L11.8744 22.8445L12.0362 24.3088C13.3326 24.5638 14.6662 24.5638 15.9626 24.3088L16.1247 22.8418C16.1491 22.6217 16.1955 22.4055 16.263 22.1966Z");
}

sealed record NavEntry(NavigationItemKind Kind, string Title, PathGeometry? Icon, Func<FrameworkElement>? Page);
sealed record SegmentItem(string Icon, string Label);
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

sealed class TemplateComplexPersonRow
{
    public TemplateComplexPersonRow(string name, int roleIndex, bool isOnline, double progress, double score)
    {
        Name = new ObservableValue<string>(name);
        RoleIndex = new ObservableValue<int>(roleIndex, value => Math.Clamp(value, 0, 2));
        IsOnline = new ObservableValue<bool>(isOnline);
        IsSelected = new ObservableValue<bool>(false);
        Progress = new ObservableValue<double>(progress,
            value => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0);
        Score = new ObservableValue<double>(score,
            value => double.IsFinite(value) ? Math.Clamp(value, 0, 100) : 0);
    }
    public ObservableValue<bool> IsSelected { get; }
    public ObservableValue<string> Name { get; }
    public ObservableValue<int> RoleIndex { get; }
    public ObservableValue<bool> IsOnline { get; }
    public ObservableValue<double> Progress { get; }
    public ObservableValue<double> Score { get; }
}

sealed class VariableHeightGridRow(int id, string author, string note)
{
    public int Id { get; } = id;
    public string Author { get; } = author;
    public string Note { get; } = note;
}

sealed class LazyTreeNode(string name, int depth, bool canLoadChildren)
{
    public string Name { get; } = name;
    public int Depth { get; } = depth;
    public bool CanLoadChildren { get; } = canLoadChildren;
    public ObservableCollection<LazyTreeNode> Children { get; } = [];
    public ObservableValue<bool> IsLoading { get; } = new(false);
    public bool IsLoaded { get; set; }
    public CancellationTokenSource? LoadCancellation { get; set; }
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
        StyleSheet.Define("chrome", () => ChromeButtonStyle);
        StyleSheet.Define("close", () => CloseButtonStyle);

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
            if (e.Button == MouseButton.Left
                && CanMaximize
                && !IsInTitleBarSideArea(e.GetPosition(_titleBar)))
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

    bool IsInTitleBarSideArea(Point pointInTitleBar)
    {
        return GetBoundsInTitleBar(_leftArea).Contains(pointInTitleBar)
            || GetBoundsInTitleBar(_rightArea).Contains(pointInTitleBar);
    }

    Rect GetBoundsInTitleBar(FrameworkElement element)
    {
        if (element.Bounds.Width <= 0 || element.Bounds.Height <= 0)
        {
            return Rect.Empty;
        }

        return element.TranslateRect(new Rect(0, 0, element.Bounds.Width, element.Bounds.Height), _titleBar);
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
    public void Clear() { _isRaining = false; _cq.Clear(); _cannonAcc = 0; _p.Clear(); StopTimer(); InvalidateVisual(); }

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
