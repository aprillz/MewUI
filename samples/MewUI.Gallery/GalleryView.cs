using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView : ContentControl
{
    private Window window;

    public GalleryView(Window window)
    {
        this.window = window;
        
        this.Content(new ScrollViewer()
            .VerticalScroll(ScrollMode.Auto)
            .Padding(8)
            .Content(BuildGalleryContent()));
    }

    private FrameworkElement Card(string title, FrameworkElement content, double minWidth = 320) => new Border()
            .MinWidth(minWidth)
            .Padding(14)
            .CornerRadius(10)
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Label()
                            .WithTheme((t, c) => c.Foreground(t.Palette.Accent))
                            .Text(title)
                            .Bold(),
                        content
                    ));

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
                    new Label()
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
                Section("Window/Menu", WindowsMenuPage()),
                Section("Selection", SelectionPage()),
                Section("Lists", ListsPage()),
                Section("GridView", GridViewPage()),
                Section("Panels", PanelsPage()),
                Section("Layout", LayoutPage()),
                Section("Media", MediaPage())
            );
    }
}
