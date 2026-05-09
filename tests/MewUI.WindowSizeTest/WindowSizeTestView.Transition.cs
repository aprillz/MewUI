using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.WindowSizeTest;

partial class WindowSizeTestView
{
    private FrameworkElement GroupBContent() =>
        new StackPanel()
            .Vertical()
            .Spacing(8)
            .Children(
                new Button()
                    .Content("Open All (B1~B6)")
                    .OnClick(OpenAllGroupB),
                new WrapPanel()
                    .Spacing(6)
                    .Children(
                        new Button().Content("B1").OnClick(OpenB1),
                        new Button().Content("B2").OnClick(OpenB2),
                        new Button().Content("B3").OnClick(OpenB3),
                        new Button().Content("B4").OnClick(OpenB4),
                        new Button().Content("B5").OnClick(OpenB5),
                        new Button().Content("B6").OnClick(OpenB6)
                    ),
                new Label()
                    .FontSize(11)
                    .Text("B1: Resizable <-> Fixed  B2: Resizable <-> FitContentHeight\n"
                        + "B3: Fixed <-> FitContentSize  B4: Resizable <-> Resizable(diff)\n"
                        + "B5: FitContentWidth <-> FitContentHeight  B6: Fixed <-> Resizable")
            );

    private void OpenAllGroupB()
    {
        OpenB1(); OpenB2(); OpenB3(); OpenB4(); OpenB5(); OpenB6();
    }

    // B1: Resizable(400,300) <-> Fixed(400,300)
    private void OpenB1() =>
        OpenTransitionWindow("B1", "Resizable <-> Fixed",
            ("Resizable(400, 300)", () => WindowSize.Resizable(400, 300)),
            ("Fixed(400, 300)", () => WindowSize.Fixed(400, 300)));

    // B2: Resizable(400,300) <-> FitContentHeight(400, 500)
    private void OpenB2() =>
        OpenTransitionWindow("B2", "Resizable <-> FitContentHeight",
            ("Resizable(400, 300)", () => WindowSize.Resizable(400, 300)),
            ("FitContentHeight(400, 500)", () => WindowSize.FitContentHeight(400, 500)),
            SizedContent(100, 150));

    // B3: Fixed(350,250) <-> FitContentSize(400, 400)
    private void OpenB3() =>
        OpenTransitionWindow("B3", "Fixed <-> FitContentSize",
            ("Fixed(350, 250)", () => WindowSize.Fixed(350, 250)),
            ("FitContentSize(400, 400)", () => WindowSize.FitContentSize(400, 400)),
            SizedContent(120, 100));

    // B4: Resizable(400,300) <-> Resizable(600,200, min 300x100)
    private void OpenB4() =>
        OpenTransitionWindow("B4", "Resizable <-> Resizable(diff constraints)",
            ("Resizable(400, 300)", () => WindowSize.Resizable(400, 300)),
            ("Resizable(600, 200, min 300x100)", () => WindowSize.Resizable(600, 200, 300, 100)));

    // B5: FitContentWidth(200, maxW=400) <-> FitContentHeight(300, maxH=400)
    private void OpenB5() =>
        OpenTransitionWindow("B5", "FitContentWidth <-> FitContentHeight",
            ("FitContentWidth(maxW=400, fixedH=200)", () => WindowSize.FitContentWidth(400, 200)),
            ("FitContentHeight(fixedW=300, maxH=400)", () => WindowSize.FitContentHeight(300, 400)),
            SizedContent(150, 120));

    // B6: Fixed(300,200) <-> Resizable(500,400)
    private void OpenB6() =>
        OpenTransitionWindow("B6", "Fixed <-> Resizable(larger)",
            ("Fixed(300, 200)", () => WindowSize.Fixed(300, 200)),
            ("Resizable(500, 400)", () => WindowSize.Resizable(500, 400)));

    private void OpenTransitionWindow(
        string id,
        string desc,
        (string label, Func<WindowSize> create) modeA,
        (string label, Func<WindowSize> create) modeB,
        FrameworkElement? fitContent = null)
    {
        Window tw = null!;
        var modeLabel = new ObservableValue<string>($"Current: {modeA.label}");

        var children = new List<FrameworkElement>
        {
            new Label().Text($"[{id}] {desc}").Bold(),
            new Label().BindText(modeLabel).FontSize(11),
            new StackPanel()
                .Horizontal()
                .Spacing(8)
                .Children(
                    new Button()
                        .Content(modeA.label)
                        .OnClick(() =>
                        {
                            tw.WindowSize = modeA.create();
                            modeLabel.Value = $"Current: {modeA.label}";
                        }),
                    new Button()
                        .Content(modeB.label)
                        .OnClick(() =>
                        {
                            tw.WindowSize = modeB.create();
                            modeLabel.Value = $"Current: {modeB.label}";
                        })
                ),
        };

        if (fitContent != null)
            children.Add(fitContent);

        children.Add(new Button().Content("Close").OnClick(() => tw.Close()));

        var content = new StackPanel()
            .Vertical()
            .Spacing(12)
            .Children(children.ToArray());

        new Window()
            .Ref(out tw)
            .Title($"[{id}] {desc}")
            .OnBuild(x => x
                .Padding(12)
                .Content(content)
            );

        tw.WindowSize = modeA.create();
        tw.Show();
    }
}
