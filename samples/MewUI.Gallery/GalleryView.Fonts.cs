using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Text;

namespace Aprillz.MewUI.Gallery;

partial class GalleryView
{
    private FrameworkElement FontsPage()
    {
        // Font Inheritance: Border sets FontSize=16, children inherit
        var inheritanceDemo = new Border()
            .FontSize(16)
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("Inherited 16pt (from parent Border)"),
                        new TextBlock().Text("Also inherited 16pt"),
                        new TextBlock().Text("Override: 10pt").FontSize(10),
                        new Button().Content("Button (inherited 16pt)"),
                        new TextBox().Placeholder("TextBox (inherited 16pt)")
                    ));

        // FontFamily Inheritance
        var fontFamilyDemo = new Border()
            .FontFamily("Consolas, Menlo, DejaVu Sans Mono")
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("Inherited Fixed"),
                        new TextBlock().Text("Also Fixed"),
                        new TextBlock().Text("Override: Default").FontFamily(Theme.Metrics.FontFamily),
                        new Button().Content("Fixed Button")
                    ));

        // FontWeight Inheritance
        var fontWeightDemo = new Border()
            .Bold()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("Inherited Bold"),
                        new TextBlock().Text("Also Bold"),
                        new TextBlock().Text("Override: Normal").FontWeight(FontWeight.Normal),
                        new Button().Content("Bold Button")
                    ));

        // FontStyle Inheritance. Times New Roman because its italic is a face of its own: a family without
        // one is slanted by the backend, which reads as italic but is not the same drawing.
        var italicLabel = new TextBlock().Text("Inherited Italic");
        var uprightLabel = new TextBlock().Text("Override: Normal").Italic(false);
        var italicButton = new Button().Content("Italic Button");

        var fontStyleDemo = new Border()
            .FontFamily("Times New Roman")
            .FontSize(16)
            .Italic()
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        italicLabel,
                        new TextBlock().Text("Bold Italic").Bold(),
                        uprightLabel,
                        italicButton
                    ));

        // Nested inheritance: outer=20pt, inner=12pt
        var nestedDemo = new Border()
            .FontSize(20)
            .Padding(12)
            .BorderThickness(1)
            .CornerRadius(8)
            .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground).BorderBrush(t.Palette.ControlBorder))
            .Child(
                new StackPanel()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock().Text("20pt (from outer)"),
                        new Border()
                            .FontSize(12)
                            .Padding(8)
                            .BorderThickness(1)
                            .CornerRadius(6)
                            .WithTheme((t, b) => b.BorderBrush(t.Palette.ControlBorder))
                            .Child(
                                new StackPanel()
                                    .Vertical()
                                    .Spacing(4)
                                    .Children(
                                        new TextBlock().Text("12pt (from inner Border)"),
                                        new TextBlock().Text("Also 12pt")
                                    )),
                        new TextBlock().Text("Back to 20pt")
                    ));

        return CardGrid(
            Card("Font Weight Ramp (100 to 900)", FontWeightRampDemo(), minWidth: 560),
            Card("Font Size Inheritance", inheritanceDemo),
            Card("Font Family Inheritance", fontFamilyDemo),
            Card("Font Weight Inheritance", fontWeightDemo),
            Card("Font Style Inheritance", fontStyleDemo),
            Card("Nested Inheritance", nestedDemo),

            Card(
                "Emoji",
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Children(
                        new TextBlock()
                            .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                            .FontSize(24),
                        new TextBlock()
                            .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                            .FontSize(20),
                        new TextBlock()
                            .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                            .FontSize(16),
                        new TextBlock()
                            .Text("\U0001F36B \U0001F600 \U0001F389 \U0001F680 \U0001F308 \U0001F40D \U0001F3B5 \U00002764\U0000FE0F \U0001F525 \U0001F4A1")
                            .FontSize(12),
                        new TextBox()
                            .Placeholder("Type or paste emoji here...")
                            .Text("\U0001F36B\U0001F600\U0001F389"),
                        new TextBlock()
                            .Text("Mixed: Hello \U0001F30D World \U0001F680!")
                            .FontSize(14)
                    )
            )
        );
    }

    private static readonly FontWeight[] _weightRamp =
    [
        FontWeight.Thin, FontWeight.ExtraLight, FontWeight.Light, FontWeight.Normal,
        FontWeight.Medium, FontWeight.SemiBold, FontWeight.Bold, FontWeight.ExtraBold, FontWeight.Black 
    ];

    private FrameworkElement FontWeightRampDemo()
    {
        var rows = new List<FrameworkElement>(_weightRamp.Length + 1);
        foreach (var weight in _weightRamp)
        {
            var sample = new TextBlock()
                .FontSize(20)
                .FontWeight(weight)
                .Text("Hamburgefonstiv 123");
            // The font arrives with the other gallery resources; until then the row uses the theme font.
            sample.SetBinding(TextElement.FontFamilyProperty, Resources.InterVariable,
                family => family ?? Theme.Metrics.FontFamily);
            rows.Add(new StackPanel()
                .Horizontal()
                .Spacing(12)
                .Children(
                    new TextBlock()
                        .Width(104)
                        .FontSize(ThemeFontSize.Small)
                        .WithTheme((t, b) => b.Foreground(t.Palette.PlaceholderText))
                        .Text($"{(int)weight}  {weight}"),
                    // The tinted box ends where the run ends, so a weight the family cannot supply
                    // shows up as a row whose sample is exactly as wide as the one above it.
                    new Border()
                        .Padding(4, 2)
                        .CornerRadius(4)
                        .WithTheme((t, b) => b.Background(t.Palette.ContainerBackground))
                        .Child(sample)));
        }

        rows.Add(new TextBlock()
            .FontSize(ThemeFontSize.Small)
            .TextWrapping(TextWrapping.Wrap)
            .Width(520)
            .WithTheme((t, b) => b.Foreground(t.Palette.PlaceholderText))
            .BindText(Resources.InterVariable, family => family != null
                ? "Inter Variable, registered through FontResources.Register: one file with a weight axis "
                    + "supplies every row."
                : "The rows use the theme font until Inter Variable is loaded. The browser backend draws "
                    + "with the browser's fonts, so a font registered through FontResources is not used there."));

        return new StackPanel()
            .Vertical()
            .Spacing(6)
            .Children([.. rows]);
    }
}
