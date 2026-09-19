using Aprillz.MewUI;
using Aprillz.MewUI.Controls;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A window presents its frame either straight into its target or through a surface that keeps its
/// contents. Both have to put the same pixels on screen. Text is where they can part: a glyph baseline
/// snapped on one surface and not the other, or a different antialiasing mode, moves or reweights every
/// line of text in the window.
/// </summary>
[TestClass]
public sealed class PresentationPathTextTests
{
    [TestMethod]
    public Task Text_LooksTheSameThroughTheFrameSurfaceAndStraightIntoTheTarget() => CaptureScene.RunAsync(async scene =>
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Reads the screen back through the Windows capture helper.");
        }

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 6, Margin = new Thickness(12) };
        double[] sizes = [11, 12, 13, 14, 16, 20];
        foreach (double size in sizes)
        {
            stack.Children(new TextBlock { Text = $"Baseline sample Hxgy {size}", FontSize = size });
        }

        stack.Children(
            new Button { Content = new TextBlock { Text = "Button caption" }, HorizontalAlignment = HorizontalAlignment.Left },
            new CheckBox { Content = new TextBlock { Text = "Check box caption" } });

        var window = await scene.ShowAsync(stack);
        await scene.Input.MoveAsync(window, CaptureScene.Away(window));
        await Task.Delay(500);

        bool previous = Window.PresentWithoutFrameSurface;
        try
        {
            Window.PresentWithoutFrameSurface = false;
            window.InvalidateVisual();
            await Task.Delay(400);
            var throughFrameSurface = ScreenCapture.OfClientArea(window.Handle);

            Window.PresentWithoutFrameSurface = true;
            window.InvalidateVisual();
            await Task.Delay(400);
            var straight = ScreenCapture.OfClientArea(window.Handle);

            Assert.AreEqual(straight.Width, throughFrameSurface.Width, "the two captures differ in width");
            Assert.AreEqual(straight.Height, throughFrameSurface.Height, "the two captures differ in height");

            int differing = CountDifferences(straight, throughFrameSurface, shiftY: 0);
            if (differing > 0)
            {
                // Says whether the text moved as a whole, and by how much, or was only weighted differently.
                int bestShift = 0;
                int bestCount = differing;
                for (int shift = -3; shift <= 3; shift++)
                {
                    int count = CountDifferences(straight, throughFrameSurface, shift);
                    if (count < bestCount)
                    {
                        bestCount = count;
                        bestShift = shift;
                    }
                }

                Assert.Fail(
                    $"{window.GraphicsFactory.Backend} at scale {window.DpiScale}: {differing} of {straight.Width * straight.Height} pixels differ " +
                    $"between the two ways of presenting; shifting the frame-surface capture by {bestShift} px vertically leaves {bestCount}");
            }
        }
        finally
        {
            Window.PresentWithoutFrameSurface = previous;
        }
    });

    private static int CountDifferences(ScreenCapture expected, ScreenCapture actual, int shiftY)
    {
        int count = 0;
        for (int y = 3; y < expected.Height - 3; y++)
        {
            for (int x = 0; x < expected.Width; x++)
            {
                var first = expected.At(x, y);
                var second = actual.At(x, y + shiftY);
                if (first.B != second.B || first.G != second.G || first.R != second.R)
                {
                    count++;
                }
            }
        }

        return count;
    }
}
