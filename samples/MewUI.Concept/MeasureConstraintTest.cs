using Aprillz.MewUI.Controls;

namespace Aprillz.MewUI.Concept;

// ─────────────────────────────────────────────────────────────────────────────
// Regression test for issue #173:
//   FrameworkElement.MeasureOverride must apply MinWidth/MaxWidth (and Min/MaxHeight)
//   to the constraint that is passed down to the content even when Width/Height is
//   auto (double.NaN).
//
// The bug: when Width is auto, the old code skipped clamping, so a long wrapping
// TextBlock inside a Border with MaxWidth=550 was measured with the FULL available
// width (e.g. the window width). The text therefore wrapped far wider than 550 and
// overflowed the 550-wide border.
//
// This file gives two checks:
//   1) Visual  - the exact repro from the issue, so you can SEE the text wrap inside
//                the orange 550-wide box (fixed) instead of overflowing (bug).
//   2) Asserted - measures a fresh detached repro with a huge available size, so only
//                MaxWidth can constrain it, then verifies the child wrapped within it.
// ─────────────────────────────────────────────────────────────────────────────
internal static class MeasureConstraintTest
{
    private const double MinW = 300;
    private const double MaxW = 550;

    // Long enough that, unconstrained, a single line would be far wider than MaxW.
    private const string LongText =
        "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea " +
        "commodo consequat. Sed ut perspiciatis unde omnis iste natus error sit voluptatem accusantium " +
        "doloremque laudantium, totam rem aperiam, eaque ipsa quae ab illo inventore veritatis et quasi " +
        "architecto beatae vitae dicta sunt explicabo.";

    internal static Window Create()
    {
        var status = new TextBlock
        {
            FontFamily = "Consolas",
            TextWrapping = TextWrapping.Wrap,
        };

        var window = new Window().Title("Issue #173 - Measure honors Min/MaxWidth when Width is auto");

        window.Content = new DockPanel()
            .Margin(12)
            .Spacing(12)
            .Children(
                new StackPanel()
                    .DockTop()
                    .Vertical()
                    .Spacing(6)
                    .Children(
                        new TextBlock { Text = "Border: Width=auto, MinWidth=300, MaxWidth=550, wrapping text.", FontSize = 12 },
                        status,
                        // Re-check after first paint, in case the layout pass is not complete on Loaded.
                        new Button().Content("Re-check").OnClick(() => status.Text = RunCheck())),

                // Visual repro (centered, exactly like the issue). Fixed: text wraps inside the box.
                BuildRepro(out _));

        // Run the assertion once the backend / text service is ready.
        window.OnLoaded(() => status.Text = RunCheck());

        //window.WindowSize = WindowSize.FitContentSize(900, 700);
        return window;
    }

    // Measures a fresh, detached repro with a deliberately huge available size so that ONLY
    // MinWidth/MaxWidth can constrain it, then asserts the child TextBlock wrapped within MaxWidth.
    private static string RunCheck()
    {
        var border = BuildRepro(out var child);

        // Available size is way larger than MaxWidth: a correct measure must still clamp to 550.
        border.Measure(new Size(2000, 2000));

        double childWidth = child.DesiredSize.Width;

        //   Bug : child measured at ~2000 wide (MaxWidth ignored)  -> childWidth >> 550  -> FAIL
        //   Fix : child constrained to MaxWidth minus border+padding chrome -> childWidth <= 550 -> PASS
        bool pass = childWidth <= MaxW + 0.5;

        return
            $"{(pass ? "PASS" : "FAIL")}  (issue #173)\n" +
            $"Border.MaxWidth                = {MaxW:0.#}\n" +
            $"Border.DesiredSize.Width       = {border.DesiredSize.Width:0.#}\n" +
            $"child TextBlock.DesiredSize.W  = {childWidth:0.#}   (must be <= {MaxW:0.#})\n" +
            $"child TextBlock.DesiredSize.H  = {child.DesiredSize.Height:0.#}";
    }

    // The exact element tree from the issue. Returns the inner TextBlock via 'child'.
    private static Border BuildRepro(out TextBlock child)
    {
        child = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Left,
            TextWrapping = TextWrapping.Wrap,
            FontFamily = "Times New Roman",
            Text = LongText,
            Foreground = Color.Black,
            FontSize = 14,
            FontWeight = FontWeight.Normal,
        };

        return new Border
        {
            Width = double.NaN,          // auto width: this is the case the bug missed
            Height = 200,
            MinWidth = MinW,
            MaxWidth = MaxW,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            ClipToBounds = false,
            Background = Color.Orange,
            BorderThickness = 20,
            BorderBrush = Color.White,
            CornerRadius = 10,
            Padding = 10,
            Child = child,
        };
    }
}
