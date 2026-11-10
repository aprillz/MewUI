using Aprillz.MewUI;
using Aprillz.MewUI.Controls;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

namespace MewUI.Test.Rendering;

/// <summary>
/// A frame drawn to a surface whose scale is below 1 holds more DIPs than the surface has pixels, and the
/// whole layout is drawn into it: taking the pixel count for the DIP size clipped the far part away.
/// Not parallelizable: assigns the process-wide Application.DefaultGraphicsFactory.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class SurfaceScaleBelowOneTests
{
    private const int LAYOUT_WIDTH = 400;
    private const int LAYOUT_HEIGHT = 300;
    private const int PIXEL_WIDTH = 200;
    private const int PIXEL_HEIGHT = 150;
    private const double SCALE = 0.5;
    private const double MARK = 80;
    private static readonly Color _ink = Color.FromRgb(200, 40, 80);
    private static readonly Color _background = Color.FromRgb(250, 250, 250);

    [TestMethod]
    [DataRow(TestBackend.Gdi)]
    [DataRow(TestBackend.Direct2D)]
    [DataRow(TestBackend.MewVG)]
    public void AFrameAtHalfScale_DrawsTheFarCornerOfTheLayout(TestBackend backend)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("The backends under test are Windows-only.");
            return;
        }

        using var session = TestBackendSession.Open(backend);
        var window = HeadlessWindow.Create(LAYOUT_WIDTH, LAYOUT_HEIGHT);
        window.SetDpi((uint)(96 * SCALE));
        window.Content = new Border { Background = _background, Child = new FarCornerMark() };
        window.PerformLayout();

        using var surface = session.Factory.CreateSurface(RenderSurfaceDescriptor.Offscreen(PIXEL_WIDTH, PIXEL_HEIGHT, SCALE, hasAlpha: false));
        window.RenderFrameToSurface(surface);
        Assert.AreEqual(_ink, PixelAt(surface, PIXEL_WIDTH - 10, PIXEL_HEIGHT - 10), "the far corner of the layout was not drawn");
        Assert.AreEqual(_background, PixelAt(surface, PIXEL_WIDTH / 2, PIXEL_HEIGHT / 2), "the layout was not drawn at the surface's scale");

        window.RenderFrameToSurface(surface);
        Assert.AreEqual(_ink, PixelAt(surface, PIXEL_WIDTH - 10, PIXEL_HEIGHT - 10), "the far corner was lost when the frame was drawn again");
    }

    private static Color PixelAt(IRenderSurface surface, int x, int y)
    {
        ReadOnlySpan<byte> pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int offset = (y * PIXEL_WIDTH + x) * 4;
        return Color.FromRgb(pixels[offset + 2], pixels[offset + 1], pixels[offset]);
    }

    private sealed class FarCornerMark : FrameworkElement
    {
        protected override void OnRender(IGraphicsContext context)
            => context.FillRectangle(new Rect(Bounds.Right - MARK, Bounds.Bottom - MARK, MARK, MARK), _ink);
    }
}
