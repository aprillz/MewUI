namespace Aprillz.MewUI.GridLayoutTest;

internal sealed partial class GridLayoutTestView
{
    private FrameworkElement ScrollCases() =>
        new StackPanel()
            .Vertical()
            .Spacing(12)
            .Children(
                CaseCard(
                    "Vertical Scroll",
                    "Grid inside ScrollViewer with star width. Content should stretch horizontally and create vertical extent.",
                    SampleHost(CreateVerticalScrollCase(), 220),
                    () => CreateCaseWindow("Vertical Scroll", CreateVerticalScrollCase(), WindowSize.Resizable(520, 420)).Show(_owner)),
                CaseCard(
                    "Vertical Scroll Repro",
                    "Reproduces the WrapPanel -> Grid case reported from the app. The inner grid should not collapse all children into one cell, and vertical extent should grow.",
                    SampleHost(CreateVerticalScrollReproCase(), 220),
                    () => CreateCaseWindow("Vertical Scroll Repro", CreateVerticalScrollReproCase(), WindowSize.Resizable(520, 420)).Show(_owner)),
                CaseCard(
                    "Horizontal Scroll",
                    "Grid inside horizontal ScrollViewer with star height. Content should stretch vertically and create horizontal extent.",
                    SampleHost(CreateHorizontalScrollCase(), 220),
                    () => CreateCaseWindow("Horizontal Scroll", CreateHorizontalScrollCase(), WindowSize.Resizable(520, 420)).Show(_owner))
            );

    private static FrameworkElement CreateVerticalScrollCase()
    {
        var items = Enumerable.Range(0, 24)
            .Select(i => (Element)new Border()
                .Height(34)
                .CornerRadius(4)
                .Padding(6)
                .Background(Color.FromArgb(40, 0, 120, 215))
                .BorderThickness(1)
                .BorderBrush(SampleBorderColor)
                .Child(new Label().Text($"Item {i:00}")))
            .ToArray();

        return new Grid().ShowGridLine()
            .Rows("*")
            .Columns("*")
            .Children(
                new ScrollViewer()
                    .VerticalScroll(ScrollMode.Auto)
                    .Content( 
                        new Grid().ShowGridLine()
                            .Columns("*")
                            .Rows(Enumerable.Repeat(GridLength.Auto, items.Length).ToArray())
                            .AutoIndexing()
                            .Spacing(5)
                            .Padding(5)
                            .Children(items)
                    )
            );
    }

    private static FrameworkElement CreateHorizontalScrollCase()
    {
        var items = Enumerable.Range(0, 20)
            .Select(i => (Element)new Border()
                .Width(130)
                .CornerRadius(4)
                .Padding(6)
                .Background(Color.FromArgb(40, 220, 120, 20))
                .BorderThickness(1)
                .BorderBrush(SampleBorderColor)
                .Child(new Label().Text($"Tile {i:00}")))
            .ToArray();

        return new ScrollViewer()
            .HorizontalScroll(ScrollMode.Auto)
            .VerticalScroll(ScrollMode.Disabled)
            .Content(
                new Grid().ShowGridLine()
                    .Rows("*")
                    .Columns("*")
                    .Children(
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(items)
                    )
            );
    }

    private static FrameworkElement CreateVerticalScrollReproCase()
    {
        var items = Enumerable.Range(0, 100)
            .Select(i => (Element)new Border()
                .Width(64)
                .Height(64)
                .CornerRadius(4)
                .Padding(6)
                .Background(Color.FromArgb(40, 40, 170, 90))
                .BorderThickness(1)
                .BorderBrush(SampleBorderColor)
                .Child(new Label().Text($"Image {i:00}")))
            .ToArray();

        return new Grid().ShowGridLine()
            .Rows("*")
            .Columns("*")
            .Children(
                new ScrollViewer()
                    .VerticalScroll(ScrollMode.Auto)
                    .Margin(0, 0, 2, 0)
                    .Content(
                        new WrapPanel()
                            .Spacing(5)
                            .Padding(5)
                            .Children(items)
                    )
            );
    }
}
