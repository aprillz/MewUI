using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;
using Aprillz.MewUI.Rendering.Gdi;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// Aligned text has to land where a frame drawn straight from the visuals puts it, after the window is
/// resized, scrolled and its text changed, at every scale. A recording that is moved instead of taken
/// again is the risk: text centred or right-aligned inside a box moves differently from the box.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RetainedTextAlignmentTests
{
    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    public void Gdi_AlignedText_MatchesTheReferenceFrameThroughResizeAndScroll(uint dpi)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("GDI backend is Windows-only.");
            return;
        }

        using var factory = new GdiGraphicsFactory();
        Run(factory, dpi);
    }

    [TestMethod]
    [DataRow(96u)]
    [DataRow(120u)]
    [DataRow(144u)]
    public void Direct2D_AlignedText_MatchesTheReferenceFrameThroughResizeAndScroll(uint dpi)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        Run(factory, dpi);
    }

    private static void Run(IGraphicsFactory factory, uint dpi)
    {
        Application.DefaultGraphicsFactory = factory;
        double scale = dpi / 96.0;

        var centred = new TextBlock { Text = "Centred caption", TextAlignment = TextAlignment.Center };
        var right = new TextBlock { Text = "Right aligned value 1234", TextAlignment = TextAlignment.Right };
        var wrapped = new TextBlock
        {
            Text = "A longer paragraph that wraps onto several lines when the window gets narrow enough.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
        };
        var button = new Button { Content = new TextBlock { Text = "Button caption" }, HorizontalAlignment = HorizontalAlignment.Stretch };
        var narrow = new Button { Content = new TextBlock { Text = "OK" }, HorizontalAlignment = HorizontalAlignment.Right, Width = 90 };

        var stack = new StackPanel { Orientation = Orientation.Vertical, Spacing = 7 };
        stack.Children(centred, right, wrapped, button, narrow);
        for (int index = 0; index < 8; index++)
        {
            stack.Children(new TextBlock { Text = $"Row {index} with trailing text", TextAlignment = index % 2 == 0 ? TextAlignment.Right : TextAlignment.Center });
        }

        var scroll = new ScrollViewer { VerticalScroll = ScrollMode.Visible, Padding = new Thickness(9), Content = stack };
        var window = HeadlessWindow.Create(300, 200);
        window.SetDpi(dpi);
        window.Content = scroll;
        window.PerformLayout();

        (double Width, double Height, double Offset, string? Caption)[] steps =
        [
            (300, 200, 0, null),
            (300, 200, 13, null),
            (263, 200, 13, null),
            (263, 231, 41.5, "Changed caption"),
            (340, 180, 5, null),
            (340, 180, 5, "Centred again, longer than before"),
            (300, 200, 0, null),
        ];

        for (int step = 0; step < steps.Length; step++)
        {
            var (width, height, offset, caption) = steps[step];
            window.SetClientSizeDip(width, height);
            if (caption != null)
            {
                centred.Text = caption;
                ((TextBlock)button.Content!).Text = caption;
            }

            window.PerformLayout();
            scroll.SetScrollOffsets(0, offset);
            window.PerformLayout();

            int pixelWidth = (int)Math.Round(width * scale);
            int pixelHeight = (int)Math.Round(height * scale);
            using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(pixelWidth, pixelHeight, scale));
            window.RenderFrameToSurface(surface);
            using var reference = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(pixelWidth, pixelHeight, scale));
            window.RenderReferenceFrameToSurface(reference);

            ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
            ReadOnlySpan<byte> shown = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
            int differing = 0;
            int first = -1;
            for (int byteOffset = 0; byteOffset + 3 < expected.Length; byteOffset += 4)
            {
                if (expected[byteOffset] != shown[byteOffset] ||
                    expected[byteOffset + 1] != shown[byteOffset + 1] ||
                    expected[byteOffset + 2] != shown[byteOffset + 2])
                {
                    differing++;
                    if (first < 0)
                    {
                        first = byteOffset / 4;
                    }
                }
            }

            Assert.AreEqual(
                0,
                differing,
                $"{factory.Backend} at {dpi} dpi, step {step} ({width}x{height}, scroll {offset}): {differing} pixels differ, first at ({first % pixelWidth}, {first / pixelWidth})");
        }
    }
}
