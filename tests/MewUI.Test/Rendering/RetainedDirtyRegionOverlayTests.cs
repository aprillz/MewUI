using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// The dirty region overlay reports what a frame did, so it must not change what a frame does: the same
/// changes have to record, mark dirty and count the same with it on as with it off, and showing it must
/// not make frames of its own.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedDirtyRegionOverlayTests
{
    private const int WIDTH = 320;
    private const int HEIGHT = 240;

    [TestMethod]
    public void Overlay_ChangesNeitherWhatIsRecordedNorWhatIsDirty()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        string withoutOverlay = Run(factory, overlay: false);
        string withOverlay = Run(factory, overlay: true);

        Assert.AreEqual(withoutOverlay, withOverlay);
    }

    [TestMethod]
    public void OverlayOnASurfaceThatKeepsItsFrame_ShowsAndLeavesNothingBehind()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        // A popup window presented from a bitmap of its own is handed a surface that keeps its frame.
        var button = new Button { Content = new TextBlock { Text = "Item" }, Width = 120, Height = 28 };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.AllowsTransparency = true;
        window.Background = Color.Transparent;
        window.Content = button;
        window.PerformLayout();

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: true));
        window.RenderFrameToSurface(surface);
        window.RenderFrameToSurface(surface);
        byte[] plain = Read(surface);

        window.ToggleDirtyRegionOverlay();
        button.Background = Color.FromArgb(255, 200, 60, 60);
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        byte[] marked = Read(surface);

        button.Background = Color.FromArgb(255, 60, 60, 200);
        window.ToggleDirtyRegionOverlay();
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        window.RenderFrameToSurface(surface);

        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: true));
        window.RenderReferenceFrameToSurface(reference);

        Assert.AreNotEqual(0, CountDifferences(plain, marked), "the overlay drew nothing onto a surface that keeps its frame");
        Assert.AreEqual(0, CountDifferences(Read(reference), Read(surface)), "pixels differ from the reference after the overlay was turned off: marks were left in the kept frame");
    }

    private static byte[] Read(IRenderSurface surface)
        => ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();

    private static int CountDifferences(byte[] first, byte[] second)
    {
        int differing = 0;
        for (int offset = 0; offset + 3 < first.Length; offset += 4)
        {
            if (first[offset] != second[offset] || first[offset + 1] != second[offset + 1] ||
                first[offset + 2] != second[offset + 2] || first[offset + 3] != second[offset + 3])
            {
                differing++;
            }
        }

        return differing;
    }

    private static string Run(GdiGraphicsFactory factory, bool overlay)
    {
        var first = new Button { Content = new TextBlock { Text = "First" } };
        var second = new Button { Content = new TextBlock { Text = "Second" } };
        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Margin = new Thickness(12) };
        stack.Children(first, second);

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = stack;
        window.PerformLayout();
        if (overlay)
        {
            window.ToggleDirtyRegionOverlay();
        }

        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        var log = new System.Text.StringBuilder();
        Frame(window, surface, log);
        Frame(window, surface, log);

        second.Background = Color.FromArgb(255, 200, 40, 40);
        Frame(window, surface, log);

        // Frames with nothing to do: an overlay that kept itself alive would show up here.
        Frame(window, surface, log);
        Frame(window, surface, log);

        first.Width = 140;
        Frame(window, surface, log);
        Frame(window, surface, log);

        log.Append(window.RetainedFrames);
        return log.ToString();
    }

    private static void Frame(Window window, IRenderSurface surface, System.Text.StringBuilder log)
    {
        window.PerformLayout();
        window.RetainedStatistics?.Reset();
        window.RenderFrameToSurface(surface);
        var statistics = window.RetainedStatistics!;
        log.Append(window.LastRetainedDirtyRect?.ToString() ?? "whole")
            .Append(" records=").Append(statistics.ContentRecordCount)
            .Append(" replays=").Append(statistics.ContentReplayCount)
            .Append(" areas=").Append(window.LastRetainedDirtyRects.Count)
            .AppendLine();
    }
}
