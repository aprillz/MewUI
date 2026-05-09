namespace Aprillz.MewUI.GridLayoutTest;

internal sealed partial class GridLayoutTestView
{
    private FrameworkElement AutoIndexingCases() =>
        new StackPanel()
            .Vertical()
            .Spacing(12)
            .Children(
                CaseCard(
                    "Automatic Placement",
                    "Children without row/column should fill left-to-right, top-to-bottom.",
                    SampleHost(
                        new Grid().ShowGridLine()
                            .Columns("*,*,*")
                            .Rows("Auto,Auto,Auto")
                            .Spacing(6)
                            .AutoIndexing()
                            .Children(
                                Cell("0"),
                                Cell("1"),
                                Cell("2"),
                                Cell("3"),
                                Cell("4"),
                                Cell("5")
                            )
                    )),
                CaseCard(
                    "Mixed Explicit And Auto",
                    "Explicit cells reserve space before auto placement.",
                    SampleHost(
                        new Grid().ShowGridLine()
                            .Columns("*,*,*")
                            .Rows("Auto,Auto,Auto")
                            .Spacing(6)
                            .AutoIndexing()
                            .Children(
                                Cell("Pinned").GridPosition(0, 1),
                                Cell("Span").GridPosition(1, 0, 1, 2),
                                Cell("Auto 0"),
                                Cell("Auto 1"),
                                Cell("Auto 2"),
                                Cell("Auto 3")
                            )
                    ))
            );
}
