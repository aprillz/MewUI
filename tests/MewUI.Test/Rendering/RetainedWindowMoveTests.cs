using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A visual taken out of one window and put into another leaves the first window's scene and joins the
/// second's. Both windows then have to show what frames drawn straight from their visuals show, also
/// after the visual changes in its new place and after it is moved back.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedWindowMoveTests
{
    private const int WIDTH = 260;
    private const int HEIGHT = 180;

    [TestMethod]
    public void VisualMovedBetweenWindows_IsDrawnOnlyWhereItNowLives()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
        }

        using var factory = new GdiGraphicsFactory();
        Application.DefaultGraphicsFactory = factory;

        var traveller = new Button { Content = new TextBlock { Text = "Traveller" }, Width = 110, Height = 30 };
        var firstStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        firstStack.Children(new TextBlock { Text = "first window" }, traveller);
        var secondStack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 8 };
        secondStack.Children(new TextBlock { Text = "second window" });

        var first = HeadlessWindow.Create(WIDTH, HEIGHT);
        first.Content = firstStack;
        var second = HeadlessWindow.Create(WIDTH, HEIGHT);
        second.Content = secondStack;

        using var firstSurface = Surface(factory);
        using var secondSurface = Surface(factory);
        Check(factory, first, firstSurface, "first window before the move");
        Check(factory, second, secondSurface, "second window before the move");

        firstStack.Remove(traveller);
        secondStack.Children(traveller);
        Check(factory, first, firstSurface, "first window after the visual left");
        Check(factory, second, secondSurface, "second window after the visual arrived");

        traveller.Background = Color.FromArgb(255, 200, 60, 60);
        Check(factory, second, secondSurface, "second window after the visual changed there");
        var leftBehind = Check(factory, first, firstSurface, "first window after a change that is no longer its own");
        Assert.AreEqual(default(Rect), leftBehind, "the window the visual left repainted for its change");

        secondStack.Remove(traveller);
        firstStack.Children(traveller);
        Check(factory, first, firstSurface, "first window after the visual came back");
        Check(factory, second, secondSurface, "second window after the visual left again");
    }

    private static IRenderSurface Surface(GdiGraphicsFactory factory)
        => factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));

    /// <summary>Draws two frames, holds the result against the reference, and returns what the last frame repainted.</summary>
    private static Rect? Check(GdiGraphicsFactory factory, Window window, IRenderSurface surface, string label)
    {
        for (int index = 0; index < 2; index++)
        {
            window.PerformLayout();
            window.RenderFrameToSurface(surface);
        }

        var dirtyRect = window.LastRetainedDirtyRect;

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
        return dirtyRect;
    }
}
