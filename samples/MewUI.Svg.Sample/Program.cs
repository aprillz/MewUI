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
        .Title("Aprillz.MewUI SVG Issues Viewer")
        .Content(
            new DockPanel()
                .Padding(12)
                .Spacing(12)
                .Children(
                    Toolbar()
                        .DockTop(),

                    StatusBar()
                        .DockBottom(),

                    Body()
                )
        )
        .OnLoaded(() =>
        {
            // Bind here (not at StatusBar() construction) — vectorPreview's `out` slot is
            // populated only after Body() runs, which happens after StatusBar() in arg order.
            drawTimeLabel.Bind(Label.TextProperty, vectorPreview, SvgView.LastDrawTimeProperty,
                ts => ts == TimeSpan.Zero ? string.Empty : $"Draw: {ts.GetText()}");

            if (svgFiles.Length > 0)
            {
                int initialIndex = 0;
                for (int i = 0; i < svgFiles.Length; i++)
                {
                    if (string.Equals(Path.GetFileName(svgFiles[i]), "__issue-134-01.svg", StringComparison.OrdinalIgnoreCase))
                    {
                        initialIndex = i;
                        break;
                    }
                }
                fileList.SelectedIndex = initialIndex;
                LoadFile(svgFiles[initialIndex]);
            }
            else
            {
                statusLabel.Text = "No SVG files found in sample svg folder";
            }
        })
        .OnClosed(ReleaseCurrentPreview)
    );

Application.Run(root);

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
