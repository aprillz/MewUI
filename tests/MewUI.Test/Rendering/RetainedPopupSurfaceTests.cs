using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A popup window draws a subtree that stays arranged in its owner's coordinates. A change of one item
/// in it has to repaint that item on the popup's surface, not the whole popup, and what ends up on the
/// surface has to be what a frame drawn straight from the visuals shows.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedPopupSurfaceTests
{
    private const int WIDTH = 200;
    private const int HEIGHT = 160;

    private sealed class Tile : Control
    {
        internal Color Fill { get; set; }

        protected override Size MeasureContent(Size availableSize) => new(180, 30);

        protected override void OnRender(IGraphicsContext context) => context.FillRectangle(Bounds, Fill);
    }

    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.5)]
    public void ChangeOfOneItemInAPopupWindow_RepaintsThatItemOnly(double portalScale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var items = new StackPanel { Orientation = Orientation.Vertical };
        var tiles = new Tile[4];
        for (int index = 0; index < tiles.Length; index++)
        {
            tiles[index] = new Tile { Fill = Color.FromArgb(255, (byte)(40 + index * 40), 120, 200) };
            items.Children(tiles[index]);
        }

        // The subtree stands where its owner arranged it, far from the popup surface's own origin.
        var origin = new Point(300, 220);
        var chrome = new PopupChrome(items);
        chrome.AttachChild();
        chrome.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        chrome.Arrange(new Rect(origin.X, origin.Y, chrome.DesiredSize.Width, chrome.DesiredSize.Height));

        int pixelWidth = (int)Math.Ceiling(WIDTH * portalScale);
        int pixelHeight = (int)Math.Ceiling(HEIGHT * portalScale);
        var popupWindow = HeadlessWindow.Create(pixelWidth, pixelHeight);
        popupWindow.SetHostedPortalRoot(chrome);
        popupWindow.HostedPortalOrigin = origin;
        popupWindow.HostedPortalScale = portalScale;
        chrome.HostSurface = popupWindow;

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(pixelWidth, pixelHeight, 1.0, hasAlpha: false));
        popupWindow.RenderFrameToSurface(surface);
        popupWindow.RenderFrameToSurface(surface);
        AssertMatchesReference(factory, popupWindow, surface, pixelWidth, pixelHeight, "first frames");

        popupWindow.RetainedStatistics!.Reset();
        tiles[2].Fill = Color.FromArgb(255, 220, 60, 60);
        tiles[2].InvalidateVisual();
        popupWindow.RenderFrameToSurface(surface);

        var dirtyRect = popupWindow.LastRetainedDirtyRect;
        Assert.IsNotNull(dirtyRect, $"one item changed and the popup drew its whole frame: {popupWindow.LastWholeFrameReason}");
        double expectedTop = (tiles[2].Bounds.Y - origin.Y) * portalScale;
        Assert.IsTrue(
            dirtyRect.Value.Height <= tiles[2].Bounds.Height * portalScale + 2 && Math.Abs(dirtyRect.Value.Y - expectedTop) <= 1,
            $"the dirty region {dirtyRect} is not the changed item, expected top {expectedTop} in the popup's own coordinates");
        AssertMatchesReference(factory, popupWindow, surface, pixelWidth, pixelHeight, "after one item changed");
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface actual, int pixelWidth, int pixelHeight, string label)
    {
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(pixelWidth, pixelHeight, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)actual).GetReadOnlyPixelSpan();
        int differing = 0;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != shown[offset] || expected[offset + 1] != shown[offset + 1] || expected[offset + 2] != shown[offset + 2])
            {
                differing++;
            }
        }

        Assert.AreEqual(0, differing, $"{label}: {differing} pixels differ from a frame drawn straight from the visuals");
    }
}
