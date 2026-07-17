using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering;

namespace MewUI.Test.Rendering;

[TestClass]
public sealed class WindowRenderTargetTests
{
    [TestMethod]
    public void PerFrameWrappers_WithSameGeneration_ReuseTargetForOneThousandFrames()
    {
        var display = new PlatformDisplayIdentity(0x100, 1, (nint)0x100);
        var target = new WindowRenderTarget(new TestWindowSurface((nint)0x200, display, 800, 600, 1.25));

        for (int frame = 0; frame < 1_000; frame++)
        {
            var wrapper = new TestWindowSurface((nint)0x200, display, 800, 600, 1.25);
            Assert.IsTrue(target.TryUpdateSurface(wrapper), $"Frame {frame} replaced a stable target.");
            Assert.AreSame(wrapper, target.Surface);
        }
    }

    [TestMethod]
    public void NativeGenerationChanges_InvalidateTarget()
    {
        var initialDisplay = new PlatformDisplayIdentity(0x100, 1, (nint)0x100);
        var target = new WindowRenderTarget(new TestWindowSurface((nint)0x200, initialDisplay, 800, 600, 1.25));

        Assert.IsFalse(target.TryUpdateSurface(
            new TestWindowSurface((nint)0x200, new PlatformDisplayIdentity(0x100, 2, (nint)0x100), 800, 600, 1.25)));
        Assert.IsFalse(target.TryUpdateSurface(new TestWindowSurface((nint)0x201, initialDisplay, 800, 600, 1.25)));
        Assert.IsFalse(target.TryUpdateSurface(new TestWindowSurface((nint)0x200, initialDisplay, 801, 600, 1.25)));
    }

    private sealed class TestWindowSurface : IWindowSurface
    {
        public TestWindowSurface(nint handle, PlatformDisplayIdentity displayIdentity, int pixelWidth, int pixelHeight, double dpiScale)
        {
            Handle = handle;
            DisplayIdentity = displayIdentity;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScale = dpiScale;
        }

        public nint Handle { get; }
        public PlatformDisplayIdentity DisplayIdentity { get; }
        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public double DpiScale { get; }
    }
}
