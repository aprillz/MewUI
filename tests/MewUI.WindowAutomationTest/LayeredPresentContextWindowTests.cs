using System.Reflection;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;

namespace MewUI.WindowAutomationTest;

/// <summary>
/// A transparent window whose backend presents it as a layered window draws every update through one
/// graphics context while it keeps its size, takes a new one when the size changes, and lets it go
/// when it closes. What reaches the screen has to be what a newly opened window in the same state shows.
/// </summary>
[TestClass]
public sealed class LayeredPresentContextWindowTests
{
    private const double WIDTH_DIP = 240;
    private const double HEIGHT_DIP = 400;
    private const double GROWN_WIDTH_DIP = 300;
    private const double GROWN_HEIGHT_DIP = 460;
    private const int ROW_COUNT = 16;
    private const int SETTLE_MS = 600;
    private const int STEP_MS = 60;

    private static readonly Color HIGHLIGHT = Color.FromArgb(255, 204, 228, 247);

    [TestMethod]
    public async Task TransparentWindow_KeepsOneContextUntilItsSizeChanges()
    {
        if (!OperatingSystem.IsWindows() || !RealAppSession.IsAvailable)
        {
            Assert.Inconclusive("Needs the real Win32 application loop.");
            return;
        }

        await RealAppSession.RunAsync(async () =>
        {
            var rows = new List<Border>();
            var window = NewWindow(BuildMenu(rows, highlighted: -1), WIDTH_DIP, HEIGHT_DIP);
            Window? fresh = null;
            bool windowClosed = false;
            try
            {
                window.Show();
                window.MoveTo(100, 100);
                await Task.Delay(SETTLE_MS);

                string backend = window.GraphicsFactory.Backend;
                var first = PresentContext(window);
                if (first == null)
                {
                    Assert.Inconclusive($"{backend} presents this transparent window without a layered present surface.");
                    return;
                }

                for (int step = 0; step < 8; step++)
                {
                    Highlight(rows, step);
                    await Task.Delay(STEP_MS);
                    Assert.AreSame(first, PresentContext(window), $"{backend}: update {step} drew through another context");
                }

                Assert.IsFalse(first.IsDisposed, $"{backend}: the kept context was released while the window stayed open");
                fresh = NewWindow(BuildMenu(new List<Border>(), highlighted: 7), WIDTH_DIP, HEIGHT_DIP);
                await AssertSamePixelsAsync(window, fresh, backend, "after updates at the first size");
                fresh.Close();
                fresh = null;

                window.WindowSize = WindowSize.Fixed(GROWN_WIDTH_DIP, GROWN_HEIGHT_DIP);
                await Task.Delay(SETTLE_MS);
                var grown = PresentContext(window);
                Assert.IsNotNull(grown, $"{backend}: no context drew the window after it grew");
                Assert.AreNotSame(first, grown, $"{backend}: the context of the old surface drew the grown window");
                Assert.IsTrue(first.IsDisposed, $"{backend}: the context of the old surface was not released");

                for (int step = 8; step < 12; step++)
                {
                    Highlight(rows, step);
                    await Task.Delay(STEP_MS);
                    Assert.AreSame(grown, PresentContext(window), $"{backend}: update {step} after growing drew through another context");
                }

                fresh = NewWindow(BuildMenu(new List<Border>(), highlighted: 11), GROWN_WIDTH_DIP, GROWN_HEIGHT_DIP);
                await AssertSamePixelsAsync(window, fresh, backend, "after updates at the grown size");
                fresh.Close();
                fresh = null;

                window.Close();
                windowClosed = true;
                await Task.Delay(SETTLE_MS);
                Assert.IsTrue(grown!.IsDisposed, $"{backend}: the kept context outlived its window");
            }
            finally
            {
                fresh?.Close();
                if (!windowClosed)
                {
                    window.Close();
                }
            }
        });
    }

    private static Window NewWindow(UIElement content, double width, double height) => new()
    {
        Title = "LayeredPresentContext",
        StartupLocation = WindowStartupLocation.Manual,
        AllowsTransparency = true,
        Background = Color.Transparent,
        Padding = new Thickness(0),
        WindowSize = WindowSize.Fixed(width, height),
        Content = content,
    };

    private static UIElement BuildMenu(List<Border> rows, int highlighted)
    {
        var stack = new StackPanel { Orientation = Orientation.Vertical };
        for (int index = 0; index < ROW_COUNT; index++)
        {
            var row = new Border
            {
                Padding = new Thickness(10, 3, 10, 3),
                Background = index == highlighted ? HIGHLIGHT : Color.FromArgb(255, 255, 255, 255),
                Child = new TextBlock { Text = $"Menu item {index}   Ctrl+{index}", Foreground = Color.FromArgb(255, 20, 20, 20) },
            };
            rows.Add(row);
        }

        stack.Children(rows.ToArray());
        return new Border { Background = Color.FromArgb(255, 255, 255, 255), Child = stack };
    }

    private static void Highlight(List<Border> rows, int highlighted)
    {
        for (int index = 0; index < rows.Count; index++)
        {
            rows[index].Background = index == highlighted ? HIGHLIGHT : Color.FromArgb(255, 255, 255, 255);
        }
    }

    private static GraphicsContextBase? PresentContext(Window window)
        => (GraphicsContextBase?)typeof(Window).GetField("_presentSurfaceContext", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(window);

    /// <summary>Opens the reference window beside the kept one and compares the two client areas pixel for pixel.</summary>
    private static async Task AssertSamePixelsAsync(Window kept, Window reference, string backend, string when)
    {
        reference.Show();
        reference.MoveTo(100 + GROWN_WIDTH_DIP + 60, 100);
        await Task.Delay(SETTLE_MS);

        var keptShot = ScreenCapture.OfClientArea(kept.Handle);
        var referenceShot = ScreenCapture.OfClientArea(reference.Handle);
        Assert.AreEqual(
            (referenceShot.Width, referenceShot.Height),
            (keptShot.Width, keptShot.Height),
            $"{backend}: the windows differ in size {when}");

        int differing = 0;
        int largest = 0;
        for (int y = 0; y < keptShot.Height; y++)
        {
            for (int x = 0; x < keptShot.Width; x++)
            {
                var left = keptShot.At(x, y);
                var right = referenceShot.At(x, y);
                int delta = Math.Max(Math.Abs(left.B - right.B), Math.Max(Math.Abs(left.G - right.G), Math.Abs(left.R - right.R)));
                if (delta > 0)
                {
                    differing++;
                    largest = Math.Max(largest, delta);
                }
            }
        }

        Assert.AreEqual(0, differing, $"{backend}: {differing} of {keptShot.Width * keptShot.Height} pixels differ from a newly opened window {when}, by up to {largest}");
    }
}
