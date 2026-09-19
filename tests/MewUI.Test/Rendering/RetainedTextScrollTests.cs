extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;
using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// Text inside a scrolled region: after the scroll layer has carried it, every frame must still
/// match an immediate render, with no glyph dropped and none drawn twice.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedTextScrollTests
{
    private const int WIDTH = 240;
    private const int HEIGHT = 160;

    private static (Window window, ScrollViewer scroll) CreateTextScene()
    {
        var panel = new StackPanel().Vertical().Spacing(6);
        for (int rowIndex = 0; rowIndex < 20; rowIndex++)
        {
            panel.Add(new TextBlock { Text = $"Row {rowIndex} the quick brown fox", Margin = new Thickness(8, 2, 8, 2) });
        }
        var scroll = new ScrollViewer
        {
            VerticalScroll = ScrollMode.Visible,
            Content = panel,
        };
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = scroll;
        window.PerformLayout();
        return (window, scroll);
    }

    /// <summary>Compares the frame as scrolling left it against the same state recorded from scratch.</summary>
    private static string CompareAgainstFullRecord(IGraphicsFactory factory, Window window, IRenderSurface retained)
    {
        using var rebuiltSurface = factory.CreateSurface(
            RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));
        window.Invalidate();
        window.RenderFrameToSurface(rebuiltSurface);
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)rebuiltSurface).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> actual = ((ICpuPixelSurface)retained).GetReadOnlyPixelSpan();
        int differing = 0;
        int maximumDelta = 0;
        int firstIndex = -1;
        for (int index = 0; index + 3 < expected.Length; index += 4)
        {
            int delta = Math.Max(
                Math.Abs(expected[index] - actual[index]),
                Math.Max(
                    Math.Abs(expected[index + 1] - actual[index + 1]),
                    Math.Abs(expected[index + 2] - actual[index + 2])));
            if (delta > 2)
            {
                differing++;
                maximumDelta = Math.Max(maximumDelta, delta);
                if (firstIndex < 0)
                {
                    firstIndex = index;
                }
            }
        }
        if (differing == 0)
        {
            return string.Empty;
        }

        int pixelIndex = firstIndex / 4;
        return $"{differing} pixels differ (max delta {maximumDelta}), " +
            $"first at ({pixelIndex % WIDTH},{pixelIndex / WIDTH})";
    }

    private static void AssertTextScrollMatchesImmediateFrame(IGraphicsFactory factory)
    {
        Application.DefaultGraphicsFactory = factory;
        var (window, scroll) = CreateTextScene();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(WIDTH, HEIGHT, 1));

        window.RenderFrameToSurface(surface);
        var failures = new List<string>();
        for (int roundTrip = 0; roundTrip < 2; roundTrip++)
        {
            for (double offset = 20; offset <= 200; offset += 20)
            {
                scroll.SetScrollOffsets(0, offset);
                window.PerformLayout();
                window.RenderFrameToSurface(surface);
                // Nothing here: the frames are judged once at the end, so the scroll keeps its layer.
            }
            for (double offset = 200; offset >= 0; offset -= 20)
            {
                scroll.SetScrollOffsets(0, offset);
                window.PerformLayout();
                window.RenderFrameToSurface(surface);
                // Nothing here either.
            }
        }

        string settledDifference = CompareAgainstFullRecord(factory, window, surface);
        if (settledDifference.Length != 0)
        {
            failures.Add($"settled: {settledDifference}");
        }
        Assert.IsEmpty(failures, string.Join(" | ", failures.Take(4)));
    }

    [TestMethod]
    public void Direct2DWindowComparisonPath_TextScrollMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The Direct2D retained text scroll comparison is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        AssertTextScrollMatchesImmediateFrame(factory);
    }

    [TestMethod]
    public void WindowComparisonPath_TextScrollMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The retained text scroll comparison uses the Windows GDI factory.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        AssertTextScrollMatchesImmediateFrame(factory);
    }

    [TestMethod]
    public void MewVGWin32WindowComparisonPath_TextScrollMatchesImmediateFrame()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The MewVG Win32 retained text scroll comparison is Windows-only.");
            return;
        }

        using var factory = new MewVGWin32GraphicsFactory();
        using var renderScope = factory.AcquireBackgroundRenderScope();
        AssertTextScrollMatchesImmediateFrame(factory);
    }
}
