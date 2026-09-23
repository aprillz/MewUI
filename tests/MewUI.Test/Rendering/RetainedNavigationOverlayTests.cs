using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// The overlay pane of a narrow NavigationView slides over the content and draws its separator along
/// its right edge. Opening it, picking a page and letting it slide away must leave nothing of the
/// separator behind, however the frames fall across the slide.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedNavigationOverlayTests
{
    private const int WIDTH = 360;
    private const int HEIGHT = 420;

    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    [DataRow(4)]
    [DataRow(7)]
    public void OverlayOpenedAndDismissedByAPick_LeavesNoSeparatorBehind(int framesPerStep)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var navigation = new NavigationView { PaneWidth = 220, PaneDisplayMode = PaneDisplayMode.Overlay, IsPaneOpen = false };
        navigation.Items(new[] { "Buttons", "Toggles", "Text Input", "Range" }, title => title, content: title =>
        {
            var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8, Margin = new Thickness(16) };
            stack.Children(new TextBlock { Text = title, FontSize = 20 });
            for (int index = 0; index < 6; index++)
            {
                stack.Children(new TextBox { Text = title + " field " + index });
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

        long start = System.Diagnostics.Stopwatch.GetTimestamp();
        long frame = System.Diagnostics.Stopwatch.Frequency / 60;
        int step = 0;
        void Run(string state, int count)
        {
            for (int index = 0; index < count; index++)
            {
                step += framesPerStep;
                Aprillz.MewUI.Animation.AnimationManager.Instance.UpdateAt(start + (frame * step));
                window.UpdateVisualStates();
                window.PerformLayout();
                window.RenderFrameToSurface(surface);
                AssertMatchesReference(factory, window, surface, $"{state}, frame {index + 1}, {framesPerStep} frames per step");
            }
        }

        navigation.IsPaneOpen = true;
        Run("opening", 16);

        // A pick dismisses the overlay, as tapping a row does.
        navigation.SelectedIndex = 2;
        navigation.IsPaneOpen = false;
        Run("closing after a pick", 16);
    }

    private static void AssertMatchesReference(GdiGraphicsFactory factory, Window window, IRenderSurface actual, string label)
    {
        using var reference = factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(reference);
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)actual).GetReadOnlyPixelSpan();
        int differing = 0;
        int minX = int.MaxValue, minY = int.MaxValue, maxX = -1, maxY = -1;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (expected[offset] != shown[offset] || expected[offset + 1] != shown[offset + 1] || expected[offset + 2] != shown[offset + 2])
            {
                differing++;
                int pixel = offset / 4;
                minX = Math.Min(minX, pixel % WIDTH);
                maxX = Math.Max(maxX, pixel % WIDTH);
                minY = Math.Min(minY, pixel / WIDTH);
                maxY = Math.Max(maxY, pixel / WIDTH);
            }
        }

        Assert.AreEqual(
            0,
            differing,
            $"{label}: {differing} pixels differ from a frame drawn straight from the visuals, inside ({minX},{minY})-({maxX},{maxY}); dirty region {string.Join(" ", window.LastRetainedDirtyRects)}");
    }
}
