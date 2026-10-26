extern alias MewVGWin32;
extern alias MewVGX11;
extern alias MewVGMacOS;

using Aprillz.MewUI;
using Aprillz.MewUI.Rendering;
using Aprillz.MewUI.Text;

using MewVGMacOSGraphicsFactory = MewVGMacOS::Aprillz.MewUI.Rendering.MewVG.MewVGMacOSGraphicsFactory;
using MewVGWin32GraphicsFactory = MewVGWin32::Aprillz.MewUI.Rendering.MewVG.MewVGWin32GraphicsFactory;
using MewVGX11GraphicsFactory = MewVGX11::Aprillz.MewUI.Rendering.MewVG.MewVGX11GraphicsFactory;

namespace MewUI.Test.Rendering;

/// <summary>
/// A context kept on an offscreen surface draws the same transient text frame after frame. Each frame
/// rasterizes it into a scratch texture again, and the next frame has to reuse that texture instead of
/// taking another one, or the text cache grows by one texture a frame (#294).
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class MewVGOffscreenTextCacheTests
{
    private const int FRAMES = 200;
    private const int WARM_UP_FRAMES = 8;

    [TestMethod]
    [DataRow("Win32")]
    [DataRow("X11")]
    [DataRow("MacOS")]
    public void RepeatedTransientText_KeepsTheTextCacheBounded(string platform)
    {
        if (!RunsHere(platform))
        {
            Assert.Inconclusive($"The MewVG {platform} backend does not run on this platform.");
            return;
        }

        IGraphicsFactory factory = CreateFactory(platform);
        using var disposable = factory as IDisposable;
        using var backgroundScope = factory.AcquireBackgroundRenderScope();
        using var surface = factory.CreateSurface(RenderSurfaceDescriptor.CpuPixels(400, 80));
        using var context = factory.CreateContext(surface);
        var layout = factory.TextEngine.CreateLayout(new TextLayoutRequest
        {
            Text = "Repeated offscreen text".AsMemory(),
            Dpi = 96,
            DefaultStyle = new TextRunStyle("sans-serif", 22),
            Paragraph = new TextParagraphStyle { MaxWidth = 380 },
        });

        long warmBytes = 0;
        for (int frame = 0; frame < FRAMES; frame++)
        {
            context.BeginFrame(surface);
            context.Clear(Color.Transparent);
            context.Text.Draw(layout, new Point(4, 4), new TextDrawOptions(Color.White, Transient: true));
            context.EndFrame();
            if (frame == WARM_UP_FRAMES - 1)
            {
                warmBytes = RenderResourceMetrics.Snapshot().TextCacheBytes;
            }
        }

        long growth = RenderResourceMetrics.Snapshot().TextCacheBytes - warmBytes;
        Assert.AreEqual(
            0,
            growth,
            $"{platform}: {FRAMES - WARM_UP_FRAMES} more frames of the same transient text grew the text cache by {growth} bytes");
    }

    private static bool RunsHere(string platform) => platform switch
    {
        "X11" => OperatingSystem.IsLinux(),
        "MacOS" => OperatingSystem.IsMacOS(),
        _ => OperatingSystem.IsWindows(),
    };

    private static IGraphicsFactory CreateFactory(string platform)
    {
        switch (platform)
        {
            case "X11":
                // No window precedes the frames, so the backend renders on a surfaceless EGL device.
                Application.RendersWithoutDisplay = true;
                MewVGX11::Aprillz.MewUI.MewVGX11Backend.Register();
                return new MewVGX11GraphicsFactory();
            case "MacOS":
                return new MewVGMacOSGraphicsFactory();
            default:
                return new MewVGWin32GraphicsFactory();
        }
    }
}
