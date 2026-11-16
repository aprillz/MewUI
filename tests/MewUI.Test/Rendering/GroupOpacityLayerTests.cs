using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// An opacity scope fades its drawing as one group on every backend: where two fills inside it overlap,
/// the surface shows the same colour as where only one of them lies, the scope's opacity applied once.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class GroupOpacityLayerTests
{
    private const int SIZE = 60;
    private const int TOLERANCE = 3;
    private static readonly Color _red = Color.FromRgb(220, 30, 30);

    [TestMethod]
    [DataRow(TestBackend.Gdi)]
    [DataRow(TestBackend.Direct2D)]
    [DataRow(TestBackend.MewVG)]
    public void OverlappingFillsInsideAScope_FadeOnce(TestBackend backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The backends under test are Windows-only.");
            return;
        }

        using var session = TestBackendSession.Open(backend);
        using var surface = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(SIZE, SIZE, 1.0, hasAlpha: false));
        using (var context = session.Factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.Clear(Color.White);
            context.BeginOpacity(0.5);
            context.FillRectangle(new Rect(0, 0, 40, SIZE), _red);
            context.FillRectangle(new Rect(20, 0, 40, SIZE), _red);
            context.EndOpacity();
            context.EndFrame();
        }

        var single = PixelAt(surface, 10, SIZE / 2);
        var overlap = PixelAt(surface, 30, SIZE / 2);
        var outside = PixelAt(surface, 50, SIZE / 2);
        // Half the red over white.
        var expected = Color.FromRgb(238, 143, 143);
        AssertClose(expected, single, "one fill under the scope");
        AssertClose(expected, overlap, "two overlapping fills under the scope");
        AssertClose(expected, outside, "the second fill alone");
    }

    private static Color PixelAt(IRenderSurface surface, int x, int y)
    {
        ReadOnlySpan<byte> pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int offset = (y * SIZE + x) * 4;
        return Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private static void AssertClose(Color expected, Color actual, string where)
    {
        bool close = Math.Abs(expected.R - actual.R) <= TOLERANCE &&
            Math.Abs(expected.G - actual.G) <= TOLERANCE &&
            Math.Abs(expected.B - actual.B) <= TOLERANCE;
        Assert.IsTrue(close, $"{where}: expected about {expected}, found {actual}");
    }
}
