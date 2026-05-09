using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.WindowSizeTest;

partial class WindowSizeTestView
{
    private FrameworkElement GroupAContent() =>
        new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new Button()
                    .Content("Open All (A1~A10)")
                    .OnClick(OpenAllGroupA),
                new WrapPanel()
                    .Spacing(6)
                    .Children(
                        new Button().Content("A1").OnClick(OpenA1),
                        new Button().Content("A2").OnClick(OpenA2),
                        new Button().Content("A3").OnClick(OpenA3),
                        new Button().Content("A4").OnClick(OpenA4),
                        new Button().Content("A5").OnClick(OpenA5),
                        new Button().Content("A6").OnClick(OpenA6),
                        new Button().Content("A7").OnClick(OpenA7),
                        new Button().Content("A8").OnClick(OpenA8),
                        new Button().Content("A9").OnClick(OpenA9),
                        new Button().Content("A10").OnClick(OpenA10)
                    ),
                new Label()
                    .FontSize(11)
                    .Text("A1: Resizable  A2: Resizable+Constraints  A3: Fixed\n"
                        + "A4: FitContentWidth(small)  A5: FitContentWidth(max)\n"
                        + "A6: FitContentHeight(small)  A7: FitContentHeight(max)\n"
                        + "A8: FitContentSize(small)  A9: FitContentSize(max)  A10: FitContentSize(mixed)")
            );

    private void OpenAllGroupA()
    {
        OpenA1(); OpenA2(); OpenA3(); OpenA4(); OpenA5();
        OpenA6(); OpenA7(); OpenA8(); OpenA9(); OpenA10();
    }

    // A1: Resizable(400, 300)
    private void OpenA1() =>
        CreateTestWindow("A1", "Resizable(400, 300)")
            .Resizable(400, 300)
            .Show();

    // A2: Resizable with min/max constraints
    private void OpenA2() =>
        CreateTestWindow("A2", "Resizable(400, 300, min 200x150, max 600x450)")
            .Resizable(400, 300, minWidth: 200, minHeight: 150, maxWidth: 600, maxHeight: 450)
            .Show();

    // A3: Fixed(350, 250)
    private void OpenA3() =>
        CreateTestWindow("A3", "Fixed(350, 250)")
            .Fixed(350, 250)
            .Show();

    // A4: FitContentWidth — content smaller than max (should shrink)
    private void OpenA4() =>
        CreateTestWindow("A4", "FitContentWidth: 100w content, max 400", SizedContent(100, 50))
            .FitContentWidth(200, maxWidth: 400)
            .Show();

    // A5: FitContentWidth — content larger than max (should clamp)
    private void OpenA5() =>
        CreateTestWindow("A5", "FitContentWidth: 500w content, max 300", SizedContent(500, 50))
            .FitContentWidth(200, maxWidth: 300)
            .Show();

    // A6: FitContentHeight — content smaller than max (should shrink)
    private void OpenA6() =>
        CreateTestWindow("A6", "FitContentHeight: 80h content, max 400", SizedContent(100, 80))
            .FitContentHeight(300, maxHeight: 400)
            .Show();

    // A7: FitContentHeight — content larger than max (should clamp)
    private void OpenA7() =>
        CreateTestWindow("A7", "FitContentHeight: 500h content, max 200", SizedContent(100, 500))
            .FitContentHeight(300, maxHeight: 200)
            .Show();

    // A8: FitContentSize — content smaller than max (should shrink both)
    private void OpenA8() =>
        CreateTestWindow("A8", "FitContentSize: 100x80 content, max 400x400", SizedContent(100, 80))
            .FitContentSize(maxWidth: 400, maxHeight: 400)
            .Show();

    // A9: FitContentSize — content larger than max (should clamp both)
    private void OpenA9() =>
        CreateTestWindow("A9", "FitContentSize: 500x500 content, max 300x250", SizedContent(500, 500))
            .FitContentSize(maxWidth: 300, maxHeight: 250)
            .Show();

    // A10: FitContentSize — mixed (width shrinks, height clamps)
    private void OpenA10() =>
        CreateTestWindow("A10", "FitContentSize: 200x500 content, max 400x300", SizedContent(200, 500))
            .FitContentSize(maxWidth: 400, maxHeight: 300)
            .Show();
}
