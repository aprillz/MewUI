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
            Page("Buttons", ButtonsPage, "select_object_regular"),
            Page("Inputs", InputsPage, "text_effects_regular"),
            Page("Drag & Drop", DragDropPage, "drag_regular"),
            Page("Selection", SelectionPage, "vote_regular"),
            Page("Typography", TypographyPage, "text_word_count_regular"),

            Group("Collections"),
            Page("Lists", ListsPage, "tabs_regular"),
            Page("TreeView", TreeViewPage, "text_bullet_list_tree_regular"),
            Page("GridView", GridViewPage, "table_freeze_regular"),
            Page("ItemsControl", ItemsControlPage, "group_list_regular"),

            Group("Layout"),
            Page("Panels", PanelsPage, "dual_screen_update_regular"),
            Page("Layout", LayoutPage, "compass_northwest_regular"),
            Page("Transform", TransformPage, "arrow_rotate_clockwise_regular"),

            Group("Graphics"),
            Page("Shapes", ShapesPage, "star_off_regular"),
            Page("Icons", IconsPage, "app_generic_regular"),
            Page("Media", MediaPage, "video_switch_regular"),
            Page("Custom Rendering", CustomRenderingPage, "text_column_three_regular"),
            Page("Transitions", TransitionsPage, "arrow_sync_circle_regular"),

            Group("Windowing"),
            Page("Window", WindowPage, "window_regular"),
            Page("Menu", MenuPage, "navigation_regular"),
            Page("MessageBox", MessageBoxPage, "note_regular"),
            Page("File Dialog", FileDialogPage, "folder_open_regular"),
            Page("ShowDialog", ShowDialogPage, "preview_link_regular"),
            Page("Overlay", OverlayPage, "tab_desktop_clock_regular"),

            Group("System"),
            Page("Threading", ThreadingPage, "power_regular"),
        ];
    }
}
