using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A rectangle filled under a rotation has slanted edges, which MewVG antialiases as it does the same
/// outline filled as a path; one on the pixel grid keeps its crisp edges. Direct2D fills a rectangle
/// with a primitive of its own whose edge ramp differs from its path fill, so only MewVG is compared.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class TurnedRectangleFillTests
{
    private const int WIDTH = 160;
    private const int HEIGHT = 120;
    private const double ANGLE = 0.5;
    private const double HALF_WIDTH = 40;
    private const double HALF_HEIGHT = 12;
    private const int CHANNEL_TOLERANCE = 2;
    private static readonly Color _ink = Color.FromRgb(200, 40, 80);
    private static readonly Color _background = Color.FromRgb(250, 250, 250);

    [TestMethod]
    public void ATurnedRectangle_FillsLikeTheSameOutlineAsAPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The MewVG backend under test is the Windows one.");
            return;
        }

        using var session = TestBackendSession.Open(TestBackend.MewVG);
        var asRectangle = Render(session, new TurnedShape(asPath: false));
        var asPath = Render(session, new TurnedShape(asPath: true));

        int differing = 0;
        for (int offset = 0; offset + 3 < asPath.Length; offset += 4)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                if (Math.Abs(asRectangle[offset + channel] - asPath[offset + channel]) > CHANNEL_TOLERANCE)
                {
                    differing++;
                    break;
                }
            }
        }

        Assert.AreEqual(0, differing, $"{differing} pixels of the turned rectangle differ from the same outline filled as a path");
    }

    [TestMethod]
    public void ARectangleOnThePixelGrid_KeepsCrispEdges()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The MewVG backend under test is the Windows one.");
            return;
        }

        using var session = TestBackendSession.Open(TestBackend.MewVG);
        var pixels = Render(session, new GridRectangle(fill: true));
        var unfilled = Render(session, new GridRectangle(fill: false));

        // A pixel is either left as it was or takes the ink; anything else is a blend.
        int blended = 0;
        for (int offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            byte green = pixels[offset + 1];
            if (green != _ink.G && green != unfilled[offset + 1])
            {
                blended++;
            }
        }

        Assert.AreEqual(0, blended, $"{blended} pixels of a rectangle on the pixel grid were blended at its edges");
    }

    private static byte[] Render(TestBackendSession session, UIElement shape)
    {
        var window = HeadlessWindow.Create(WIDTH, HEIGHT);
        window.Content = new Border { Background = _background, Child = shape };
        window.PerformLayout();
        using var surface = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(WIDTH, HEIGHT, 1.0, hasAlpha: false));
        window.RenderReferenceFrameToSurface(surface);
        return ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan().ToArray();
    }

    private sealed class TurnedShape(bool asPath) : FrameworkElement
    {
        protected override void OnRender(IGraphicsContext context)
        {
            double centerX = WIDTH / 2.0;
            double centerY = HEIGHT / 2.0;
            if (asPath)
            {
                double cos = Math.Cos(ANGLE);
                double sin = Math.Sin(ANGLE);
                var path = new PathGeometry();
                ReadOnlySpan<(double X, double Y)> corners =
                [
                    (-HALF_WIDTH, -HALF_HEIGHT), (HALF_WIDTH, -HALF_HEIGHT), (HALF_WIDTH, HALF_HEIGHT), (-HALF_WIDTH, HALF_HEIGHT),
                ];
                for (int corner = 0; corner < corners.Length; corner++)
                {
                    double x = corners[corner].X * cos - corners[corner].Y * sin + centerX;
                    double y = corners[corner].X * sin + corners[corner].Y * cos + centerY;
                    if (corner == 0)
                    {
                        path.MoveTo(x, y);
                    }
                    else
                    {
                        path.LineTo(x, y);
                    }
                }

                path.Close();
                context.FillPath(path, _ink);
            }
            else
            {
                context.Save();
                context.Translate(centerX, centerY);
                context.Rotate(ANGLE);
                context.FillRectangle(new Rect(-HALF_WIDTH, -HALF_HEIGHT, HALF_WIDTH * 2, HALF_HEIGHT * 2), _ink);
                context.Restore();
            }
        }
    }

    private sealed class GridRectangle(bool fill) : FrameworkElement
    {
        protected override void OnRender(IGraphicsContext context)
        {
            // Off whole pixels, so antialiasing would blend the edge pixels it half covers.
            if (fill)
            {
                context.FillRectangle(new Rect(20.4, 30.4, 90, 40), _ink);
            }
        }
    }
}
