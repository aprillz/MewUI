namespace Aprillz.MewUI.GridLayoutTest;

internal sealed partial class GridLayoutTestView
{
    private FrameworkElement WindowCases() =>
        new StackPanel()
            .Vertical()
            .Spacing(12)
            .Children(
                CaseCard(
                    "FitContentHeight",
                    "Root grid with implicit star definition and fixed-height child. Checks fit-content regression.",
                    SampleHost(
                        new Button()
                            .Content("Open FitContentHeight Case")
                            .OnClick(OpenFitContentHeightWindow)
                    ),
                    OpenFitContentHeightWindow),
                CaseCard(
                    "FitContentSize",
                    "Grid with auto/star rows and spans inside a fit-content window.",
                    SampleHost(
                        new Button()
                            .Content("Open FitContentSize Case")
                            .OnClick(OpenFitContentSizeWindow)
                    ),
                    OpenFitContentSizeWindow)
            );

    private void OpenFitContentHeightWindow()
    {
        CreateCaseWindow(
            "FitContentHeight",
            new Grid().ShowGridLine()
                .Margin(12)
                .Children(
                    new StackPanel()
                        .Horizontal()
                        .Height(200)
                        .CenterHorizontal()
                        .CenterVertical()
                        .Spacing(8)
                        .Children(
                            new Label().Text("Password:").CenterVertical(),
                            new TextBox().Width(180).CenterVertical(),
                            new Button().Content("Enter").Width(60).CenterVertical()
                        )
                ),
            WindowSize.FitContentHeight(480, 1000))
            .Show(_owner);
    }

    private void OpenFitContentSizeWindow()
    {
        CreateCaseWindow(
            "FitContentSize",
            new Grid().ShowGridLine()
                .Margin(12)
                .Columns("Auto,*")
                .Rows("Auto,Auto,*")
                .Spacing(8)
                .Children(
                    new Label().Text("Name").Right().CenterVertical(),
                    new TextBox().Column(1).Width(180),
                    new Label().Text("Description").Right().CenterVertical().Row(1),
                    new MultiLineTextBox().GridPosition(1, 1).Width(240).Height(120),
                    new Button().Content("Save").GridPosition(2, 0, 1, 2)
                ),
            WindowSize.FitContentSize(900, 700))
            .Show(_owner);
    }
}
