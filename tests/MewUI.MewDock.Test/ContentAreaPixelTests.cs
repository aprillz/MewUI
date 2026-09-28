using System.Reflection;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.MewDock;
using Aprillz.MewUI.MewDock.Controls;
using Aprillz.MewUI.MewDock.Extended;

using static MewUI.MewDock.Test.DockTestSupport;

namespace MewUI.MewDock.Test;

/// <summary>
/// Pane content is drawn in a layer above the frames, so its area must sit on whole device pixels inside the frame
/// border; otherwise at a fractional scale the content covers the border pixel.
/// </summary>
[TestClass]
public sealed class ContentAreaPixelTests
{
    private static readonly MethodInfo SetDpi =
        typeof(Window).GetMethod("SetDpi", BindingFlags.NonPublic | BindingFlags.Instance)!;

    [TestMethod]
    [DataRow(120u)]
    [DataRow(144u)]
    public void RevealedAutoHideContent_StaysOnWholePixelsInsideTheFrame(uint dpi)
    {
        var manager = Load(AutoHiddenTools(Tool("Explorer")));
        var window = DockWindow.Create(manager);
        SetDpi.Invoke(window, [dpi]);
        Pane(manager, "Explorer").Activate();
        window.PerformLayout();

        var bar = Descendants(manager).OfType<ExtendedBorderBar>().Single(candidate => ((IPaneContentOwner)candidate).ContentArea.Width > 0);
        var area = ((IPaneContentOwner)bar).ContentArea;
        double scale = dpi / 96.0;

        AssertOnWholePixels(area, scale);
        // The revealed frame floats off the left strip by the splitter gap, as the bar draws it.
        var panel = manager.Model!.BorderSet.Borders.Single(border => border.Children.Count > 0).ContentRect;
        double gap = manager.Model.SplitterSize;
        var frame = LayoutRounding.SnapBoundsRectToPixels(new Rect(panel.X + gap, panel.Y, Math.Max(0, panel.Width - gap), panel.Height), scale);
        // The border is as wide on every side, so the content's gap to the frame is the same on each open side.
        int leftGap = Px(area.X, scale) - Px(frame.X, scale);
        Assert.IsGreaterThanOrEqualTo(1, leftGap, "the content starts after the frame's left border pixel");
        Assert.AreEqual(leftGap, Px(frame.Right, scale) - Px(area.Right, scale), "the content stops before the right border pixels");
        Assert.AreEqual(leftGap, Px(frame.Bottom, scale) - Px(area.Bottom, scale), "the content stops before the bottom border pixels");
    }

    [TestMethod]
    [DataRow(120u)]
    [DataRow(144u)]
    public void DocumentGroupContent_StaysOnWholePixels(uint dpi)
    {
        var manager = Load(DockedTools(Tool("Explorer")));
        var window = DockWindow.Create(manager);
        SetDpi.Invoke(window, [dpi]);
        window.PerformLayout();

        var view = Descendants(manager).OfType<FlexTabSetView>().First(candidate => ((IPaneContentOwner)candidate).ContentArea.Width > 0);

        AssertOnWholePixels(((IPaneContentOwner)view).ContentArea, dpi / 96.0);
    }

    private static int Px(double dip, double scale) => (int)Math.Round(dip * scale);

    private static void AssertOnWholePixels(Rect area, double scale)
    {
        Assert.IsGreaterThan(0, area.Width);
        foreach (double edge in new[] { area.X, area.Y, area.Right, area.Bottom })
        {
            Assert.AreEqual(Math.Round(edge * scale), edge * scale, 1e-6, $"edge {edge} DIP is not on a device pixel at scale {scale}");
        }
    }
}
