using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

// Shared state and section helpers for the sample gallery. The per-category sample builders live in
// the Samples.*.cs partial files; Program.cs composes them into the gallery.
internal static partial class Samples
{
    // Shared RNG for the randomized/real-time samples.
    internal static readonly Random Random = new();

    // Keeps the live-update timers rooted for the lifetime of the app (otherwise they'd be collected).
    internal static readonly List<DispatcherTimer> Timers = [];

    // A titled cell: title + main element (a single chart, or a WithActions/WithControls/Charts wrapper)
    // sized to the common cell; samples whose legend and axes need more room pass a larger size.
    internal static StackPanel Section(string title, FrameworkElement content, double width = CELL_WIDTH, double height = CELL_HEIGHT) =>
        new StackPanel()
            .Vertical()
            .Spacing(6)
            .Children(
                new TextBlock().Text(title),
                content.Width(width).Height(height).Center());

    // A "main element" that is a chart with a row of action buttons beneath it (for the interactive
    // event/button samples); the buttons wrap and the chart takes the height they leave in the cell.
    internal static FrameworkElement WithActions(FrameworkElement chart, params (string Label, Action OnClick)[] actions)
    {
        var buttons = new FrameworkElement[actions.Length];
        for (int index = 0; index < actions.Length; index++)
        {
            var button = new Button().Content(actions[index].Label);
            button.Click += actions[index].OnClick;
            buttons[index] = button;
        }

        return WithControls(chart, buttons);
    }

    // A "main element" that is a chart with a row of arbitrary controls (buttons, combo boxes, sliders,
    // check boxes) beneath it; the controls wrap and the chart takes the height they leave in the cell.
    internal static FrameworkElement WithControls(FrameworkElement chart, params FrameworkElement[] controls) =>
        new DockPanel()
            .Spacing(4)
            .Children(new WrapPanel().Spacing(4).DockBottom().Children(controls), chart);

    // A "main element" that stacks several charts into one cell (Axes/Shared, General/Scrollable);
    // the charts share the cell that Section sizes around this wrapper.
    internal static FrameworkElement Charts(params FrameworkElement[] charts) =>
        new UniformGrid { Columns = 1 }.Spacing(4).Children(charts);

    internal const double CELL_WIDTH = 300;
    internal const double CELL_HEIGHT = 200;

    // A double-width cell: two common cells plus the gallery spacing between them.
    internal const double WIDE_CELL_WIDTH = CELL_WIDTH * 2 + 16;
}
