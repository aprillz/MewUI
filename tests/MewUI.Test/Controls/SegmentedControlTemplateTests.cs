using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Controls;

[TestClass]
public sealed class SegmentedControlTemplateTests
{
    private static DelegateControlTemplate<SegmentedBase> HostTemplate(Border host)
        => new((owner, ctx) =>
        {
            ctx.Register(SegmentedBase.PART_ITEMS_HOST, host);
            return host;
        });

    [TestMethod]
    public void ItemsHostPart_ReceivesTheSegmentStrip()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var host = new Border { Padding = new Thickness(0), BorderThickness = 0 };
        var window = HeadlessWindow.Create(400, 100);
        window.Padding = new Thickness(0);
        var control = new SegmentedControl().Items("Day", "Week", "Month").SelectedIndex(0);
        control.Template = HostTemplate(host);
        control.HorizontalAlignment = HorizontalAlignment.Left;
        control.VerticalAlignment = VerticalAlignment.Top;
        window.Content = control;
        window.PerformLayout();

        Assert.IsInstanceOfType<Panel>(host.Child, "the strip panel did not move into the host part");
        var panel = (Panel)host.Child!;
        Assert.AreEqual(3, panel.Count, "the segments are not in the hosted strip");
        Assert.AreEqual(host.Bounds.Left, panel[0].Bounds.Left, 0.5, "the first segment does not start at the host's edge");

        window.SendClick(((UIElement)panel[2]).CenterOf());
        Assert.AreEqual(2, control.SelectedIndex, "a click on a hosted segment did not select it");
    }

    [TestMethod]
    public void TemplatedStrip_DrawsItsOwnDividers()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var host = new Border { Padding = new Thickness(0), BorderThickness = 0 };
        var window = HeadlessWindow.Create(400, 100);
        window.Padding = new Thickness(0);
        var control = new SegmentedControl().Items("Day", "Week", "Month").SelectedIndex(0);
        control.Template = HostTemplate(host);
        window.Content = control;
        window.PerformLayout();

        var recorder = new LineRecordingContext();
        control.Render(recorder);

        Assert.AreEqual(2, recorder.Lines, "three segments need two dividers between them");
    }

    [TestMethod]
    public void ClearTemplate_RestoresTheControlAsTheStripsParent()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var host = new Border();
        var window = HeadlessWindow.Create(400, 100);
        var control = new SegmentedControl().Items("Day", "Week").SelectedIndex(0);
        control.Template = HostTemplate(host);
        window.Content = control;
        window.PerformLayout();
        var panel = (Panel)host.Child!;

        control.Template = null;
        window.PerformLayout();

        Assert.IsNull(host.Child, "the discarded host still holds the strip");
        Assert.AreSame(control, panel.Parent, "the control does not host the strip again");
    }

    [TestMethod]
    public void MissingItemsHostPart_Throws()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        var window = HeadlessWindow.Create(400, 100);
        var control = new ButtonGroup().Items("A", "B");
        control.Template = new DelegateControlTemplate<SegmentedBase>((_, _) => new Border());
        window.Content = control;

        Assert.ThrowsExactly<InvalidOperationException>(() => window.PerformLayout());
    }

    private sealed class LineRecordingContext : NoOpGraphicsContext
    {
        public int Lines { get; private set; }

        public override void DrawLine(Point start, Point end, Color color, double thickness, bool pixelSnap) => Lines++;
    }
}
