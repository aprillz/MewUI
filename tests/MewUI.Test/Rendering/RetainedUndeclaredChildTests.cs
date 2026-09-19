using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Some controls draw visuals of their own inside their own drawing instead of declaring them as
/// children: a tab strip, the segments of a segmented control, the text of a label. The scene has no
/// node for such a visual, so a change of it has to reach the drawing that holds it. Each case is held
/// against a frame drawn straight from the visuals.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedUndeclaredChildTests
{
    private const int WIDTH = 360;
    private const int HEIGHT = 240;

    [TestMethod]
    public void DisablingATab_RedrawsItsHeader()
    {
        var first = new TabItem().Header("First").Content(new TextBlock { Text = "one" });
        var second = new TabItem().Header("Second").Content(new TextBlock { Text = "two" });
        var tabs = new TabControl().TabItems(first, second);

        Check(tabs, () => second.IsEnabled = false, "after a tab was disabled");
    }

    [TestMethod]
    public void SelectingASegment_RedrawsTheSegments()
    {
        var segments = new SegmentedControl().Items("Day", "Week", "Month").SelectedIndex(0);
        segments.HorizontalAlignment = HorizontalAlignment.Left;
        segments.VerticalAlignment = VerticalAlignment.Top;

        Check(segments, () => segments.SelectedIndex = 2, "after another segment was selected");
    }

    [TestMethod]
    public void ShowingAccessKeys_RedrawsLabels()
    {
        var label = new Label().Text("_Open file");
        label.HorizontalAlignment = HorizontalAlignment.Left;
        label.VerticalAlignment = VerticalAlignment.Top;
        Window? shown = null;

        Check(label, () => shown!.ShowAccessKeys = true, "after access keys were shown", window => shown = window);
    }

    [TestMethod]
    public void CollapsingAGridRow_MovesItsGridLines()
    {
        var top = new TextBlock { Text = "top row" };
        var bottom = new Border { Height = 30, VerticalAlignment = VerticalAlignment.Bottom, Background = Color.FromArgb(255, 60, 120, 200) };
        var grid = new Grid().Rows("Auto,*");
        grid.ShowGridLine = true;
        Grid.SetRow(bottom, 1);
        grid.Children(top, bottom);

        Check(grid, () => top.IsVisible = false, "after the first row collapsed");
    }

    private static void Check(UIElement content, Action change, string label, Action<Window>? onShown = null)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = content;
        window.PerformLayout();
        onShown?.Invoke(window);

        using var surface = Surface(factory);
        Frames(window, surface, 3);
        AssertMatchesReference(factory, window, surface, "before the change");

        change();
        Frames(window, surface, 2);
        AssertMatchesReference(factory, window, surface, label);
    }

    private static IRenderSurface Surface(GdiGraphicsFactory factory)
        => factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

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
        using var reference = Surface(factory);
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
