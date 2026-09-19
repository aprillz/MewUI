using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Bringing the scene up to date must cost what changed, not what exists: a frame in which nothing
/// changed looks at the root and stops, and a change of one visual looks along the way to it. Every
/// case still has to end with the surface showing what a frame drawn straight from the visuals shows.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedCleanSubtreeTests
{
    private const int WIDTH = 640;
    private const int HEIGHT = 480;
    private const int GROUPS = 20;
    private const int BUTTONS_PER_GROUP = 25;

    [TestMethod]
    public void UpdatingTheScene_LooksOnlyWhereSomethingChanged()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var groups = new StackPanel { Orientation = Orientation.Vertical };
        var buttons = new List<Button>();
        for (int group = 0; group < GROUPS; group++)
        {
            var row = new WrapPanel();
            for (int index = 0; index < BUTTONS_PER_GROUP; index++)
            {
                var button = new Button { Content = new TextBlock { Text = $"{group}.{index}" }, Width = 24, Height = 20 };
                buttons.Add(button);
                row.Children(button);
            }

            groups.Children(new Border { Child = row });
        }

        var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Content = groups };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = scroll;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        Frames(window, surface, 3);
        var statistics = window.RetainedStatistics!;

        statistics.Reset();
        Frames(window, surface, 1);
        Assert.IsTrue(
            statistics.CapturedNodeCount <= 2,
            $"a frame in which nothing changed looked into {statistics.CapturedNodeCount} visuals");

        statistics.Reset();
        buttons[260].Background = Color.FromArgb(255, 200, 60, 60);
        Frames(window, surface, 1);
        Assert.IsTrue(
            statistics.CapturedNodeCount <= 16,
            $"a change of one button looked into {statistics.CapturedNodeCount} visuals");
        AssertMatchesReference(factory, window, surface, "after one button changed");

        // A visual taken out and another put in change what the scene holds, under a clean sibling.
        var firstRow = (WrapPanel)((Border)groups.Children[0]).Child!;
        firstRow.RemoveAt(3);
        firstRow.Children(new Button { Content = new TextBlock { Text = "new" }, Width = 40, Height = 20 });
        Frames(window, surface, 2);
        AssertMatchesReference(factory, window, surface, "after a button was replaced");

        scroll.SetScrollOffsets(0, 37);
        Frames(window, surface, 2);
        AssertMatchesReference(factory, window, surface, "after scrolling");

        buttons[30].Background = Color.FromArgb(255, 60, 60, 200);
        Frames(window, surface, 1);
        AssertMatchesReference(factory, window, surface, "after a change that followed the scroll");

        window.SetDpi(120);
        using var scaled = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH * 5 / 4, HEIGHT * 5 / 4, 1.25, hasAlpha: false));
        Frames(window, scaled, 2);
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH * 5 / 4, HEIGHT * 5 / 4, 1.25, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        Assert.AreEqual(0, CountDifferences(reference, scaled), "after the scale changed");
    }

    private static void Frames(Window window, IRenderSurface surface, int count)
    {
        for (int index = 0; index < count; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface actual, string label)
    {
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        int differing = CountDifferences(reference, actual);
        Assert.AreEqual(0, differing, $"{label}: {differing} pixels differ from a frame drawn straight from the visuals");
    }

    private static int CountDifferences(IRenderSurface reference, IRenderSurface actual)
    {
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

        return differing;
    }
}
