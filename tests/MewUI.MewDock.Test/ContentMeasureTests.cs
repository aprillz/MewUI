using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;

using static MewUI.MewDock.Test.DockTestSupport;

namespace MewUI.MewDock.Test;

/// <summary>Pane content is measured with the size it is then arranged in, so content that takes all the room it is offered never outgrows its pane.</summary>
[TestClass]
public sealed class ContentMeasureTests
{
    private const double WIDTH = 1000;
    private const double HEIGHT = 600;

    [TestMethod]
    public void DocumentBesideADockedToolIsMeasuredAtItsArrangedSize()
    {
        var probes = new Dictionary<string, GreedyProbe>();
        var manager = LoadWithProbes(DockedTools(Tool("Explorer")), probes);

        Layout(manager);

        AssertMeasuredAtArrangedSize(probes["Document"]);
        AssertMeasuredAtArrangedSize(probes["Explorer"]);
    }

    [TestMethod]
    public void DocumentsSplitInARowAreMeasuredAtTheirShare()
    {
        var probes = new Dictionary<string, GreedyProbe>();
        var manager = LoadWithProbes("""
            {
              "layout": { "type": "row", "children": [
                { "type": "tabset", "weight": 70, "children": [ { "type": "tab", "name": "Left", "component": "document" } ] },
                { "type": "tabset", "weight": 30, "children": [ { "type": "tab", "name": "Right", "component": "document" } ] }
              ]}
            }
            """, probes);

        Layout(manager);

        AssertMeasuredAtArrangedSize(probes["Left"]);
        AssertMeasuredAtArrangedSize(probes["Right"]);
    }

    [TestMethod]
    public void RevealedAutoHideToolIsMeasuredAtItsPanelSize()
    {
        var probes = new Dictionary<string, GreedyProbe>();
        var manager = LoadWithProbes(AutoHiddenTools(Tool("Explorer")), probes);
        Pane(manager, "Explorer").Activate();

        Layout(manager);

        AssertMeasuredAtArrangedSize(probes["Document"]);
        AssertMeasuredAtArrangedSize(probes["Explorer"]);
    }

    private static DockingManager LoadWithProbes(string json, Dictionary<string, GreedyProbe> probes)
    {
        var manager = new DockingManager { ContentFactory = pane => probes[pane.Title ?? string.Empty] = new GreedyProbe() };
        manager.LoadLayout(json);
        return manager;
    }

    private static void Layout(DockingManager manager)
    {
        manager.Measure(new Size(WIDTH, HEIGHT));
        manager.Arrange(new Rect(0, 0, WIDTH, HEIGHT));
    }

    private static void AssertMeasuredAtArrangedSize(GreedyProbe probe)
    {
        Assert.IsGreaterThan(0, probe.Bounds.Width, "the content is arranged");
        Assert.AreEqual(probe.Bounds.Width, probe.Constraint.Width, 1, "measured width");
        Assert.AreEqual(probe.Bounds.Height, probe.Constraint.Height, 1, "measured height");
    }

    /// <summary>Content that wants all the room it is offered, as a text editor does.</summary>
    private sealed class GreedyProbe : FrameworkElement
    {
        public Size Constraint { get; private set; }

        protected override Size MeasureContent(Size availableSize)
        {
            Constraint = availableSize;
            return availableSize;
        }
    }
}
