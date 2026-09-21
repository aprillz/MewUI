using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A visual can have more than one drawing of its own: a background across its whole box and a small
/// mark drawn after its children. When only the small one changes, what is repainted is where that
/// drawing was and where it is now, not the whole box. A navigation view sliding its pane over the
/// content is the case that shows it: the separator moves while the background stays.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedSlotDirtyRegionTests
{
    private const int WIDTH = 800;
    private const int HEIGHT = 500;

    [TestMethod]
    public void PaneSlidingOverTheContent_RepaintsWhereThePaneIs()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var navigation = new NavigationView { PaneWidth = 150, PaneDisplayMode = PaneDisplayMode.Overlay, IsPaneOpen = false };
        navigation.Items(new[] { "One", "Two", "Three" }, title => title, content: title =>
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Margin = new Thickness(12) };
            for (int index = 0; index < 8; index++)
            {
                stack.Children(new Button { Content = new TextBlock { Text = title + index }, Width = 120, HorizontalAlignment = HorizontalAlignment.Left });
            }

            return stack;
        });
        navigation.SelectedIndex = 0;

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = navigation;
        window.PerformLayout();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        for (int warm = 0; warm < 3; warm++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        navigation.IsPaneOpen = true;
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        long frame = System.Diagnostics.Stopwatch.Frequency / 60;
        for (int step = 1; step <= 8; step++)
        {
            Aprillz.MewUI.Animation.AnimationManager.Instance.UpdateAt(start + (frame * step * 2));
            window.PerformLayout();
            window.RenderFrameToSurface(surface);

            var dirtyRect = window.LastRetainedDirtyRect;
            Assert.IsTrue(
                dirtyRect is Rect area && area.Right <= 260,
                $"step {step}: the pane is 150 wide and the frame repainted {dirtyRect?.ToString() ?? "everything"} ({window.LastWholeFrameReason})");
        }

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

        Assert.AreEqual(0, differing, $"{differing} pixels differ from a frame drawn straight from the visuals after the pane opened");
    }
}
