using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView : UserControl
{
    private Window window;

    // All card borders, so the global "Cached" toggle can flip BitmapCache on every card at once.
    private readonly List<Border> _cardBorders = new();

    protected override Element? OnBuild() => BuildNavigationShell();

    private Element BuildNavigationShell()
    {
        var entries = NavEntries();
        var nav = new NavigationView { PaneWidth = 220 };

        Element? PageContent(NavEntry e) => e.Page != null
            ? new ScrollViewer().VerticalScroll(ScrollMode.Auto).Padding(24).Content(
                new StackPanel()
                    .Vertical()
                    .Spacing(16)
                    .Children(
                        new TextBlock()
                            .Text(e.Title)
                            .FontSize(ThemeFontSize.Large)
                            .SemiBold()
                            .LineBoxTrim(LineBoxTrim.CapAndBaseline),
                        e.Page()))
            : null;

        nav.Items(entries, e => e.Title, icon: e => e.Icon, content: PageContent, kind: e => e.Kind);

        // Bottom-pinned footer item, sharing selection with the main list.
        var footer = new[]
        {
            new NavEntry(NavigationItemKind.Item, "Settings", IconShape("settings_regular"), SettingsPage),
        };
        nav.FooterItems(footer, e => e.Title, icon: e => e.Icon, content: PageContent, kind: e => e.Kind);

        nav.SelectedIndex = Array.FindIndex(entries, e => e.Kind == NavigationItemKind.Item);

        // Top-only 1px separator below the app top bar (static chrome, no hover).
        return new Border()
            .WithTheme((t, b) => b
                .BorderBrush(t.Palette.WindowBackground.Lerp(t.Palette.ControlBorder, 0.45))
                .BorderThickness(new Thickness(0, t.Metrics.ControlBorderThickness, 0, 0)))
            .Child(nav);
    }

    /// <summary>Content shown by the footer "Settings" entry (theme/rendering controls), supplied by the host.</summary>
    public FrameworkElement? SettingsContent { get; set; }

    private FrameworkElement SettingsPage() =>
        SettingsContent ?? new StackPanel().Vertical();

    // Stands in until the icon dictionary arrives, so a navigation item never renders an empty slot.
    // Material Symbols "select" (24dp): a dotted square whose 16 dots read as "nothing here yet",
    // where a solid shape would read as a real icon.
    private const string FALLBACK_ICON =
        """
        M3.288,4.712C3.096,4.521,3,4.283,3,4s0.096-0.521,0.288-0.712C3.479,3.096,3.717,3,4,3 s0.521,0.096,0.712,0.288C4.904,3.479,5,3.717,5,4S4.904,4.521,4.712,4.712C4.521,4.904,4.283,5,4,5S3.479,4.904,3.288,4.712z
        M7.288,4.712C7.096,4.521,7,4.283,7,4s0.096-0.521,0.288-0.712C7.479,3.096,7.717,3,8,3s0.521,0.096,0.712,0.288 C8.904,3.479,9,3.717,9,4S8.904,4.521,8.712,4.712C8.521,4.904,8.283,5,8,5S7.479,4.904,7.288,4.712z
        M11.288,4.712 C11.096,4.521,11,4.283,11,4s0.096-0.521,0.288-0.712C11.479,3.096,11.717,3,12,3s0.521,0.096,0.713,0.288 C12.904,3.479,13,3.717,13,4s-0.096,0.521-0.287,0.712C12.521,4.904,12.283,5,12,5S11.479,4.904,11.288,4.712z
        M15.287,4.712 C15.096,4.521,15,4.283,15,4s0.096-0.521,0.287-0.712C15.479,3.096,15.717,3,16,3s0.521,0.096,0.713,0.288 C16.904,3.479,17,3.717,17,4s-0.096,0.521-0.287,0.712C16.521,4.904,16.283,5,16,5S15.479,4.904,15.287,4.712z
        M19.287,4.712 C19.096,4.521,19,4.283,19,4s0.096-0.521,0.287-0.712C19.479,3.096,19.717,3,20,3s0.521,0.096,0.713,0.288 C20.904,3.479,21,3.717,21,4s-0.096,0.521-0.287,0.712C20.521,4.904,20.283,5,20,5S19.479,4.904,19.287,4.712z
        M3.288,8.712 C3.096,8.521,3,8.283,3,8s0.096-0.521,0.288-0.712C3.479,7.096,3.717,7,4,7s0.521,0.096,0.712,0.288C4.904,7.479,5,7.717,5,8 S4.904,8.521,4.712,8.712C4.521,8.904,4.283,9,4,9S3.479,8.904,3.288,8.712z
        M19.287,8.712C19.096,8.521,19,8.283,19,8 s0.096-0.521,0.287-0.712C19.479,7.096,19.717,7,20,7s0.521,0.096,0.713,0.288C20.904,7.479,21,7.717,21,8s-0.096,0.521-0.287,0.712 C20.521,8.904,20.283,9,20,9S19.479,8.904,19.287,8.712z
        M3.288,12.713C3.096,12.521,3,12.283,3,12s0.096-0.521,0.288-0.712 C3.479,11.096,3.717,11,4,11s0.521,0.096,0.712,0.288C4.904,11.479,5,11.717,5,12s-0.096,0.521-0.288,0.713 C4.521,12.904,4.283,13,4,13S3.479,12.904,3.288,12.713z
        M19.287,12.713C19.096,12.521,19,12.283,19,12s0.096-0.521,0.287-0.712 C19.479,11.096,19.717,11,20,11s0.521,0.096,0.713,0.288C20.904,11.479,21,11.717,21,12s-0.096,0.521-0.287,0.713S20.283,13,20,13 S19.479,12.904,19.287,12.713z
        M3.288,16.713C3.096,16.521,3,16.283,3,16s0.096-0.521,0.288-0.713C3.479,15.096,3.717,15,4,15 s0.521,0.096,0.712,0.287C4.904,15.479,5,15.717,5,16s-0.096,0.521-0.288,0.713C4.521,16.904,4.283,17,4,17 S3.479,16.904,3.288,16.713z
        M19.287,16.713C19.096,16.521,19,16.283,19,16s0.096-0.521,0.287-0.713S19.717,15,20,15 s0.521,0.096,0.713,0.287S21,15.717,21,16s-0.096,0.521-0.287,0.713S20.283,17,20,17S19.479,16.904,19.287,16.713z
        M3.288,20.713 C3.096,20.521,3,20.283,3,20s0.096-0.521,0.288-0.713C3.479,19.096,3.717,19,4,19s0.521,0.096,0.712,0.287 C4.904,19.479,5,19.717,5,20s-0.096,0.521-0.288,0.713C4.521,20.904,4.283,21,4,21S3.479,20.904,3.288,20.713z
        M7.288,20.713 C7.096,20.521,7,20.283,7,20s0.096-0.521,0.288-0.713C7.479,19.096,7.717,19,8,19s0.521,0.096,0.712,0.287 C8.904,19.479,9,19.717,9,20s-0.096,0.521-0.288,0.713C8.521,20.904,8.283,21,8,21S7.479,20.904,7.288,20.713z
        M11.288,20.713 C11.096,20.521,11,20.283,11,20s0.096-0.521,0.288-0.713C11.479,19.096,11.717,19,12,19s0.521,0.096,0.713,0.287S13,19.717,13,20 s-0.096,0.521-0.287,0.713S12.283,21,12,21S11.479,20.904,11.288,20.713z
        M15.287,20.713C15.096,20.521,15,20.283,15,20 s0.096-0.521,0.287-0.713S15.717,19,16,19s0.521,0.096,0.713,0.287S17,19.717,17,20s-0.096,0.521-0.287,0.713S16.283,21,16,21 S15.479,20.904,15.287,20.713z
        M19.287,20.713C19.096,20.521,19,20.283,19,20s0.096-0.521,0.287-0.713S19.717,19,20,19 s0.521,0.096,0.713,0.287S21,19.717,21,20s-0.096,0.521-0.287,0.713S20.283,21,20,21S19.479,20.904,19.287,20.713z
        """;

    /// <summary>Builds a shape for a named icon, on the design grid it was drawn on.</summary>
    private static PathShape IconShape(string name)
    {
        var icon = new PathShape { Stretch = Stretch.Uniform };
        BindNamedIcon(icon, name);

        icon.Bind(Shape.FillProperty, icon, TextElement.ForegroundProperty,
            (Color color) => (Brush)new SolidColorBrush(color));
        return icon;
    }

    /// <summary>One icon's geometry and the design grid it was drawn on.</summary>
    private sealed record NamedIcon(PathGeometry Geometry, Rect ViewBox);

    // One value per requested name. The dictionary can arrive after the shapes exist, and item templates
    // recycle their shapes, so the shapes bind to these instead of being refilled by hand.
    private static readonly Dictionary<string, ObservableValue<NamedIcon>> _namedIcons = new();

    /// <summary>Binds a shape to a named icon, so a late-arriving dictionary reaches it.</summary>
    private static void BindNamedIcon(PathShape shape, string name)
    {
        var icon = NamedIconValue(name);
        shape.Bind(PathShape.DataProperty, icon, x => x.Geometry);

        // Without the design grid, Uniform fits the ink, so an icon drawn with room around it comes out
        // larger than one drawn to the edges even though both sit in the same 16 DIP slot.
        shape.Bind(Shape.ViewBoxProperty, icon, x => (Rect?)x.ViewBox);
    }

    private static ObservableValue<NamedIcon> NamedIconValue(string name)
    {
        if (_namedIcons.TryGetValue(name, out var existing))
        {
            return existing;
        }

        if (_namedIcons.Count == 0)
        {
            Resources.Icons.Changed += () =>
            {
                foreach (var pair in _namedIcons)
                {
                    pair.Value.Value = ReadNamedIcon(pair.Key);
                }
            };
        }

        var icon = new ObservableValue<NamedIcon>(ReadNamedIcon(name));
        _namedIcons.Add(name, icon);
        return icon;
    }

    /// <summary>Reads one icon from the dictionary, standing in the placeholder while it is unavailable.</summary>
    private static NamedIcon ReadNamedIcon(string name)
    {
        var all = IconResource.GetAll(Resources.Icons.Value);
        var entry = Array.Find(all, x => x.Name == name);
        var geometry = PathGeometry.Parse(entry?.PathData ?? FALLBACK_ICON);

        // Shared by every shape bound to this name, so it must not be mutated afterwards.
        geometry.Freeze();
        return new NamedIcon(geometry, IconViewBox(geometry));
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
        .Spacing(24)
        .Children(cards);

    private sealed record NavEntry(NavigationItemKind Kind, string Title, Element? Icon, Func<FrameworkElement>? Page);

    // Group headers separate sections; pages are selectable items with their own icon elements.
    private NavEntry[] NavEntries()
    {
        NavEntry Group(string title) => new(NavigationItemKind.Header, title, null, null);
        NavEntry Page(string title, Func<FrameworkElement> page, string icon) => new(NavigationItemKind.Item, title, IconShape(icon), page);

        // Headers carry no icon; each selectable item uses a distinct icon.
        return
        [
            Group("Basics"),
            Page("Buttons", ButtonsPage, "tap_single_regular"),
            Page("Inputs", InputsPage, "textbox_regular"),
            Page("Data Binding", DataBindingPage, "link_regular"),
            Page("Drag & Drop", DragDropPage, "drag_regular"),
            Page("Selection", SelectionPage, "multiselect_regular"),
            Page("Typography", TypographyPage, "text_font_regular"),
            Page("Styling", StylingPage, "color_regular"),

            Group("Navigation"),
            Page("NavigationView", NavigationViewPage, "navigation_regular"),

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
            Page("ToolBar", ToolBarPage, "wrench_regular"),
            Page("MessageBox", MessageBoxPage, "alert_on_regular"),
            Page("File Dialog", FileDialogPage, "folder_open_regular"),
            Page("ShowDialog", ShowDialogPage, "window_new_regular"),
            Page("Overlay", OverlayPage, "layer_regular")
        ];
    }
}

static class ResourceImageExtensions
{
    /// <summary>Binds an image to a host-filled resource box, which may still be empty.</summary>
    public static Image BindSource(this Image image, ObservableValue<IImageSource?> resource)
    {
        image.SetBinding(Image.SourceProperty, resource);
        return image;
    }
}
