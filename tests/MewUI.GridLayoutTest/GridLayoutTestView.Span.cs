namespace Aprillz.MewUI.GridLayoutTest;

internal sealed partial class GridLayoutTestView
{
    private FrameworkElement SpanCases() =>
        new StackPanel()
            .Vertical()
            .Spacing(12)
            .Children(
                CaseCard(
                    "Column Span",
                    "Tests span across auto/star/fixed columns with spacing.",
                    SampleHost(
                        new Grid().ShowGridLine()
                            .Columns("Auto,*,90")
                            .Rows("Auto,Auto,*")
                            .Spacing(8)
                            .Children(
                                Cell("A").GridPosition(0, 0),
                                Cell("B").GridPosition(0, 1),
                                Cell("C").GridPosition(0, 2),
                                Cell("Span 2 columns").GridPosition(1, 0, 1, 2),
                                Cell("Span all columns").GridPosition(2, 0, 1, 3).Height(60)
                            )
                    )),
                CaseCard(
                    "Row Span",
                    "Tests height accumulation with spacing across multiple rows.",
                    SampleHost(
                        new Grid().ShowGridLine()
                            .Columns("140,*")
                            .Rows("Auto,Auto,*")
                            .Spacing(8)
                            .Children(
                                Cell("RowSpan 2").GridPosition(0, 0, 2, 1),
                                Cell("R0C1").GridPosition(0, 1),
                                Cell("R1C1").GridPosition(1, 1),
                                Cell("Bottom").GridPosition(2, 0, 1, 2).Height(54)
                            )
                    )),
                CaseCard(
                    "Spacing Stress",
                    "Tests that gaps are counted once between definitions and inside spans.",
                    SampleHost(
                        new Grid().ShowGridLine()
                            .Columns("*,*,*")
                            .Rows("*,*")
                            .Spacing(12)
                            .Children(
                                Cell("1").GridPosition(0, 0),
                                Cell("2").GridPosition(0, 1, 1, 2),
                                Cell("3").GridPosition(1, 0, 1, 3)
                            )
                    ))
            );
}
