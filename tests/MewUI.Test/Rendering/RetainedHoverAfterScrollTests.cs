using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Validates the pointer state after a scroll: the wheel moves the rows under a pointer that stays
/// where it is, so the hover has to go to the row that arrives there, the row that left must not keep
/// its hover picture on the kept surface, and the frame has to match one drawn straight from the visuals.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedHoverAfterScrollTests
{
    private const int SURFACE_WIDTH = 240;
    private const int SURFACE_HEIGHT = 200;
    private const double ITEM_HEIGHT = 24;
    private const int ITEM_COUNT = 80;
    private const int SAMPLE_INSET = 24;

    [TestMethod]
    [DataRow(-1.0)]
    [DataRow(-3.0)]
    public void TheHover_FollowsTheRowThatArrivesUnderThePointer(double wheelDelta)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var window = HeadlessWindow.Create(SURFACE_WIDTH, SURFACE_HEIGHT);
        var list = new ListBox { ItemHeight = ITEM_HEIGHT };
        list.ItemsSource = ItemsView.Create(Enumerable.Range(0, ITEM_COUNT).Select(index => "Item " + index).ToList());
        window.Content = list;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        var before = Container(list, 3).Bounds;
        var pointer = new Point(before.X + (before.Width / 2), before.Y + (before.Height / 2));
        window.SendMouseMove(pointer);
        Frames(window, surface, 3);
        Assert.AreEqual(3, HoveredIndex(list), "precondition: the row under the pointer is hovered");

        window.SendMouseWheel(pointer, wheelDelta);
        Frames(window, surface, 2);

        int under = IndexAt(list, pointer);
        Assert.AreNotEqual(3, under, "precondition: the wheel moved another row under the pointer");
        Assert.AreEqual(under, HoveredIndex(list), "the hover did not follow the row that arrived under the pointer");

        var palette = list.ThemeInternal.Palette;
        var hover = palette.ControlBackground.Lerp(palette.Accent, 0.15);
        var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int stride = ((ICpuPixelSurface)surface).StrideBytes;
        int hoverRows = 0;
        list.VisitRealizedContainers((index, element) =>
        {
            var bounds = element.Bounds;
            int sampleY = (int)(bounds.Y + (bounds.Height / 2));
            if (sampleY < 2 || sampleY >= SURFACE_HEIGHT - 2)
            {
                return;
            }

            int offset = (sampleY * stride) + (((int)bounds.Right - SAMPLE_INSET) * 4);
            var read = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
            if (read[offset] == hover.B && read[offset + 1] == hover.G && read[offset + 2] == hover.R)
            {
                hoverRows++;
                Assert.AreEqual(under, index, $"row {index} shows the hover picture, the pointer is over row {under}");
            }
        });
        Assert.AreEqual(1, hoverRows, "the number of rows that show the hover picture");

        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SURFACE_WIDTH, SURFACE_HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        int differing = 0;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != pixels[offset] || expected[offset + 1] != pixels[offset + 1] || expected[offset + 2] != pixels[offset + 2])
            {
                differing++;
            }
        }

        Assert.AreEqual(0, differing, $"{differing} pixels differ from a frame drawn straight from the visuals");
    }

    private static ItemContainer Container(ListBox list, int index)
    {
        ItemContainer? found = null;
        list.VisitRealizedContainers((realizedIndex, element) =>
        {
            if (realizedIndex == index && element is ItemContainer container)
            {
                found = container;
            }
        });

        Assert.IsNotNull(found, "item " + index + " has no realized ItemContainer");
        return found;
    }

    private static int HoveredIndex(ListBox list)
    {
        int hovered = -1;
        list.VisitRealizedContainers((index, element) =>
        {
            if (element is ItemContainer container && container.IsHovered)
            {
                hovered = index;
            }
        });
        return hovered;
    }

    private static int IndexAt(ListBox list, Point point)
    {
        int at = -1;
        list.VisitRealizedContainers((index, element) =>
        {
            if (element.Bounds.Contains(point))
            {
                at = index;
            }
        });
        return at;
    }

    private static void Frames(Window window, IRenderSurface surface, int count)
    {
        for (int index = 0; index < count; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }
    }
}
