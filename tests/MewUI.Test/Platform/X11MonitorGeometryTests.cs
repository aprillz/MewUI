using System.Runtime.InteropServices;
using Aprillz.MewUI;
using Aprillz.MewUI.Native;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Platform.Linux.X11;

namespace MewUI.Test.Platform;

[TestClass]
public sealed class X11MonitorGeometryTests
{
    [TestMethod]
    public void StaggeredMonitorsAndGapsSelectTheNearestDisplay()
    {
        Rect[] monitors = [new(0, 0, 1920, 1080), new(1920, -492, 1920, 1080), new(2048, 588, 1920, 1080)];
        Assert.AreEqual(monitors[0], Select(100, 100));
        Assert.AreEqual(monitors[1], Select(2200, -100));
        Assert.AreEqual(monitors[2], Select(2200, 1200));
        Assert.AreEqual(monitors[0], Select(1940, 800));
        Assert.AreEqual(monitors[2], Select(2030, 800));
        Assert.AreEqual(monitors[1], Select(1920, 0));
        Assert.AreEqual(monitors[2], Select(4000, 1800));
        Rect Select(double horizontal, double vertical)
            => X11MonitorGeometry.SelectMonitor(monitors, new Point(horizontal, vertical), default);
    }

    [TestMethod]
    public void MissingOrInvalidMonitorsUseRootBounds()
    {
        var root = new Rect(0, 0, 1920, 1080);
        Assert.AreEqual(root, X11MonitorGeometry.SelectMonitor([], new Point(100, 100), root));
        Assert.AreEqual(root, X11MonitorGeometry.SelectMonitor([new Rect(0, 0, 0, 100)], default, root));
    }

    [TestMethod]
    public void PartialPanelOnShorterMonitorDoesNotReserveTheOtherMonitor()
    {
        var root = new Rect(0, 0, 2304, 1024);
        var left = new Rect(0, 0, 1280, 1024);
        var right = new Rect(1280, 0, 1024, 768);
        long[] panel = [0, 0, 0, 306, 0, 0, 0, 0, 0, 0, 1280, 2303];
        Assert.AreEqual(left, X11MonitorGeometry.ApplyStruts(left, root, [panel]));
        Assert.AreEqual(new Rect(1280, 0, 1024, 718), X11MonitorGeometry.ApplyStruts(right, root, [panel]));
    }

    [TestMethod]
    public void OffsetTopPanelUsesRootDepthRatherThanMonitorDepth()
    {
        var root = new Rect(0, 0, 3840, 2160);
        var upper = new Rect(0, 0, 1920, 1080);
        var lower = new Rect(1920, 600, 1920, 1080);
        long[] panel = [0, 0, 640, 0, 0, 0, 0, 0, 1920, 3839, 0, 0];
        Assert.AreEqual(upper, X11MonitorGeometry.ApplyStruts(upper, root, [panel]));
        Assert.AreEqual(new Rect(1920, 640, 1920, 1040), X11MonitorGeometry.ApplyStruts(lower, root, [panel]));
    }

    [TestMethod]
    public void SidePanelsAndInclusiveRangesRespectMonitorBoundaries()
    {
        var root = new Rect(0, 0, 1920, 2160);
        var upper = new Rect(0, 0, 1920, 1080);
        var lower = new Rect(0, 1080, 1920, 1080);
        long[] leftPanel = [40, 0, 0, 0, 0, 1079, 0, 0, 0, 0, 0, 0];
        long[] rightPanel = [0, 60, 0, 0, 0, 0, 1080, 2159, 0, 0, 0, 0];
        Assert.AreEqual(new Rect(40, 0, 1880, 1080), X11MonitorGeometry.ApplyStruts(upper, root, [leftPanel, rightPanel]));
        Assert.AreEqual(new Rect(0, 1080, 1860, 1080), X11MonitorGeometry.ApplyStruts(lower, root, [leftPanel, rightPanel]));
    }

    [TestMethod]
    public void LegacyAndMultipleStrutsAreClampedAndOrderIndependent()
    {
        var root = new Rect(0, 0, 1920, 1080);
        long[] first = [30, 0, 40, 0];
        long[] second = [0, 50, 0, 60];
        var expected = new Rect(30, 40, 1840, 980);
        Assert.AreEqual(expected, X11MonitorGeometry.ApplyStruts(root, root, [first, second]));
        Assert.AreEqual(expected, X11MonitorGeometry.ApplyStruts(root, root, [second, first]));
        Assert.AreEqual(root, X11MonitorGeometry.ApplyStruts(root, root, [[0, 0, 0, 0], [1, 2]]));
        Assert.AreEqual(0, X11MonitorGeometry.ApplyStruts(root, root, [[uint.MaxValue, 0, 0, 0]]).Width);
    }

    [TestMethod]
    public void DesktopFallbackNeverEscapesSelectedMonitor()
    {
        var monitor = new Rect(1920, 600, 1920, 1080);
        Assert.AreEqual(new Rect(1920, 640, 1920, 1040),
            X11MonitorGeometry.IntersectWorkArea(monitor, new Rect(0, 640, 3840, 1520)));
        Assert.AreEqual(monitor, X11MonitorGeometry.IntersectWorkArea(monitor, default));
        Assert.AreEqual(monitor, X11MonitorGeometry.IntersectWorkArea(monitor, new Rect(0, 0, 100, 100)));
    }

    [TestMethod]
    public void PopupFlipsAndClampsInsideTheSelectedMonitorWorkArea()
    {
        var root = new Rect(0, 0, 2304, 1024);
        Rect[] monitors = [new(0, 0, 1280, 1024), new(1280, 0, 1024, 768)];
        var selected = X11MonitorGeometry.SelectMonitor(monitors, new Point(2250, 680), root);
        long[] panel = [0, 0, 0, 306, 0, 0, 0, 0, 0, 0, 1280, 2303];
        var workArea = X11MonitorGeometry.ApplyStruts(selected, root, [panel]);
        Assert.AreEqual((380.0, 300.0), PopupPlacement.ResolveVerticalPreferBelowIfFits(680, 710, workArea, 300));
        Assert.AreEqual(2004.0, PopupPlacement.ClampHorizontal(2250, 300, workArea, true));
        Assert.AreEqual((120.0, 300.0), PopupPlacement.ResolveVerticalPreferBelowIfFits(100, 120, workArea, 300));
    }

    [TestMethod]
    public void MonitorInteropMatchesXorgHeaders()
    {
        Assert.AreEqual(IntPtr.Size == 8 ? 56 : 44, Marshal.SizeOf<XRandrExt.MonitorInfo>());
        Assert.AreEqual(IntPtr.Size == 8 ? 48 : 40, Marshal.OffsetOf<XRandrExt.MonitorInfo>(nameof(XRandrExt.MonitorInfo.Outputs)).ToInt32());
        Assert.AreEqual(IntPtr.Size + 12, Marshal.OffsetOf<XRandrExt.MonitorInfo>(nameof(XRandrExt.MonitorInfo.X)).ToInt32());
        Assert.AreEqual(12, Marshal.SizeOf<XRandrExt.XineramaScreenInfo>());
        Assert.AreEqual(4, Marshal.OffsetOf<XRandrExt.XineramaScreenInfo>(nameof(XRandrExt.XineramaScreenInfo.X)).ToInt32());
    }
}
