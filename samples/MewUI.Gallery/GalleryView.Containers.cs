using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement ContainersPage()
    {
        return CardGrid(
            Card(
                "GroupBox",
                new GroupBox()
                    .Header("Header")
                    .Content(
                        new StackPanel()
                            .Vertical()
                            .Spacing(6)
                            .Children(
                                new TextBlock().Text("GroupBox content"),
                                new Button().Content("Action")
                            )
                    )
            ),

            Card(
                "Expander",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Expander()
                            .Header("Details")
                            .Content(
                                new StackPanel()
                                    .Vertical()
                                    .Spacing(6)
                                    .Children(
                                        new TextBlock().Text("Click the header to collapse."),
                                        new Button().Content("Action")
                                    )),
                        new Expander { IsExpanded = false }
                            .Header("More options")
                            .Content(new TextBlock().Text("Hidden until expanded."))
                    )
            ),

            Card(
                "Border + Alignment",
                new Border()
                    .Height(120)
                    .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                    .BorderThickness(1)
                    .CornerRadius(12)
                    .Child(new TextBlock()
                            .Text("Centered Text")
                            .Center()
                            .Bold())
            ),

            Card(
                "Border Top + Wrap Growth",
                new Border()
                    .Width(260)
                    .Top()
                    .Padding(8)
                    .BorderThickness(1)
                    .CornerRadius(8)
                    .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                    .Child(
                        new TextBlock()
                            .TextWrapping(TextWrapping.Wrap)
                            .Text("Top-aligned border should grow with wrapped text. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog.")
                    )
            ),

            Card(
                "ScrollViewer",
                new ScrollViewer()
                    .Height(120)
                    .Width(200)
                    .VerticalScroll(ScrollMode.Auto)
                    .HorizontalScroll(ScrollMode.Auto)
                    .Content(
                        new StackPanel()
                            .Vertical()
                            .Spacing(6)
                            .Children(Enumerable.Range(1, 15).Select(i => new TextBlock().Text($"Line {i} - The quick brown fox jumps over the lazy dog.")).ToArray())
                    )
            )
        );
    }
}
