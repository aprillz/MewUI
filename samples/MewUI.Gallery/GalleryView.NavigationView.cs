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
                IconShape("shapes_regular"),
                "A PathGeometry wrapped in a PathShape. The fill follows the inherited foreground."),
            new NavigationIconEntry(
                "Emoji",
                DimWhenDisabled(new TextBlock().Text("😀").FontSize(14).Center()),
                "An emoji rendered by a TextBlock, demonstrating that an icon can be any Element."),
            new NavigationIconEntry(
                "Image",
                DimWhenDisabled(new Image().Source(imageSource).StretchMode(Stretch.Uniform)),
                "A bitmap icon rendered by Image with Uniform stretch inside the navigation icon slot."),
        };

        var navigation = new NavigationView
        {
            Height = 300,
            PaneWidth = 190,
            // Inline rather than Auto: the card is far narrower than the width Auto needs to keep the
            // pane beside the content, and this sample is about the icon slot, not the adaptive rule.
            PaneDisplayMode = PaneDisplayMode.Inline,
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
                        new TextBlock().Text(entry.Title).FontSize(ThemeFontSize.Medium).SemiBold(),
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

    /// <summary>
    /// Icons are arbitrary elements rather than a dedicated icon type, so nothing dims them when an
    /// ancestor is disabled. An element trigger declares the disabled look and restores the normal
    /// one on re-enable; the transition animates both directions.
    /// </summary>
    private static T DimWhenDisabled<T>(T icon) where T : UIElement
    {
        icon.Transitions = [Transition.Create(UIElement.OpacityProperty, 150)];
        icon.Triggers =
        [
            ElementTrigger.When(UIElement.IsEffectivelyEnabledProperty, false,
                Setter.Create(UIElement.OpacityProperty, 0.5)),
        ];
        return icon;
    }

    private sealed record NavigationIconEntry(string Title, Element Icon, string Description);
}
