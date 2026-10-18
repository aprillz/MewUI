using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A child that fills a rounded border's corner under <see cref="UIElement.ClipToBounds"/> meets the border stroke
/// without a seam: the frame's background never shows between the two (issue #277).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class RoundedBorderClipTests
{
    private const int WIDTH = 240;
    private const int HEIGHT = 180;
    private const double FRAME_WIDTH = 200;
    private const double FRAME_HEIGHT = 140;
    // The top-right corner region the hover child and the arc share, in pixels from the frame's corner.
    private const int CORNER_BOX = 14;
    // At 45 degrees a 1px stroke does not quite cover the pixel on its centre line, so about one level of the frame
    // still shows there (Direct2D and MewVG measure 1.3). The seam this guards against sat 5 to 6 levels below.
    private const double LUMINANCE_TOLERANCE = 2.0;

    private static readonly Color _windowBackground = Color.FromRgb(0x3A, 0x27, 0x4C);
    private static readonly Color _frameBackground = Color.FromRgb(0x14, 0x14, 0x14);
    private static readonly Color _borderColor = Color.FromRgb(0x2C, 0x2C, 0x2C);
    private static readonly Color _hoverFill = Color.FromArgb(0x2E, 0xF8, 0x71, 0x71);

    [TestMethod]
    [DataRow(TestBackend.Gdi)]
    [DataRow(TestBackend.Direct2D)]
    [DataRow(TestBackend.MewVG)]
    public void ACornerFillingChild_MeetsTheBorderWithoutASeam(TestBackend backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The backends under test are Windows-only.");
            return;
        }

        using var session = TestBackendSession.Open(backend);
        var hover = new Border
        {
            Background = _hoverFill,
            Width = 46,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var frame = new Border
        {
            CornerRadius = 10,
            BorderThickness = 1,
            BorderBrush = _borderColor,
            Background = _frameBackground,
            ClipToBounds = true,
            Width = FRAME_WIDTH,
            Height = FRAME_HEIGHT,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Child = new Grid().Children(hover),
        };

        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new Border { Background = _windowBackground, Child = frame };
        window.PerformLayout();

        using var direct = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(direct);
        using var retained = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderFrameToSurface(retained);
        window.RenderFrameToSurface(retained);

        // The hover fill over the frame background, and the stroke: no pixel between them may be darker than both.
        var child = Blend(_hoverFill, _frameBackground);
        double floor = Math.Min(Luminance(child), Luminance(_borderColor)) - LUMINANCE_TOLERANCE;
        int right = (int)Math.Round(frame.Bounds.Right);
        int top = (int)Math.Round(frame.Bounds.Y);

        AssertNoSeam(direct, right, top, floor, $"{backend}, drawn straight");
        AssertNoSeam(retained, right, top, floor, $"{backend}, replayed from the retained scene");
    }

    private static void AssertNoSeam(IRenderSurface surface, int right, int top, double floor, string label)
    {
        ReadOnlySpan<byte> pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        double darkest = double.MaxValue;
        int darkestX = -1;
        int darkestY = -1;
        for (int y = top; y < top + CORNER_BOX; y++)
        {
            for (int x = right - CORNER_BOX; x < right; x++)
            {
                int offset = (y * WIDTH + x) * 4;
                double luminance = Luminance(Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]));
                if (luminance < darkest)
                {
                    darkest = luminance;
                    darkestX = x;
                    darkestY = y;
                }
            }
        }

        Assert.IsGreaterThanOrEqualTo(floor, darkest,
            $"{label}: pixel ({darkestX}, {darkestY}) has luminance {darkest:0.0}, below both the child and the border ({floor + LUMINANCE_TOLERANCE:0.0})");
    }

    private static Color Blend(Color over, Color under)
    {
        double alpha = over.A / 255.0;
        return Color.FromRgb(
            (byte)Math.Round(over.R * alpha + under.R * (1 - alpha)),
            (byte)Math.Round(over.G * alpha + under.G * (1 - alpha)),
            (byte)Math.Round(over.B * alpha + under.B * (1 - alpha)));
    }

    private static double Luminance(Color color) => 0.2126 * color.R + 0.7152 * color.G + 0.0722 * color.B;
}
