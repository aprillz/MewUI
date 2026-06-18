using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Svg;
using Aprillz.MewUI.Svg.Sample.Controls;

using Svg;

Startup();

Window window = null!;
ListBox fileList = null!;
MultiLineTextBox editor = null!;
ScrollViewer vectorScrollViewer = null!;
ZoomPanCanvas vectorZoomHost = null!;
SvgView vectorPreview = null!;
ScrollViewer pngScrollViewer = null!;
ZoomPanCanvas pngZoomHost = null!;
Image pngPreview = null!;
Label statusLabel = null!;
Label sizeLabel = null!;
Label drawTimeLabel = null!;
Label fileCountLabel = null!;

SvgDocument? currentDocument = null;
string[] svgFiles = LoadSvgFiles();
string? currentFilePath = null;
string? loadedFilePath = null;
Application.DispatcherUnhandledException += e =>
{
    e.Handled = true;
    _ = MessageBox.NotifyAsync("An unexpected error occurred", PromptIconKind.Crash, e.Exception.ToString());
};
var root = new Window()
    .Resizable(1180, 760)
    .OnBuild(x => x
        .Ref(out window)
        .Title("Aprillz.MewUI.Svg Sample")
        .Content(
            new TabControl()
                .TabItems(
                    new TabItem()
                        .Header("Icons")
                        .Content(IconsView()),
                    new TabItem()
                        .Header("Issues")
                        .Content(IssuesView())
                )
        )
        .OnLoaded(() =>
        {
            // Bind here (not at StatusBar() construction) — vectorPreview's `out` slot is
            // populated only after Body() runs, which happens after StatusBar() in arg order.
            drawTimeLabel.Bind(Label.TextProperty, vectorPreview, SvgView.LastDrawTimeProperty,
                ts => ts == TimeSpan.Zero ? string.Empty : $"Draw: {ts.GetText()}");

            string? startupFile = null;

            //startupFile = "__issue-127-01.svg";

            if (startupFile is not null && svgFiles.Length > 0)
            {
                int initialIndex = 0;
                for (int i = 0; i < svgFiles.Length; i++)
                {
                    if (string.Equals(Path.GetFileName(svgFiles[i]), startupFile, StringComparison.OrdinalIgnoreCase))
                    {
                        initialIndex = i;
                        break;
                    }
                }
                fileList.SelectedIndex = initialIndex;
                LoadFile(svgFiles[initialIndex]);
            }
        })
        .OnClosed(ReleaseCurrentPreview)
    );

Application.Run(root);

// The original SVG issue viewer (file list + source editor + vector/PNG preview).
Element IssuesView() => new DockPanel()
    .Padding(12)
    .Spacing(12)
    .Children(
        Toolbar().DockTop(),
        StatusBar().DockBottom(),
        Body()
    );

// Loads every .svg entry from icons.zip as an SvgImageSource and shows them in a WrapPanel.
// Parses every .svg in icons.zip up front (data), then shows them through a virtualized
// WrapPresenter (only visible rows are realized). Loads all entries to exercise parse-all cost
// while keeping the visual tree bounded by virtualization.
Element IconsView()
{
    var sources = new List<SvgImageSource>();
    string header;

    string zipPath = Path.Combine(AppContext.BaseDirectory, "icons.zip");
    if (File.Exists(zipPath))
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        using (var archive = ZipFile.OpenRead(zipPath))
        {
            // simple-icons.json (bundled in the zip) maps each icon to its brand color (hex).
            var hexBySlug = LoadIconColors(archive);

            foreach (var entry in archive.Entries
                .Where(entry => entry.Name.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase))
            {
                try
                {
                    using var stream = entry.Open();
                    var source = SvgImageSource.FromStream(stream);
                    // Tint each (monochrome) icon with its simple-icons brand color.
                    if (hexBySlug.TryGetValue(Path.GetFileNameWithoutExtension(entry.Name), out var hex))
                    {
                        source.Tint = Color.FromHex(hex);
                    }
                    sources.Add(source);
                }
                catch
                {
                    // Skip icons that fail to parse.
                }
            }
        }
        stopwatch.Stop();

        header = $"icons.zip — {sources.Count} icons (ZIP entry -> SvgImageSource), virtualized WrapPresenter | parse {stopwatch.ElapsedMilliseconds} ms";
    }
    else
    {
        header = "icons.zip was not found in the output directory.";
    }

    var icons = new ItemsControl()
        .WrapPresenter(48, 48)
        .ItemsSource(ItemsView.Create(sources))
        .ItemTemplate(new DelegateTemplate<SvgImageSource>(
            build: ctx => new Image()
                .Register(ctx, "Img")
                .StretchMode(Stretch.Uniform),
            bind: (view, item, index, ctx) =>
                ctx.Get<Image>("Img").Source(item)));

    // S/M/L radio buttons resize the virtualized tiles by re-applying the WrapPresenter
    // (SetPresenter keeps ItemsSource/ItemTemplate + scroll offset).
    void SetIconSize(double size) => icons.WrapPresenter(size, size);

    var sizeBar = new StackPanel()
        .Horizontal()
        .Spacing(8)
        .Children(
            new Label().Text("Size:").CenterVertical(),
            new RadioButton().Content("S").GroupName("IconSize").CenterVertical().OnChecked(() => SetIconSize(32)),
            new RadioButton().Content("M").GroupName("IconSize").CenterVertical().IsChecked().OnChecked(() => SetIconSize(64)),
            new RadioButton().Content("L").GroupName("IconSize").CenterVertical().OnChecked(() => SetIconSize(128)));

    return new DockPanel()
        .Padding(12)
        .Spacing(8)
        .Children(
            new Label()
                .Text(header)
                .DockTop(),

            sizeBar.DockTop(),

            icons
        );
}

// Reads simple-icons.json from the archive into a slug -> hex map. simple-icons names each SVG
// file by the slug derived from its title, so we derive the same slug to match files to colors.
static Dictionary<string, string> LoadIconColors(ZipArchive archive)
{
    var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    var entry = archive.GetEntry("simple-icons.json");
    if (entry is null)
    {
        return map;
    }

    try
    {
        using var stream = entry.Open();
        using var json = JsonDocument.Parse(stream);
        foreach (var icon in json.RootElement.EnumerateArray())
        {
            if (icon.TryGetProperty("title", out var titleEl) &&
                icon.TryGetProperty("hex", out var hexEl) &&
                titleEl.GetString() is string title &&
                hexEl.GetString() is string hex)
            {
                map[TitleToSlug(title)] = hex;
            }
        }
    }
    catch
    {
        // No color metadata — icons render untinted.
    }

    return map;
}

// Mirrors simple-icons' titleToSlug: special-char words, strip diacritics, keep [a-z0-9].
static string TitleToSlug(string title)
{
    var pre = title.ToLowerInvariant()
        .Replace("+", "plus")
        .Replace(".", "dot")
        .Replace("&", "and")
        .Replace("đ", "d")
        .Replace("ħ", "h")
        .Replace("ı", "i")
        .Replace("ŀ", "l")
        .Replace("ł", "l")
        .Replace("ß", "ss")
        .Replace("ŧ", "t")
        .Normalize(NormalizationForm.FormD);

    var sb = new StringBuilder(pre.Length);
    foreach (var ch in pre)
    {
        if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
        {
            continue;
        }
        if ((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9'))
        {
            sb.Append(ch);
        }
    }

    return sb.ToString();
}

Element Toolbar() => new DockPanel()
    .Spacing(8)
    .Children(
        new Label()
            .Ref(out fileCountLabel)
            .Text($"Files: {svgFiles.Length}")
            .DockLeft(),

        new StackPanel()
            .Horizontal()
            .Spacing(8)
            .DockRight()
            .Children(
            new Button()
                .Content("Apply")
                .OnClick(ApplyEditorText),

            new Button()
                .Content("Reset")
                .OnClick(() =>
                {
                    if (!string.IsNullOrEmpty(currentFilePath) && File.Exists(currentFilePath))
                    {
                        editor.Text = File.ReadAllText(currentFilePath);
                        ApplyEditorText();
                    }
                }),

            new Button()
                .Content("Reload Files")
                .OnClick(() =>
                {
                    svgFiles = LoadSvgFiles();
                    fileList.Items(svgFiles.Select(path => Path.GetFileName(path) ?? string.Empty).ToArray());
                    fileCountLabel.Text = $"Files: {svgFiles.Length}";

                    if (svgFiles.Length > 0)
                    {
                        fileList.SelectedIndex = 0;
                        LoadFile(svgFiles[0]);
                    }
                })
            )
    );

Element StatusBar() => new DockPanel()
    .Children(
        new Label()
            .Ref(out statusLabel)
            .Text("Ready")
            .DockLeft(),

        new Label()
            .Ref(out sizeLabel)
            .Text("viewBox: -")
            .DockRight(),

        new Label()
            .Ref(out drawTimeLabel)
            .Text(string.Empty)
            .DockRight()
    );

Element Body() => new Grid()
    .Columns("250,*")
    .Spacing(12)
    .Children(
        new GroupBox()
            .Header("SVG Files")
            .Content(
                new ListBox()
                    .Ref(out fileList)
                    .Items(svgFiles.Select(path => Path.GetFileName(path) ?? string.Empty).ToArray())
                    .OnSelectionChanged(_ =>
                    {
                        if (fileList.SelectedIndex >= 0 && fileList.SelectedIndex < svgFiles.Length)
                        {
                            LoadFile(svgFiles[fileList.SelectedIndex]);
                        }
                    })
            ),

        new SplitPanel()
            .Column(1)
            .Horizontal()
            .SplitterThickness(8)
            .FirstLength(new GridLength(1.1, GridUnitType.Star))
            .SecondLength(new GridLength(1, GridUnitType.Star))
            .First(
                new GroupBox()
                    .Header("SVG Source")
                    .Content(
                        new MultiLineTextBox()
                            .Ref(out editor)
                            .Wrap(false)
                            .FontFamily("Consolas")
                    )
            )
            .Second(
                new SplitPanel()
                    .Vertical()
                    .SplitterThickness(8)
                    .FirstLength(new GridLength(3, GridUnitType.Star))
                    .SecondLength(new GridLength(2, GridUnitType.Star))
                    .First(
                        new GroupBox()
                            .Header("Vector Render")
                            .Content(
                                PreviewPanel(
                                    out vectorScrollViewer,
                                    out vectorZoomHost,
                                    "Vector",
                                    new SvgView()
                                        .Ref(out vectorPreview)
                                )
                            )
                    )
                    .Second(
                        new GroupBox()
                            .Header("PNG Reference")
                            .Content(
                                PreviewPanel(
                                    out pngScrollViewer,
                                    out pngZoomHost,
                                    "PNG",
                                    new Image()
                                        .Ref(out pngPreview)
                                        .StretchMode(Stretch.None)
                                        .ImageScaleQuality(ImageScaleQuality.HighQuality)
                                )
                            )
                    )
            )
    );

FrameworkElement PreviewPanel(out ScrollViewer scrollViewer, out ZoomPanCanvas zoomHost, string prefix, UIElement child)
{
    scrollViewer = null!;
    zoomHost = null!;
    ScrollViewer previewScrollViewer = null!;
    ZoomPanCanvas previewZoomHost = null!;

    var panel = new DockPanel()
        .Spacing(8)
        .Children(
            new StackPanel()
                .DockTop()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Button()
                        .Content($"{prefix} Reset Zoom")
                        .OnClick(() => previewZoomHost.ResetView(previewScrollViewer))
                ),

            new Border()
                .Background(Color.White)
                .BorderBrush(Color.FromRgb(203, 213, 225))
                .BorderThickness(1)
                .Child(
                    new ScrollViewer()
                        .Ref(out previewScrollViewer)
                        .HorizontalScroll(ScrollMode.Auto)
                        .VerticalScroll(ScrollMode.Auto)
                        .Content(
                            new ZoomPanCanvas()
                                .Ref(out previewZoomHost)
                                .Apply(x =>
                                {
                                    x.CenterContent = true;
                                    x.ShowCheckerboardBackground = false;
                                    x.Child = child;
                                })
                        )
                )
        );

    scrollViewer = previewScrollViewer;
    zoomHost = previewZoomHost;
    return panel;
}

void LoadFile(string path)
{
    bool isDifferentFile = !string.Equals(loadedFilePath, path, StringComparison.OrdinalIgnoreCase);
    if (isDifferentFile)
    {
        ReleaseCurrentPreview();
        loadedFilePath = path;
    }

    currentFilePath = path;
    editor.Text = File.ReadAllText(path);
    ApplyEditorText();
}

void ApplyEditorText()
{
    try
    {
        // Use Parse(string, Uri) so relative href in <image>/<use> resolves against the
        // file's directory. Plain Parse(string) leaves BaseUri null and any
        // `../images/foo.png` reference fails to load.
        Uri? baseUri = null;
        if (!string.IsNullOrEmpty(currentFilePath))
        {
            try { baseUri = new Uri(Path.GetFullPath(currentFilePath)); }
            catch { }
        }
        var document = baseUri is null
            ? SvgDocument.Parse(editor.Text ?? string.Empty)
            : SvgDocument.Parse(editor.Text ?? string.Empty, baseUri);
        currentDocument = document;

        vectorPreview.Document = document;
        vectorPreview.InvalidateMeasure();
        vectorPreview.InvalidateVisual();

        FitPreview(vectorZoomHost, vectorScrollViewer);
        UpdatePngPreview();
        FitPreview(pngZoomHost, pngScrollViewer);

        string fileName = currentFilePath is null ? "(unsaved)" : Path.GetFileName(currentFilePath);
        sizeLabel.Text = $"viewBox: {document.ViewBoxWidth:0.##} x {document.ViewBoxHeight:0.##}";
        statusLabel.Text = $"Parsed: {fileName}{GetPngStatusSuffix()}";
    }
    catch (Exception ex)
    {
        UpdatePngPreview();
        FitPreview(pngZoomHost, pngScrollViewer);
        statusLabel.Text = $"Parse failed: {ex.Message}{GetPngStatusSuffix()}";
    }
}

void ReleaseCurrentPreview()
{
    vectorPreview.Document = null;
    pngPreview.Source = null;
    currentDocument = null;

    vectorPreview.InvalidateMeasure();
    vectorPreview.InvalidateVisual();
}

void UpdatePngPreview()
{
    string? pngPath = GetMatchingPngPath(currentFilePath);
    pngPreview.Source = pngPath is null ? null : ImageSource.FromFile(pngPath);
}

static void FitPreview(ZoomPanCanvas zoomHost, ScrollViewer scrollViewer)
{
    zoomHost.FitToView(scrollViewer);
}

string GetPngStatusSuffix()
{
    string? pngPath = GetMatchingPngPath(currentFilePath);
    return pngPath is null ? " | PNG: missing" : $" | PNG: {Path.GetFileName(pngPath)}";
}

static string? GetMatchingPngPath(string? svgPath)
{
    if (string.IsNullOrEmpty(svgPath))
    {
        return null;
    }

    string pngDir = Path.Combine(AppContext.BaseDirectory, "issue", "png");
    if (!Directory.Exists(pngDir))
    {
        return null;
    }

    string fileName = $"{Path.GetFileNameWithoutExtension(svgPath)}.png";
    string pngPath = Path.Combine(pngDir, fileName);
    return File.Exists(pngPath) ? pngPath : null;
}

static string[] LoadSvgFiles()
{
    string projectSvgDir = Path.Combine(AppContext.BaseDirectory, "issue", "svg");
    if (!Directory.Exists(projectSvgDir))
    {
        return [];
    }

    return Directory
        .GetFiles(projectSvgDir, "*.svg", SearchOption.AllDirectories)
        .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

static void Startup()
{
    var args = Environment.GetCommandLineArgs();

#if MEWUI_GALLERY_WIN
#pragma warning disable CA1416
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
}
