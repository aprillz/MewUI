using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement NavigationViewPage()
    {
        var imageSource = ImageSource.FromFile(CombineBaseDirectory("Resources", "document.png"));
        var entries = new[]
        {
            new NavigationIconEntry(
                "PathShape",
                Ico("shapes_regular"),
                "A PathGeometry wrapped in a PathShape. The fill follows the inherited foreground."),
            new NavigationIconEntry(
                "Emoji",
                new TextBlock().Text("😀").FontSize(14).Center(),
                "An emoji rendered by a TextBlock, demonstrating that an icon can be any Element."),
            new NavigationIconEntry(
                "Image",
                new Image { Source = imageSource, StretchMode = Stretch.Uniform },
                "A bitmap icon rendered by Image with Uniform stretch inside the navigation icon slot."),
        };

        var navigation = new NavigationView
        {
            Height = 300,
            PaneWidth = 190,
            PaneDisplayMode = PaneDisplayMode.Expanded,
        };
        navigation.Items(
            entries,
            entry => entry.Title,
            icon: entry => entry.Icon,
            content: entry => new Border()
                .BorderThickness(0)
                .Padding(24)
                .Child(new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock().Text(entry.Title).FontSize(22).Bold(),
                        new TextBlock().Text(entry.Description).TextWrapping(TextWrapping.Wrap))));
        navigation.SelectedIndex = 0;

        return Card(
            "NavigationView / Element icons",
            new StackPanel()
                .Vertical()
                .Spacing(8)
                .Children(
                    new TextBlock()
                        .Text("PathShape, emoji TextBlock, and Image share the same Element icon API.")
                        .WithTheme((t, text) => text.Foreground(t.Palette.DisabledText)),
                    navigation),
            minWidth: 560);
    }

    private sealed record NavigationIconEntry(string Title, Element Icon, string Description);
}
