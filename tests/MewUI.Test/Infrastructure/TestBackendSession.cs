extern alias MewVGWin32;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Rendering.Gdi;
using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;

namespace MewUI.Test.Infrastructure;

public enum TestBackend
{
    Gdi,
    Direct2D,
    MewVG,
}

/// <summary>A graphics factory made the process default for one test, with what its backend needs held open.</summary>
internal sealed class TestBackendSession : IDisposable
{
    // MewVG blends an antialiased edge one level apart where the clip of a repainted area cuts it; see
    // RetainedDirtyRegionExactnessTests.
    private const int MEWVG_CHANNEL_TOLERANCE = 1;

    private readonly IDisposable? _renderScope;

    private TestBackendSession(IGraphicsFactory factory, IDisposable? renderScope, int channelTolerance)
    {
        Factory = factory;
        _renderScope = renderScope;
        ChannelTolerance = channelTolerance;
        Application.DefaultGraphicsFactory = factory;
    }

    internal IGraphicsFactory Factory { get; }

    internal int ChannelTolerance { get; }

    internal static TestBackendSession Open(TestBackend backend)
    {
        switch (backend)
        {
            case TestBackend.Direct2D:
                return new TestBackendSession(new Aprillz.MewUI.Rendering.Direct2D.Direct2DGraphicsFactory(), null, 0);
            case TestBackend.MewVG:
                var factory = new MewVGWin32GraphicsFactory();
                return new TestBackendSession(factory, factory.AcquireBackgroundRenderScope(), MEWVG_CHANNEL_TOLERANCE);
            default:
                return new TestBackendSession(new GdiGraphicsFactory(), null, 0);
        }
    }

    /// <summary>Compares the color channels of two surfaces and names the box the differing pixels fall in.</summary>
    internal static void AssertSurfacesEqual(IRenderSurface reference, IRenderSurface actual, int width, int channelTolerance, string label)
    {
        ReadOnlySpan<byte> expected = ((ICpuPixelSurface)reference).GetReadOnlyPixelSpan();
        ReadOnlySpan<byte> shown = ((ICpuPixelSurface)actual).GetReadOnlyPixelSpan();
        int differing = 0;
        int left = int.MaxValue;
        int top = int.MaxValue;
        int right = -1;
        int bottom = -1;
        for (int offset = 0; offset + 3 < expected.Length; offset += 4)
        {
            if (Math.Abs(expected[offset] - shown[offset]) > channelTolerance ||
                Math.Abs(expected[offset + 1] - shown[offset + 1]) > channelTolerance ||
                Math.Abs(expected[offset + 2] - shown[offset + 2]) > channelTolerance)
            {
                differing++;
                int pixel = offset / 4;
                left = Math.Min(left, pixel % width);
                right = Math.Max(right, pixel % width);
                top = Math.Min(top, pixel / width);
                bottom = Math.Max(bottom, pixel / width);
            }
        }

        Assert.AreEqual(0, differing, $"{label}: {differing} pixels differ from a whole frame, within x {left}..{right}, y {top}..{bottom}");
    }

    public void Dispose()
    {
        _renderScope?.Dispose();
        (Factory as IDisposable)?.Dispose();
    }
}
