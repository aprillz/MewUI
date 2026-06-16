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

    // A titled cell: title + main element. The main element gets the common cell size + cache,
    // whatever it is (a single chart, a Charts(...) multi-chart wrapper, or a WithActions(...)
    // chart+buttons wrapper). Caching the wrapper is safe: a child's InvalidateVisual propagates up
    // (Element.InvalidateVisual -> Parent.InvalidateVisual) and busts the cached ancestor.
    internal static StackPanel Section(string title, FrameworkElement content) =>
        new StackPanel()
            .Vertical()
            .Spacing(6)
            .Children(
                new TextBlock().Text(title),
                ConfigureContent(content));

    // A "main element" that is a chart with a row of action buttons beneath it (for the interactive
    // event/button samples). The chart takes a fixed height so it and the buttons fit the cell that
    // Section sizes around this wrapper.
    internal static FrameworkElement WithActions(FrameworkElement chart, params (string Label, Action OnClick)[] actions)
    {
        var buttons = new StackPanel().Horizontal().Spacing(6);
        foreach (var (label, onClick) in actions)
        {
            var button = new Button().Content(label);
            button.Click += onClick;
            buttons.Children(button);
        }

        return new StackPanel()
            .Vertical()
            .Spacing(4)
            .Children(chart.Height(165).StretchHorizontal(), buttons);
    }

    // A "main element" that stacks several charts into one cell (Axes/Shared, General/Scrollable);
    // the charts share the cell that Section sizes around this wrapper.
    internal static FrameworkElement Charts(params FrameworkElement[] charts) =>
        new UniformGrid().Column(1).Spacing(4).Children(charts);

    private static FrameworkElement ConfigureContent(FrameworkElement content) =>
        content.Width(300).Height(200).Cached().Center();
}
