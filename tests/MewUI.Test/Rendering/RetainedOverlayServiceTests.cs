using Aprillz.MewUI;
using Aprillz.MewUI.Animation;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Busy indicators and toasts are drawn over the window from its overlay layer.
/// A busy indicator starts fully transparent, where it draws nothing, and fades in. What it is made
/// of changes the moment it stops being transparent, so its ring and message have to show then, and
/// every frame of the fade has to match one drawn straight from the visuals.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedOverlayServiceTests
{
    private const int WIDTH = 400;
    private const int HEIGHT = 300;

    [TestMethod]
    public void FadingInAndOut_MatchesTheReferenceAtEveryFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new Button { Content = new TextBlock { Text = "Behind" }, Width = 120, Height = 30 };
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        var busy = window.CreateBusyIndicator("Working on it");
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        long frame = System.Diagnostics.Stopwatch.Frequency / 60;
        for (int step = 0; step <= 24; step++)
        {
            AnimationManager.Instance.UpdateAt(start + (frame * step));
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            AssertMatchesReference(factory, window, surface, $"fade step {step}");
            if (step == 6)
            {
                // Part-way in, the cover, the ring and the message fade together, as one group.
                window.RetainedStatistics!.Reset();
                window.RenderFrameToSurface(surface);
                ((Button)window.Content!).Background = Color.FromArgb(255, 200, 60, 60);
                window.PerformLayout();
                window.RenderFrameToSurface(surface);
                Assert.IsGreaterThan(0, window.RetainedStatistics.GroupSurfaceCount, "the busy indicator was not faded as a group part-way through its fade");
            }
        }

        busy.NotifyProgress("Almost there");
        window.PerformLayout();
        window.RenderFrameToSurface(surface);
        AssertMatchesReference(factory, window, surface, "after a new message");

        busy.Dispose();
        for (int step = 25; step <= 50; step++)
        {
            AnimationManager.Instance.UpdateAt(start + (frame * step));
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            AssertMatchesReference(factory, window, surface, $"fade out step {step}");
        }
    }

    [TestMethod]
    public void AToast_MatchesTheReferenceAtEveryFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new Button { Content = new TextBlock { Text = "Behind" }, Width = 120, Height = 30 };
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        window.ShowToast("Saved the file");
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        long frame = System.Diagnostics.Stopwatch.Frequency / 60;
        for (int step = 0; step <= 40; step++)
        {
            AnimationManager.Instance.UpdateAt(start + (frame * step));
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
            AssertMatchesReference(factory, window, surface, $"toast step {step}");
        }
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface surface, string label)
    {
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
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
