using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Direct2D;

namespace MewUI.Test.Rendering;

/// <summary>
/// Verifies the Direct2D gradient path still renders correctly after the stop collection was moved
/// behind a per-brush cache: the same brush instance is drawn twice (frame two hits the cache) and
/// both frames must produce the same left-dark / right-light horizontal ramp.
/// </summary>
[TestClass]
public sealed class Direct2DGradientTests
{
    private const int Width = 64;
    private const int Height = 16;

    [TestMethod]
    public void HorizontalGradient_RampsLeftToRight_AndIsStableAcrossCachedDraws()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("Direct2D backend is Windows-only.");
            return;
        }

        using var factory = new Direct2DGraphicsFactory();
        var brush = new LinearGradientBrush(
            new Point(0, 0),
            new Point(Width, 0),
            [new GradientStop(0.0, Color.FromArgb(255, 0, 0, 0)), new GradientStop(1.0, Color.FromArgb(255, 255, 255, 255))]);

        (int left, int right) first = RenderAndSample(factory, brush);
        (int left, int right) second = RenderAndSample(factory, brush);

        // Gradient present and correctly oriented (left dark, right light).
        Assert.IsLessThan(64, first.left, $"Expected a dark left edge, got {first.left}.");
        Assert.IsGreaterThan(190, first.right, $"Expected a light right edge, got {first.right}.");

        // The cached-stop-collection draw (frame two) matches the first within readback tolerance.
        Assert.IsTrue(Math.Abs(first.left - second.left) <= 4, $"Left drifted: {first.left} vs {second.left}.");
        Assert.IsTrue(Math.Abs(first.right - second.right) <= 4, $"Right drifted: {first.right} vs {second.right}.");
    }

    private static (int left, int right) RenderAndSample(Direct2DGraphicsFactory factory, LinearGradientBrush brush)
    {
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(Width, Height, 1.0));
        using (var context = factory.CreateContext(surface))
        {
            context.BeginFrame(surface);
            context.FillRectangle(new Rect(0, 0, Width, Height), brush);
            context.EndFrame();
        }

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int stride = cpu.StrideBytes;
        return (AverageRed(pixels, stride, 2), AverageRed(pixels, stride, Width - 3));
    }

    // BGRA byte order: red is the third channel.
    private static int AverageRed(ReadOnlySpan<byte> pixels, int stride, int column)
    {
        long sum = 0;
        for (int row = 0; row < Height; row++)
        {
            sum += pixels[row * stride + column * 4 + 2];
        }
        return (int)(sum / Height);
    }
}
