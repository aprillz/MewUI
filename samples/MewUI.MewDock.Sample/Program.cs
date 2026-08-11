using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;

Startup(args);

// --- Component factory: each tab's content is a coloured pane showing its name + component key. -----------
var componentColors = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase)
{
    ["placeholder"] = Color.FromArgb(255, 0x4C, 0x6E, 0xF5),
    ["grid"] = Color.FromArgb(255, 0x2E, 0xA0, 0x6A),
    ["text"] = Color.FromArgb(255, 0xC7, 0x6E, 0x2E),
    ["chart"] = Color.FromArgb(255, 0xB0, 0x47, 0xC0),
    ["console"] = Color.FromArgb(255, 0x37, 0x47, 0x55),
    ["notes"] = Color.FromArgb(255, 0xC0, 0x47, 0x47),
};

int nextAdded = 1;

UIElement? Factory(DockPane pane)
{
    var accent = pane.Component is string key && componentColors.TryGetValue(key, out var color)
        ? color
        : Color.FromArgb(255, 0x70, 0x70, 0x70);

    var swatch = new Border()
        .Width(14)
        .Height(14)
        .CornerRadius(7)
        .Background(accent);
    var title = new TextBlock()
        .Text(pane.Title ?? "(unnamed)")
        .FontSize(15);
    var subtitle = new TextBlock()
        .Text($"component: {pane.Component ?? "(none)"}")
        .FontSize(11)
        .WithTheme((t, l) => l.Foreground = t.Palette.WindowText.WithAlpha(160));

    var header = new StackPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8)
        .Children(swatch, title);

    var border = new Border()
        .Padding(16)
        .Child(new StackPanel()
            .Spacing(4)
            .Children(header, subtitle))
        .Margin(2);

    if (!pane.IsDocument)
    {
        border.WithTheme((t, c) => c.Background(accent.WithAlpha(t.IsDark ? (byte)16 : (byte)32)));
    }
    return border;
}

// --- Layouts ---------------------------------------------------------------------------------------------
const string complexJson = """
{
  "global": {},
  "borders": [
    { "location": "left", "children": [] },
    { "location": "right", "children": [] },
    { "location": "bottom", "children": [] }
  ],
  "layout": {
    "type": "row",
    "children": [
      { "type": "tabset", "weight": 30, "children": [
        { "type": "tab", "name": "One (pinned)", "component": "placeholder", "enableClose": false, "enableDrag": false }
      ]},
      { "type": "row", "weight": 70, "children": [
        { "type": "tabset", "weight": 60, "children": [
          { "type": "tab", "name": "Two", "component": "grid" },
          { "type": "tab", "name": "Three", "component": "text" }
        ]},
        { "type": "tabset", "weight": 40, "children": [
          { "type": "tab", "name": "Chart", "component": "chart" },
          { "type": "tab", "name": "Notes", "component": "notes" }
        ]}
      ]}
    ]
  },
  "subLayouts": {
    "dock-left": {
      "type": "dock", "edge": "left", "size": 200, "dockRank": 0,
      "layout": { "type": "row", "children": [
        { "type": "tabset", "children": [
          { "type": "tab", "name": "Explorer", "component": "grid", "isDocument": false }
        ]}
      ]}
    },
    "dock-right": {
      "type": "dock", "edge": "right", "size": 200, "dockRank": 1,
      "layout": { "type": "row", "children": [
        { "type": "tabset", "children": [
          { "type": "tab", "name": "Properties", "component": "notes", "isDocument": false }
        ]}
      ]}
    },
    "dock-bottom": {
      "type": "dock", "edge": "bottom", "size": 180, "dockRank": 2,
      "layout": { "type": "row", "children": [
        { "type": "tabset", "weight": 50, "children": [
          { "type": "tab", "name": "Console", "component": "console", "isDocument": false }
        ]},
        { "type": "tabset", "weight": 50, "children": [
          { "type": "tab", "name": "Search", "component": "text", "isDocument": false }
        ]}
      ]}
    }
  }
}
""";

const string simpleJson = """
{
  "global": {},
  "layout": { "type": "row", "children": [
    { "type": "tabset", "weight": 50, "children": [ { "type": "tab", "name": "Left", "component": "grid" } ] },
    { "type": "tabset", "weight": 50, "children": [ { "type": "tab", "name": "Right", "component": "chart" } ] }
  ]}
}
""";

const string verticalJson = """
{
  "global": { "rootOrientationVertical": true },
  "layout": { "type": "row", "children": [
    { "type": "tabset", "weight": 50, "children": [ { "type": "tab", "name": "Top", "component": "text" } ] },
    { "type": "tabset", "weight": 50, "children": [ { "type": "tab", "name": "Bottom", "component": "console" } ] }
  ]}
}
""";

// --- Docking manager (the facade): one control hosting the whole dock space -------------------------------
var manager = new DockingManager().WithContentFactory(Factory);
ObservableValue<bool> isDocumentHost = new(true);

void Load(string json)
{
    manager.LoadLayout(json);
    // Centre is either the built-in document host (CenterContent = null) or a host-supplied custom element.
    manager.CenterContent = isDocumentHost.Value ? null : BuildNonDocumentContent();
    Console.WriteLine("Loaded layout.");
}

// A custom centre element: replaces the document host while tools still dock around the edges.
UIElement BuildNonDocumentContent()
{
    var title = new TextBlock()
        .Text("Non-document content")
        .FontSize(18);
    var subtitle = new TextBlock()
        .Text("This element replaces the document host. Tool docks still surround it.")
        .FontSize(12)
        .WithTheme((t, l) => l.Foreground = t.Palette.WindowText.WithAlpha(160));
    return new Border()
        .Padding(24)
        .Margin(2)
        .Child(
            new StackPanel()
                .Spacing(8)
                .Children(title, subtitle)
        );
}

Load(complexJson);

// --- Toolbar ---------------------------------------------------------------------------------------------
string? savedJson = null; // last explicit Save snapshot, restored by the Restore button

// Accent picker (gallery standard): WrapPanel.ItemWidth/ItemHeight forces each swatch to 22x22, the button itself
// carries no size.
Button AccentSwatch(Accent accent) => new Button()
    .CornerRadius(11)
    .MinHeight(22)
    .Width(22)
    .Height(22)
    .BorderThickness(0)
    .Content(string.Empty)
    .WithTheme((t, c) => c.Background(accent.GetAccentColor(t.IsDark)))
    .ToolTip(accent.ToString())
    .OnClick(() => Application.Current.SetAccent(accent));


// Accent swatches (gallery standard): click to apply that built-in accent.
var theme = new StackPanel()
    .Spacing(6)
    .Horizontal()
    .Children(
        new StackPanel()
            .Horizontal()
            .Spacing(6)
            .CenterVertical()
            .Children(BuiltInAccent.Accents.Select(AccentSwatch).ToArray()),
        new Button()
            .Content("Theme")
            .OnClick(() =>
            {
                var app = Application.Current;
                app.SetTheme(app.Theme.IsDark ? ThemeVariant.Light : ThemeVariant.Dark);
            })
    ).DockTop();

var toolbar = new StackPanel()
    .Horizontal()
    .Spacing(6)
    .DockTop()
    .Children(
        new Button()
            .Content("Complex")
            .OnClick(() => Load(complexJson)),
        new Button()
            .Content("Simple")
            .OnClick(() => Load(simpleJson)),
        new Button()
            .Content("Vertical")
            .OnClick(() => Load(verticalJson)),
        new Button()
            .Content("Add tab")
            .OnClick(() =>
            {
                int n = nextAdded++;
                manager.AddDocumentPane($"Added {n}", new TextBlock()
                    .Text($"Added pane #{n} (explicit content via AddDocumentPane)")
                    .Center());
            }),
        new Button()
            .Content("Popout")
            .OnClick(() => manager.ActivePane?.FloatGroup()),
        new Button()
            .Content("Save")
            .OnClick(() => savedJson = manager.SaveLayout()),
        new Button()
            .Content("Restore")
            .OnClick(() =>
            {
                if (savedJson is { } json)
                {
                    Load(json);
                }
            }),
        new Button()
            .BindContent(isDocumentHost, b => b ? "Document Host" : "Non-document Content")
            .OnClick(() =>
            {
                isDocumentHost.Value = !isDocumentHost.Value;
                manager.CenterContent = isDocumentHost.Value ? null : BuildNonDocumentContent();
            })
    );

var window = new Window()
    .Title("MewDock (FlexLayout port) Sample")
    .Resizable(1100, 740)
    .Content(new DockPanel().Spacing(6).Children(theme, toolbar, manager));

Application.Run(window);

static void Startup(string[] args)
{
#if MEWUI_ALL
    if (OperatingSystem.IsWindows())
    {
        Win32Platform.Register();
        if (args.Contains("--gdi"))
        {
            GdiBackend.Register();
        }
        else if (args.Contains("--vg"))
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
#elif MEWUI_WIN
    Win32Platform.Register();
    Direct2DBackend.Register();
#elif MEWUI_OSX
    MacOSPlatform.Register();
    MewVGMacOSBackend.Register();
#elif MEWUI_LINUX
    X11Platform.Register();
    MewVGX11Backend.Register();
#endif
}
