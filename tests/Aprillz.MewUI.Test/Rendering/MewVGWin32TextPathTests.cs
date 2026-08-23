extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using MewUI.Test.Infrastructure;

using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Rendering;

[TestClass]
[DoNotParallelize]
public sealed class MewVGWin32TextPathTests
{
    [TestMethod]
    [DataRow(1.0)]
    [DataRow(1.25)]
    [DataRow(1.5)]
    public void PathClip_IntersectsArbitraryPathsAndRestoreRemovesClip(double dpiScale)
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("MewVG Win32 is Windows-only.");
            return;
        }

        const int logicalWidth = 128;
        const int logicalHeight = 128;
        var width = (int)Math.Ceiling(logicalWidth * dpiScale);
        var height = (int)Math.Ceiling(logicalHeight * dpiScale);
        using var factory = new MewVGWin32GraphicsFactory();
        using var backgroundScope = factory.AcquireBackgroundRenderScope();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, dpiScale));
        using var context = factory.CreateContext(surface);

        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        context.Save();
        context.SetClipRoundedRect(new Rect(10, 10, 108, 108), 20, 20);
        context.SetClipPath(PathGeometry.Parse("M36 18 L116 64 L36 110 Z"));
        context.FillRectangle(new Rect(0, 0, logicalWidth, logicalHeight), Color.FromRgb(255, 0, 0));
        context.Restore();
        context.FillRectangle(new Rect(1, 1, 6, 6), Color.FromRgb(0, 255, 0));
        context.EndFrame();

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        static byte AlphaAt(ReadOnlySpan<byte> data, int stride, int x, int y)
            => data[y * stride + x * 4 + 3];
        int Px(double value) => (int)Math.Round(value * dpiScale);

        Assert.IsGreaterThan((byte)240, AlphaAt(pixels, cpu.StrideBytes, Px(64), Px(64)), "A pixel inside both clips must be drawn.");
        Assert.IsLessThan((byte)16, AlphaAt(pixels, cpu.StrideBytes, Px(20), Px(64)), "The arbitrary inner clip must reject this pixel.");
        Assert.IsLessThan((byte)16, AlphaAt(pixels, cpu.StrideBytes, Px(12), Px(12)), "The rounded outer corner must remain clipped.");
        Assert.IsGreaterThan((byte)240, AlphaAt(pixels, cpu.StrideBytes, Px(3), Px(3)), "Restore must remove the path clip.");
    }

    [TestMethod]
    public void TransparentConcaveFill_UsesBoundedCoverageAtItsDeviceOffset()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("MewVG Win32 is Windows-only.");
            return;
        }

        const int width = 256;
        const int height = 128;
        using var factory = new MewVGWin32GraphicsFactory();
        using var backgroundScope = factory.AcquireBackgroundRenderScope();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1));
        using var context = factory.CreateContext(surface);

        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        var concave = PathGeometry.Parse("M80 20 L180 20 L180 50 L110 50 L110 100 L80 100 Z");
        context.FillPath(concave, Color.FromArgb(128, 255, 255, 255));
        context.EndFrame();

        var cpu = (ICpuPixelSurface)surface;
        var pixels = cpu.GetReadOnlyPixelSpan();
        int minX = width;
        int maxX = -1;
        int coveredPixels = 0;
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                if (pixels[y * cpu.StrideBytes + x * 4 + 3] <= 16)
                {
                    continue;
                }

                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                coveredPixels++;
            }
        }

        Assert.IsGreaterThan(4_000, coveredPixels);
        Assert.IsGreaterThanOrEqualTo(78, minX,
            "The call-local coverage texture was sampled as if it started at the framebuffer origin.");
        Assert.IsLessThanOrEqualTo(82, minX);
        Assert.IsGreaterThanOrEqualTo(178, maxX);
        Assert.IsLessThanOrEqualTo(182, maxX);
    }

    [TestMethod]
    public void DrawTextLayout_RealizesGdiMeasuredTextIntoMewVGSurface()
    {
        if (!OperatingSystem.IsWindows())
        {
            Assert.Inconclusive("MewVG Win32 is Windows-only.");
            return;
        }

        const int width = 240;
        const int height = 64;
        const string text = "office 한글";

        using var factory = new MewVGWin32GraphicsFactory();
        using var backgroundScope = factory.AcquireBackgroundRenderScope();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CachedImage(width, height, 1));
        using var context = factory.CreateContext(surface);
        using var font = factory.CreateFont("Segoe UI", 18, 96);

        context.BeginFrame(surface);
        context.Clear(Color.Transparent);
        var bounds = new Rect(4, 4, width - 8, height - 8);
        var layout = TextTestHarness.CreateLayout(factory, text.AsMemory(), font, bounds);
        Assert.IsGreaterThan(0, layout.MeasuredSize.Width);
        TextTestHarness.Draw(context, layout, bounds, Color.White);
        context.EndFrame();

        var pixels = ((ICpuPixelSurface)surface).GetReadOnlyPixelSpan();
        int coveredPixels = 0;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] > 16)
            {
                coveredPixels++;
            }
        }

        Assert.IsGreaterThanOrEqualTo(5, coveredPixels,
            "GDI measurement succeeded, but the independent MewVG raster/image-pattern path produced no text pixels.");
    }
}
