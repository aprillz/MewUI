using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Concept;

// ─────────────────────────────────────────────────────────────────────────────
// Regression test for issue #199:
//   Window.FitContentSize() with an empty ListBox hung the UI thread and the window
//   never appeared.
//
// The bug: the fit-content branch of PerformLayout compared its target against the
// APPLIED client size. An empty ListBox asks for almost no height (just its border),
// the OS refuses that and clamps to its minimum window size, so applied != target
// forever. Every layout therefore re-requested the same target, and the synchronous
// WM_SIZE that SetWindowPos delivers re-entered PerformLayout -> never terminates.
//
// The fix: compare against the last REQUESTED target instead. A refused target is
// asked for exactly once and the size the OS actually applied is what gets laid out.
//
// This file gives two checks:
//   1) Liveness - the buttons open the repro windows. With the bug the first one never
//                 appears (the UI thread is stuck before the window is shown).
//   2) Asserted - measures a detached empty ListBox to show the width it really asks
//                 for. This settles the "ListBox width looks infinite and overflows"
//                 report: it never asks for infinity, it asks for ALL of whatever
//                 finite width it is offered (stretch), which is also why
//                 FitContentSize() grows the window to its max width.
// ─────────────────────────────────────────────────────────────────────────────
internal static class Issue199FitContentTest
{
    private const double PROBE = 2000;

    internal static Window Create()
    {
        var status = new TextBlock
        {
            FontFamily = "Consolas",
            TextWrapping = TextWrapping.Wrap,
        };

        var window = new Window()
            .Title("Issue #199 - FitContent settles when the OS refuses the target")
            .Resizable(820, 460);

        window.Content = new DockPanel()
            .Margin(12)
            .Spacing(12)
            .Children(
                new StackPanel()
                    .DockTop()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock
                        {
                            Text = "Each button opens its own window. With the bug, the first one never appears.",
                            FontSize = 12,
                        },
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new Button().Content("#199 repro (FitContentSize)").OnClick(OpenRepro),
                                new Button().Content("Repro, no DockPanel").OnClick(OpenReproNoDock),
                                new Button().Content("Same content, no FitContentSize").OnClick(OpenPlain),
                                new Button().Content("No DockPanel, no FitContentSize").OnClick(OpenPlainNoDock),
                                new Button().Content("Plain 1000x18 sliver + ListBox").OnClick(OpenPlainSliver),
                                new Button().Content("Refused target (10x10)").OnClick(OpenRefusedTarget),
                                new Button().Content("Grow / shrink").OnClick(OpenGrowShrink),
                                new Button().Content("Re-check").OnClick(() => status.Text = RunCheck())),
                        status));

        // Run the assertion once the backend / text service is ready.
        window.OnLoaded(() => status.Text = RunCheck());
        return window;
    }

    // Measures a detached empty ListBox twice: with a large finite width and with an infinite one.
    private static string RunCheck()
    {
        var finite = new ListBox();
        finite.Measure(new Size(PROBE, PROBE));

        var unbounded = new ListBox();
        unbounded.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

        double finiteWidth = finite.DesiredSize.Width;
        double unboundedWidth = unbounded.DesiredSize.Width;

        //   Report: "the width looks infinite" -> would mean unboundedWidth is Infinity -> FAIL
        //   Actual: an unbounded ListBox falls back to its items, so an empty one asks for ~0 -> PASS
        bool pass = double.IsFinite(unboundedWidth);
        bool takesAll = Math.Abs(finiteWidth - PROBE) < 0.5;

        return
            $"{(pass ? "PASS" : "FAIL")}  (empty ListBox never asks for infinite width)\n" +
            $"measured at {PROBE:0}x{PROBE:0} : desired = {finiteWidth:0.#} x {finite.DesiredSize.Height:0.#}" +
            $"   ({(takesAll ? "takes the WHOLE offered width (stretch)" : "sizes to its items")})\n" +
            $"measured at infinity   : desired = {unboundedWidth:0.#} x {unbounded.DesiredSize.Height:0.#}" +
            $"   (must be finite)\n" +
            "\n" +
            "A stretched ListBox asks for all the width it is offered, so FitContentSize() grows the\n" +
            "window to its max width (1000 by default). Its height, though, is only its border when the\n" +
            "list is empty - that is the tiny value the OS refuses, which is what issue #199 looped on.";
    }

    /// <summary>The literal issue #199 code.</summary>
    private static void OpenRepro()
    {
        var list = new ListBox();
        var window = new Window()
            .Title("#199 repro: empty ListBox + FitContentSize()")
            .Content(
                new DockPanel().Children(
                    list
                ))
            .FitContentSize();

        ShowAndReport(window, "#199 repro", list);
    }

    /// <summary>The issue #199 code minus the DockPanel. Must behave exactly like the repro.</summary>
    private static void OpenReproNoDock()
    {
        var list = new ListBox();
        var window = new Window()
            .Title("#199 repro without DockPanel: empty ListBox + FitContentSize()")
            .Content(list)
            .FitContentSize();

        ShowAndReport(window, "repro no-dock", list);
    }

    /// <summary>The same content without fit-content, for the reported "infinite width / overflow".</summary>
    private static void OpenPlain()
    {
        var list = new ListBox();
        var window = new Window()
            .Title("Plain resizable: empty ListBox (no FitContentSize)")
            .Resizable(500, 300)
            .Content(
                new DockPanel().Children(
                    list
                ));

        ShowAndReport(window, "plain", list);
    }

    /// <summary>Plain window, ListBox directly as content. Must behave exactly like the DockPanel one.</summary>
    private static void OpenPlainNoDock()
    {
        var list = new ListBox();
        var window = new Window()
            .Title("Plain resizable: empty ListBox, no DockPanel")
            .Resizable(500, 300)
            .Content(list);

        ShowAndReport(window, "plain no-dock", list);
    }

    /// <summary>
    /// The size fit-content lands on (1000x18), forced directly without fit-content. If this spins too,
    /// the culprit is a ListBox in a near-zero-height viewport, not the fit-content sizing.
    /// </summary>
    private static void OpenPlainSliver()
    {
        var list = new ListBox();
        var window = new Window()
            .Title("Plain resizable 1000x18 + empty ListBox (no fit-content)")
            .Resizable(1000, 18)
            .Content(
                new DockPanel().Children(
                    list
                ));

        ShowAndReport(window, "plain sliver", list);
    }

    /// <summary>Content smaller than any OS minimum, so the request is refused on both axes.</summary>
    private static void OpenRefusedTarget()
    {
        var window = new Window()
            .Title("Refused target: 10x10 content")
            .Content(new Border
            {
                Width = 10,
                Height = 10,
                Background = Color.Orange,
            })
            .FitContentSize();

        ShowAndReport(window, "refused target", null);
    }

    /// <summary>A changed target must re-fit the window exactly once per change.</summary>
    private static void OpenGrowShrink()
    {
        var box = new Border
        {
            Width = 200,
            Height = 120,
            Background = Color.Orange,
        };

        Window window = null!;
        var readout = new TextBlock { FontFamily = "Consolas", FontSize = 12 };

        void Apply(string label)
        {
            window.PerformLayout();
            string line = $"{label}: content={box.Width:0}x{box.Height:0} client={window.ClientSize.Width:0}x{window.ClientSize.Height:0}";
            readout.Text = line;
            Console.Error.WriteLine($"[#199/grow-shrink] {line}");
        }

        window = new Window()
            .Title("Grow / shrink (FitContentSize 600x600)")
            .Content(
                new StackPanel()
                    .Vertical()
                    .Spacing(8)
                    .Padding(8)
                    .Children(
                        new StackPanel()
                            .Horizontal()
                            .Spacing(8)
                            .Children(
                                new Button().Content("Wider").OnClick(() => { box.Width += 60; Apply("wider"); }),
                                new Button().Content("Narrower").OnClick(() => { box.Width = Math.Max(0, box.Width - 60); Apply("narrower"); }),
                                new Button().Content("Taller").OnClick(() => { box.Height += 40; Apply("taller"); }),
                                new Button().Content("Empty").OnClick(() => { box.Width = 0; box.Height = 0; Apply("empty"); })),
                        readout,
                        box))
            .FitContentSize(600, 600);

        ShowAndReport(window, "grow-shrink", null);
    }

    private static void ShowAndReport(Window window, string label, ListBox? list)
    {
        window.FirstFrameRendered += () =>
        {
            Console.Error.WriteLine(
                $"[#199/{label}] client={window.ClientSize.Width:0.#}x{window.ClientSize.Height:0.#}");
            if (list != null)
            {
                Console.Error.WriteLine(
                    $"[#199/{label}] listbox desired={list.DesiredSize.Width:0.#}x{list.DesiredSize.Height:0.#}" +
                    $" bounds={list.Bounds.Width:0.#}x{list.Bounds.Height:0.#}");
            }
        };

        window.Show();
    }
}
