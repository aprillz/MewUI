using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView : UserControl
{
    private Window window;

    // All card borders, so the global "Cached" toggle can flip BitmapCache on every card at once.
    private readonly List<Border> _cardBorders = new();

    protected override Element? OnBuild() =>
        new ScrollViewer()
            .VerticalScroll(ScrollMode.Auto)
            .Padding(8)
            .Content(BuildGalleryContent());

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
                    new TextBlock()
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
                Section("Selection", SelectionPage()),
                Section("Window/Menu", WindowsMenuPage()),
                Section("MessageBox", MessageBoxPage()),
                Section("Lists", ListsPage()),
                Section("GridView", GridViewPage()),
                Section("Panels", PanelsPage()),
                Section("Layout", LayoutPage()),
                Section("Typography", TypographyPage()),
                Section("Media", MediaPage()),
                Section("Custom Rendering", CustomRenderingPage()),
                Section("Transform", TransformPage()),
                Section("Shapes", ShapesPage()),
                Section("Icons", IconsPage()),
                Section("Transitions", TransitionsPage()),
                Section("Overlay", OverlayPage())
            );
    }
}
