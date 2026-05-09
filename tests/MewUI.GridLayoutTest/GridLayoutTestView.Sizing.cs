namespace Aprillz.MewUI.GridLayoutTest;

internal sealed partial class GridLayoutTestView
{
    private FrameworkElement SizingCases() =>
        new StackPanel()
            .Vertical()
            .Spacing(12)
            .Children(
                CaseCard(
                    "Implicit 1x1",
                    "Definitions omitted. Tests default star row/column and fit behavior.",
                    SampleHost(
                        new Grid().ShowGridLine()
                            .Children(
                                Cell("Single child in implicit grid").Height(60).CenterVertical()
                            )
                    )),
                CaseCard(
                    "Auto / Star / Pixel",
                    "Left auto, center star, right fixed. Tests horizontal sizing balance.",
                    SampleHost(
                        new Grid().ShowGridLine()
                            .Columns("Auto,*,80")
                            .Rows("Auto,*")
                            .Spacing(6)
                            .Children(
                                Cell("Auto").Row(0).Column(0),
                                Cell("Star content grows").Row(0).Column(1),
                                Cell("80").Row(0).Column(2),
                                Cell("Fill row 1").Row(1).Column(0).ColumnSpan(3).Height(70)
                            )
                    )),
                CaseCard(
                    "Vertical Auto / Star / Pixel",
                    "Auto row, star row, fixed row. Tests vertical distribution and child constraints.",
                    SampleHost(
                        new Grid().ShowGridLine()
                            .Columns("*")
                            .Rows("Auto,*,50")
                            .Spacing(6)
                            .Children(
                                Cell("Auto row").Row(0),
                                Cell("Star row").Row(1),
                                Cell("50 row").Row(2)
                            )
                    )),
                CaseCard(
                    "Auto + Star Width",
                    "Auto column with content and star column with wrapping content. Checks that auto stays content-sized while star takes remaining width.",
                    SampleHost(
                        new Grid().ShowGridLine()
                            .Columns("Auto,*")
                            .Rows("Auto")
                            .Spacing(6)
                            .Children(
                                Cell("Auto content").Row(0).Column(0),
                                new Border()
                                    .Row(0)
                                    .Column(1)
                                    .Padding(6)
                                    .CornerRadius(4)
                                    .Background(AccentFillColor)
                                    .BorderThickness(1)
                                    .BorderBrush(SampleBorderColor)
                                    .Child(
                                        new Label()
                                            .Text("Star column should take the remaining width and wrap this sentence instead of forcing the auto column to expand.")
                                    )
                            )
                    )),
                CaseCard(
                    "Auto + Star Height",
                    "Auto row above a star row with larger content. Checks desired height and final fill behavior.",
                    SampleHost(
                        new Grid().ShowGridLine()
                            .Columns("*")
                            .Rows("Auto,*")
                            .Spacing(6)
                            .Children(
                                Cell("Header auto row").Row(0),
                                new Border()
                                    .Row(1)
                                    .Padding(6)
                                    .CornerRadius(4)
                                    .Background(Color.FromArgb(40, 220, 120, 20))
                                    .BorderThickness(1)
                                    .BorderBrush(SampleBorderColor)
                                    .Child(
                                        new StackPanel()
                                            .Vertical()
                                            .Spacing(4)
                                            .Children(
                                                new Label().Text("Star row body"),
                                                new Border().Height(80).Background(Color.FromArgb(48, 220, 120, 20))
                                            )
                                    )
                            )
                    ))
            );
}
