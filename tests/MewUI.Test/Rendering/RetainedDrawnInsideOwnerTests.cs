using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Some controls draw visuals of their own inside their own drawing: tab headers, the segments of a
/// segmented control, the bars of a scroll viewer that fade. When such a visual changes by itself (a
/// pointer over it, a fade step), the surface has to show it, as a frame drawn straight from the
/// visuals does.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedDrawnInsideOwnerTests
{
    private const int WIDTH = 360;
    private const int HEIGHT = 220;

    [TestMethod]
    public void PointerOverATabHeader_ShowsOnTheSurface()
    {
        using var factory = Start();
        var tabs = new TabControl().TabItems(
            new TabItem().Header("First").Content(new TextBlock { Text = "one" }),
            new TabItem().Header("Second").Content(new TextBlock { Text = "two" }),
            new TabItem().Header("Third").Content(new TextBlock { Text = "three" }));
        var window = Show(tabs);
        using var surface = Surface(factory);
        Check(factory, window, surface, "first frames");

        // Over the second header, which is not the selected one.
        window.SendMouseMove(new Point(tabs.Bounds.X + 90, tabs.Bounds.Y + 12));
        Check(factory, window, surface, "pointer over an unselected header");

        window.SendMouseMove(new Point(WIDTH - 4, HEIGHT - 4));
        Check(factory, window, surface, "pointer moved away");
    }

    [TestMethod]
    public void PointerOverASegment_ShowsOnTheSurface()
    {
        using var factory = Start();
        var segments = new SegmentedControl().Items("Day", "Week", "Month").SelectedIndex(0);
        segments.HorizontalAlignment = HorizontalAlignment.Left;
        segments.VerticalAlignment = VerticalAlignment.Top;
        segments.Margin = new Thickness(12);
        var window = Show(segments);
        using var surface = Surface(factory);
        Check(factory, window, surface, "first frames");

        window.SendMouseMove(new Point(segments.Bounds.Right - 14, segments.Bounds.Y + segments.Bounds.Height / 2));
        Check(factory, window, surface, "pointer over the last segment");

        window.SendClick(new Point(segments.Bounds.Right - 14, segments.Bounds.Y + segments.Bounds.Height / 2));
        Check(factory, window, surface, "after the last segment was clicked");

        window.SendMouseMove(new Point(WIDTH - 4, HEIGHT - 4));
        Check(factory, window, surface, "pointer moved away");
    }

    [TestMethod]
    public void AutoHideScrollBarFade_ShowsOnTheSurface()
    {
        using var factory = Start();
        var rows = new StackPanel { Orientation = Orientation.Vertical };
        for (int index = 0; index < 60; index++)
        {
            rows.Children(new TextBlock { Text = $"row {index}" });
        }

        var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Auto, AutoHideScrollBars = true, Content = rows };
        var window = Show(scroll);
        using var surface = Surface(factory);
        Check(factory, window, surface, "first frames");

        scroll.SetScrollOffsets(0, 60);
        Check(factory, window, surface, "right after scrolling");

        // Run the idle timer and the fade out, a frame at a time.
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        long frame = System.Diagnostics.Stopwatch.Frequency / 60;
        for (int step = 1; step <= 90; step++)
        {
            Aprillz.MewUI.Animation.AnimationManager.Instance.UpdateAt(start + (frame * step));
            if (step % 10 == 0)
            {
                Check(factory, window, surface, $"fade step {step}");
            }
        }
    }

    [TestMethod]
    public void ChangeInsideAnInSurfaceDialog_ShowsOnTheSurface()
    {
        using var factory = Start();
        var owner = Show(new TextBlock { Text = "owner content" });
        var inner = new Button { Content = new TextBlock { Text = "Inside" }, Width = 100, Height = 28 };
        var dialogContent = new StackPanel { Orientation = Orientation.Vertical, Margin = new Thickness(16) };
        dialogContent.Children(new TextBlock { Text = "dialog" }, inner);
        var dialog = HeadlessWindow.Create(200, 120);
        var host = new InSurfaceDialogHost(dialog, dialogContent, owner);
        owner.OverlayLayer.Add(host);

        using var surface = Surface(factory);
        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        long frame = System.Diagnostics.Stopwatch.Frequency / 60;
        for (int step = 0; step <= 40; step++)
        {
            Aprillz.MewUI.Animation.AnimationManager.Instance.UpdateAt(start + (frame * step));
        }

        Check(factory, owner, surface, "dialog shown");

        inner.Background = Color.FromArgb(255, 200, 60, 60);
        Check(factory, owner, surface, "after a button inside the dialog changed");

        MoveOver(owner, inner);
        Check(factory, owner, surface, "pointer over the button inside the dialog");
    }

    private static void MoveOver(Window window, UIElement element) => window.SendMouseMove(element.CenterOf());

    private static GdiGraphicsFactory Start()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;
        return factory;
    }

    private static Window Show(UIElement content)
    {
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = content;
        window.PerformLayout();
        return window;
    }

    private static IRenderSurface Surface(GdiGraphicsFactory factory)
        => factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

    private static void Check(GdiGraphicsFactory factory, Window window, IRenderSurface surface, string label)
    {
        for (int index = 0; index < 2; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        using var reference = Surface(factory);
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
