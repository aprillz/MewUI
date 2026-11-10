using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A rectangle filled or stroked under a rotation has slanted edges, which MewVG antialiases as it does the
/// same outline drawn as a path; one on the pixel grid keeps its crisp edges. Direct2D draws a rectangle
/// with a primitive of its own whose edge ramp differs from its path, so only MewVG is compared.
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
    private const double STROKE = 3;
    private const int CHANNEL_TOLERANCE = 2;
    private static readonly Color _ink = Color.FromRgb(200, 40, 80);
    private static readonly Color _background = Color.FromRgb(250, 250, 250);
    private static readonly Rect _rectangle = new(-HALF_WIDTH, -HALF_HEIGHT, HALF_WIDTH * 2, HALF_HEIGHT * 2);

    public enum Figure
    {
        ColorFill,
        ColorStroke,
        PenStroke,
    }

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

        int differing = CountDiffering(asRectangle, asPath);
        Assert.AreEqual(0, differing, $"{differing} pixels of the turned rectangle differ from the same outline filled as a path");
    }

    [TestMethod]
    public void ATurnedRectangle_FillsAGradientLikeTheSameOutlineAsAPath()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The MewVG backend under test is the Windows one.");
            return;
        }

        using var session = TestBackendSession.Open(TestBackend.MewVG);
        var asRectangle = Render(session, new TurnedGradient(asPath: false));
        var asPath = Render(session, new TurnedGradient(asPath: true));

        int differing = CountDiffering(asRectangle, asPath);
        Assert.AreEqual(0, differing, $"{differing} pixels of the turned gradient rectangle differ from the same outline filled as a path");
    }

    [TestMethod]
    [DataRow(Figure.ColorStroke)]
    [DataRow(Figure.PenStroke)]
    public void ATurnedRectangle_StrokesWithBlendedEdges(Figure figure)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The MewVG backend under test is the Windows one.");
            return;
        }

        using var session = TestBackendSession.Open(TestBackend.MewVG);
        var pixels = Render(session, new SolidRectangle(figure, turned: true));
        var undrawn = Render(session, new SolidRectangle(null, turned: true));

        // Four slanted edges, inside and outside the stroke, each a few hundred pixels long.
        int blended = CountBlended(pixels, undrawn);
        Assert.IsGreaterThan(200, blended, "the edges of a stroke under a rotation were not antialiased");
    }

    [TestMethod]
    [DataRow(Figure.ColorFill)]
    [DataRow(Figure.ColorStroke)]
    [DataRow(Figure.PenStroke)]
    public void ARectangleOnThePixelGrid_KeepsCrispEdges(Figure figure)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The MewVG backend under test is the Windows one.");
            return;
        }

        using var session = TestBackendSession.Open(TestBackend.MewVG);
        var pixels = Render(session, new SolidRectangle(figure, turned: false));
        var undrawn = Render(session, new SolidRectangle(null, turned: false));

        int blended = CountBlended(pixels, undrawn);
        Assert.AreEqual(0, blended, $"{blended} pixels of a rectangle on the pixel grid were blended at its edges");
    }

    private static int CountDiffering(byte[] left, byte[] right)
    {
        int differing = 0;
        for (int offset = 0; offset + 3 < right.Length; offset += 4)
        {
            for (int channel = 0; channel < 3; channel++)
            {
                if (Math.Abs(left[offset + channel] - right[offset + channel]) > CHANNEL_TOLERANCE)
                {
                    differing++;
                    break;
                }
            }
        }

        return differing;
    }

    /// <summary>Counts the pixels that neither kept what was there nor took the ink: anything else is a blend.</summary>
    private static int CountBlended(byte[] pixels, byte[] undrawn)
    {
        int blended = 0;
        for (int offset = 0; offset + 3 < pixels.Length; offset += 4)
        {
            byte green = pixels[offset + 1];
            if (green != _ink.G && green != undrawn[offset + 1])
            {
                blended++;
            }
        }

        return blended;
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
                context.FillRectangle(_rectangle, _ink);
                context.Restore();
            }
        }
    }

    private sealed class TurnedGradient(bool asPath) : FrameworkElement
    {
        private static readonly LinearGradientBrush _gradient = new(
            new Point(0, 0),
            new Point(1, 0),
            [new GradientStop(0, Color.FromRgb(200, 40, 80)), new GradientStop(1, Color.FromRgb(40, 80, 200))],
            units: GradientUnits.ObjectBoundingBox);

        protected override void OnRender(IGraphicsContext context)
        {
            // Both under the same rotation, so the gradient runs along the same axis in each.
            context.Save();
            context.Translate(WIDTH / 2.0, HEIGHT / 2.0);
            context.Rotate(ANGLE);
            if (asPath)
            {
                context.FillPath(PathGeometry.FromRect(_rectangle), _gradient);
            }
            else
            {
                context.FillRectangle(_rectangle, _gradient);
            }

            context.Restore();
        }
    }

    private sealed class SolidRectangle(Figure? figure, bool turned) : FrameworkElement
    {
        protected override void OnRender(IGraphicsContext context)
        {
            context.Save();
            Rect rectangle;
            if (turned)
            {
                context.Translate(WIDTH / 2.0, HEIGHT / 2.0);
                context.Rotate(ANGLE);
                rectangle = _rectangle;
            }
            else
            {
                // Off whole pixels, so antialiasing would blend the edge pixels it half covers.
                rectangle = new Rect(20.4, 30.4, 90, 40);
            }

            switch (figure)
            {
                case Figure.ColorFill:
                    context.FillRectangle(rectangle, _ink);
                    break;
                case Figure.ColorStroke:
                    context.DrawRectangle(rectangle, _ink, STROKE);
                    break;
                case Figure.PenStroke:
                    context.DrawRectangle(rectangle, new Pen(_ink, STROKE));
                    break;
            }

            context.Restore();
        }
    }
}
