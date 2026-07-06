using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement LayoutPage()
    {
        FrameworkElement LabelBox(string title, TextAlignment horizontal, TextAlignment vertical, TextWrapping wrapping)
        {
            const string sample =
                "The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog";

            return new StackPanel()
                .Vertical()
                .Spacing(4)
                .Children(
                    new TextBlock()
                        .Text(title)
                        .FontSize(11),
                    new Border()
                        .Width(240)
                        .Height(80)
                        .Padding(6)
                        .BorderThickness(1)
                        .CornerRadius(6)
                        .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                        .Child(
                            new TextBlock()
                                .Text(sample)
                                .TextWrapping(wrapping)
                                .TextAlignment(horizontal)
                                .VerticalTextAlignment(vertical)
                        )
                );
        }

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
                "Label Wrap/Alignment",
                new UniformGrid()
                    .Columns(3)
                    .Spacing(8)
                    .Children(
                        LabelBox("Left/Top + Wrap", TextAlignment.Left, TextAlignment.Top, TextWrapping.Wrap),
                        LabelBox("Center/Top + Wrap", TextAlignment.Center, TextAlignment.Top, TextWrapping.Wrap),
                        LabelBox("Right/Top + Wrap", TextAlignment.Right, TextAlignment.Top, TextWrapping.Wrap),
                        LabelBox("Left/Center + Wrap", TextAlignment.Left, TextAlignment.Center, TextWrapping.Wrap),
                        LabelBox("Left/Bottom + Wrap", TextAlignment.Left, TextAlignment.Bottom, TextWrapping.Wrap),
                        LabelBox("Left/Top + NoWrap", TextAlignment.Left, TextAlignment.Top, TextWrapping.NoWrap),
                        LabelBox("Right/Top + NoWrap", TextAlignment.Right, TextAlignment.Top, TextWrapping.NoWrap)
                    )
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
                "StackPanel Wrap Growth",
                new Border()
                    .Width(260)
                    .Top()
                    .Padding(8)
                    .BorderThickness(1)
                    .CornerRadius(8)
                    .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                    .Child(
                        new StackPanel()
                            .Vertical()
                            .Spacing(6)
                            .Children(
                                new TextBlock()
                                    .TextWrapping(TextWrapping.Wrap)
                                    .Text("First wrapped label. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog."),
                                new TextBlock()
                                    .TextWrapping(TextWrapping.Wrap)
                                    .Text("Second wrapped label. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog.")
                            )
                    )
            ),

            Card(
                "Wrap + Button",
                new Border()
                    .Width(260)
                    .Top()
                    .Padding(8)
                    .BorderThickness(1)
                    .CornerRadius(8)
                    .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                    .Child(
                        new StackPanel()
                            .Vertical()
                            .Spacing(6)
                            .Children(
                                new TextBlock()
                                    .TextWrapping(TextWrapping.Wrap)
                                    .Text("Wrapped label followed by a button. The quick brown fox jumps over the lazy dog. The quick brown fox jumps over the lazy dog."),
                                new Border()
                                    .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                                    .Child(
                                        new TextBlock()
                                            .Center()
                                            .Text("After Wrap"))
                            )
                    )
            ),

            Card(
                "TextTrimming",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new Border()
                            .Width(200)
                            .Padding(6)
                            .BorderThickness(1)
                            .CornerRadius(6)
                            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                            .Child(
                                new TextBlock()
                                    .Text("No trimming: The quick brown fox jumps over the lazy dog")
                            ),
                        new Border()
                            .Width(200)
                            .Padding(6)
                            .BorderThickness(1)
                            .CornerRadius(6)
                            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                            .Child(
                                new TextBlock()
                                    .Text("CharacterEllipsis: The quick brown fox jumps over the lazy dog")
                                    .TextTrimming(TextTrimming.CharacterEllipsis)
                            ),
                        new Border()
                            .Width(200)
                            .Padding(6)
                            .BorderThickness(1)
                            .CornerRadius(6)
                            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                            .Child(
                                new TextBlock()
                                    .Text("Ellipsis + Center: The quick brown fox jumps over the lazy dog")
                                    .TextTrimming(TextTrimming.CharacterEllipsis)
                                    .TextAlignment(TextAlignment.Center)
                            ),
                        new Border()
                            .Width(200)
                            .Height(50)
                            .Padding(6)
                            .BorderThickness(1)
                            .CornerRadius(6)
                            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
                            .Child(
                                new TextBlock()
                                    .Text("Wrap + Ellipsis: The quick brown fox jumps over the lazy dog. The quick brown fox jumps.")
                                    .TextWrapping(TextWrapping.Wrap)
                                    .TextTrimming(TextTrimming.CharacterEllipsis)
                            )
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
