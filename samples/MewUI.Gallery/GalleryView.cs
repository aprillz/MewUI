using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView : UserControl
{
    private Window window;

    // All card borders, so the global "Cached" toggle can flip BitmapCache on every card at once.
    private readonly List<Border> _cardBorders = new();

    // Flip to switch the gallery shell between the single-scroll list and a NavigationView (pane + content).
    private const bool UseNavigationView = true;

    protected override Element? OnBuild() =>
        UseNavigationView ? BuildNavigationShell() : BuildScrollShell();

    private Element BuildScrollShell() =>
        new ScrollViewer()
            .VerticalScroll(ScrollMode.Auto)
            .Padding(8)
            .Content(BuildGalleryContent());

    private Element BuildNavigationShell()
    {
        var entries = NavEntries();
        var nav = new NavigationView { PaneWidth = 220 };

        Element? PageContent(NavEntry e) => e.Page != null
            ? new ScrollViewer().VerticalScroll(ScrollMode.Auto).Padding(8).Content(e.Page())
            : null;

        nav.Items(entries, e => e.Title, icon: e => e.Icon, content: PageContent, kind: e => e.Kind);

        // Bottom-pinned footer item, sharing selection with the main list.
        var footer = new[]
        {
            new NavEntry(NavigationItemKind.Item, "Settings", Ico("settings_regular"), SettingsPage),
        };
        nav.FooterItems(footer, e => e.Title, icon: e => e.Icon, content: PageContent, kind: e => e.Kind);

        nav.SelectedIndex = Array.FindIndex(entries, e => e.Kind == NavigationItemKind.Item);

        // Top-only 1px separator below the app top bar (static chrome, no hover).
        return new Border()
            .BorderThickness(new Thickness(0, 1, 0, 0))
            .WithTheme((t, b) => b.BorderBrush(t.Palette.WindowBackground.Lerp(t.Palette.ControlBorder, 0.45)))
            .Child(nav);
    }

    /// <summary>Content shown by the footer "Settings" entry (theme/rendering controls), supplied by the host.</summary>
    public FrameworkElement? SettingsContent { get; set; }

    private FrameworkElement SettingsPage() =>
        SettingsContent ?? new StackPanel()
            .Vertical()
            .Children(new TextBlock().Text("Settings").FontSize(22).Bold());

    private static PathGeometry Ico(string name)
    {
        var all = IconResource.GetAll();
        var entry = Array.Find(all, x => x.Name == name) ?? all[0];
        return PathGeometry.Parse(entry.PathData);
    }

    public GalleryView(Window window)
    {
        this.window = window;
        InitializeDragDropSample();
        Build();
    }

    public static string CombineBaseDirectory(params string[] path)
        => Path.Combine([AppContext.BaseDirectory, .. path]);

    private FrameworkElement Card(string title, FrameworkElement content, double minWidth = 320)
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
                        content
                    ));
        _cardBorders.Add(border);
        return border;
    }

    /// <summary>Globally turns BitmapCache on/off for every card (debug toggle).</summary>
    public void SetCardsCached(bool cached)
    {
        foreach (var border in _cardBorders)
        {
            border.CacheMode = cached ? new BitmapCache() : null;
        }
    }

    private FrameworkElement CardGrid(params FrameworkElement[] cards) => new WrapPanel()
        .Orientation(Orientation.Horizontal)
        .Spacing(8)
        .Children(cards);

    private FrameworkElement BuildGalleryContent()
    {
        FrameworkElement Section(string title, FrameworkElement content) =>
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock().Text(title).FontSize(18).Bold(),
                    content
                );

        var children = new List<FrameworkElement>();
        foreach (var e in NavEntries())
        {
            if (e.Kind == NavigationItemKind.Header)
            {
                children.Add(new TextBlock().Text(e.Title).FontSize(22).Bold());
            }
            else if (e.Page != null)
            {
                children.Add(Section(e.Title, e.Page()));
            }
        }

        return new StackPanel().Vertical().Spacing(16).Children(children.ToArray());
    }

    private sealed record NavEntry(NavigationItemKind Kind, string Title, PathGeometry? Icon, Func<FrameworkElement>? Page);

    // Single source of navigation entries, shared by both shells. Group headers carry a top-level icon;
    // pages are the selectable items.
    private NavEntry[] NavEntries()
    {
        NavEntry Group(string title) => new(NavigationItemKind.Header, title, null, null);
        NavEntry Page(string title, Func<FrameworkElement> page, string icon) => new(NavigationItemKind.Item, title, Ico(icon), page);

        // Headers carry no icon; each selectable item uses a distinct icon.
        return
        [
            Group("Basics"),
            Page("Buttons", ButtonsPage, "tap_single_regular"),
            Page("Inputs", InputsPage, "textbox_regular"),
            Page("Drag & Drop", DragDropPage, "drag_regular"),
            Page("Selection", SelectionPage, "multiselect_regular"),
            Page("Typography", TypographyPage, "text_font_regular"),
            Page("Styling", StylingPage, "color_regular"),

            Group("Collections"),
            Page("Lists", ListsPage, "list_regular"),
            Page("TreeView", TreeViewPage, "text_bullet_list_tree_regular"),
            Page("GridView", GridViewPage, "grid_regular"),
            Page("ItemsControl", ItemsControlPage, "collections_regular"),

            Group("Layout"),
            Page("Panels", PanelsPage, "dock_regular"),
            Page("Layout", LayoutPage, "match_app_layout_regular"),
            Page("Transform", TransformPage, "resize_regular"),

            Group("Graphics"),
            Page("Shapes", ShapesPage, "shapes_regular"),
            Page("Icons", IconsPage, "icons_regular"),
            Page("Media", MediaPage, "image_library_regular"),
            Page("Custom Rendering", CustomRenderingPage, "paint_brush_regular"),
            Page("Transitions", TransitionsPage, "arrow_sync_circle_regular"),

            Group("Windowing"),
            Page("Window", WindowPage, "window_regular"),
            Page("Menu", MenuPage, "options_regular"),
            Page("MessageBox", MessageBoxPage, "alert_on_regular"),
            Page("File Dialog", FileDialogPage, "folder_open_regular"),
            Page("ShowDialog", ShowDialogPage, "window_new_regular"),
            Page("Overlay", OverlayPage, "layer_regular")
        ];
    }
}
