using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
using Aprillz.MewUI.MewDock.Controls;

using static MewUI.MewDock.Test.DockTestSupport;

namespace MewUI.MewDock.Test;

/// <summary>A selected tab past the strip's end is shown in full, with the leading tabs cut back until it fits.</summary>
[TestClass]
public sealed class TabOverflowTests
{
    private const double NARROW = 40;
    private const double WIDE = 200;

    [TestMethod]
    public void SelectedWideTab_PastTheCutoff_DoesNotRunUnderTheStripButtons()
    {
        var manager = new DockingManager
        {
            HeaderFactory = pane => new FixedHeader(pane.Title == "Wide" ? WIDE : NARROW),
        };
        manager.LoadLayout(Documents("T1", "T2", "T3", "T4", "T5", "T6", "T7", "T8", "Wide"));
        var window = DockWindow.Create(manager, width: 420, height: 300);

        Pane(manager, "Wide").Activate();
        window.PerformLayout();

        var view = Descendants(manager).OfType<FlexTabSetView>().Single();
        var shown = Descendants(view).OfType<FlexTabButton>().Where(tab => tab.Bounds.Width > 0).ToList();
        var overflow = Descendants(view).OfType<Button>()
            .Single(button => button is not FlexTabButton && button.Content is GlyphElement { Kind: GlyphKind.ChevronDown });

        Assert.IsTrue(shown.Any(tab => tab.Tab.Name == "Wide"), "the selected tab is shown");
        Assert.IsGreaterThan(0, overflow.Bounds.Width, "the strip overflows, so the dropdown is shown");
        Assert.IsLessThanOrEqualTo(overflow.Bounds.X, shown.Max(tab => tab.Bounds.Right), "no shown tab reaches the dropdown");
    }

    private static string Documents(params string[] names) => $$"""
        {
          "layout": { "type": "row", "children": [
            { "type": "tabset", "children": [ {{string.Join(", ", names.Select(name => $$"""{ "type": "tab", "name": "{{name}}", "component": "document" }"""))}} ] }
          ]}
        }
        """;

    private sealed class FixedHeader(double width) : FrameworkElement
    {
        protected override Size MeasureContent(Size availableSize) => new(width, 16);
    }
}
