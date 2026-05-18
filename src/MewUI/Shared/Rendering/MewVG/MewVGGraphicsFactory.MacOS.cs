using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering.CoreText;

namespace Aprillz.MewUI.Rendering.MewVG;

public sealed partial class MewVGMacOSGraphicsFactory
{
    public const string BackendIdentifier = "MewVG.MacOS";

    private readonly MewVGMetalOffscreenSurfaceProvider _offscreenProvider = new();

    public string Backend => BackendIdentifier;

    private partial IFont CreateFontCore(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        uint dpi = 96;
        try
        {
            if (Application.IsRunning)
            {
                dpi = Application.Current.PlatformHost.GetSystemDpi();
            }
        }
        catch
        {
            // Best-effort: use 96 DPI when app/platform isn't initialized yet.
        }

        return CoreTextFont.Create(family, size, dpi, weight, italic, underline, strikethrough);
    }

    private partial IFont CreateFontCore(string family, double size, uint dpi, FontWeight weight, bool italic, bool underline, bool strikethrough)
        => CoreTextFont.Create(family, size, dpi, weight, italic, underline, strikethrough);

    private partial IDisposable CreateWindowResources(IWindowSurface surface)
    {
        if (surface is not Platform.MacOS.IMacOSMetalWindowSurface metal || metal.View == 0 || metal.MetalLayer == 0)
        {
            throw new ArgumentException("MewVG (Metal) requires a macOS Metal window surface.", nameof(surface));
        }

        return MewVGMetalWindowResources.Create(metal.View, metal.MetalLayer);
    }

    private partial IGraphicsContext CreateContextCore(WindowRenderTarget target, IDisposable resources)
    {
        if (target.Surface is not Platform.MacOS.IMacOSMetalWindowSurface metal ||
            metal.View == 0 ||
            metal.MetalLayer == 0)
        {
            throw new ArgumentException("MewVG (Metal) requires a macOS Metal window surface.", nameof(target));
        }

        var res = (MewVGMetalWindowResources)resources;
        var ctx = res.GetOrCreateContext(_offscreenProvider);
        return ctx;
    }

    private partial IGraphicsContext CreateMeasurementContextCore(uint dpi)
        => new MewVGMetalMeasurementContext(dpi);

    partial void TryGetPreferredSurfaceKind(ref bool handled, ref WindowSurfaceKind kind)
    {
        if (handled)
        {
            return;
        }

        kind = WindowSurfaceKind.Metal;
        handled = true;
    }

    partial void TryCreatePixelSurface(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha, ref bool handled, ref IRenderSurface? renderTarget)
    {
        if (handled)
        {
            return;
        }

        renderTarget = new MewVGMetalPixelRenderSurface(pixelWidth, pixelHeight, dpiScale, hasAlpha);
        handled = true;
    }

    partial void TryGetImageDisposeHandler(ref Action<MewVGImage>? handler)
        => handler ??= _offscreenProvider.QueueImageDisposal;

    partial void TryCreateImageFilterExecutor(ref Filters.IImageFilterExecutor? executor)
        => executor ??= new MetalImageFilterExecutor(_offscreenProvider);

    partial void TryCreateContextForTarget(IRenderTarget target, ref bool handled, ref IGraphicsContext? context)
    {
        if (handled)
        {
            return;
        }

        if (target is not MewVGMetalPixelRenderSurface pixelSurface)
        {
            return;
        }

        // Offscreen rendering borrows a fresh NVG instance from the pool
        // bound to the system-default MTLDevice. Metal textures created on
        // any pool instance are sample-able from the window's NVG because
        // they share the device. Borrow gives this pass its own NVG so
        // nested offscreen (e.g. cache -> pattern -> filter) does not
        // have inner BeginFrame stomping outer's state. No drawable is
        // acquired and no Present runs; the context blits the pixel surface's
        // own MTLTexture back into its CPU pixel buffer at EndFrame
        // so filter / pattern uploads see the rendered output.
        var offscreenResources = _offscreenProvider.AcquireSurface();
        context = MewVGMacOSGraphicsContext.CreateForOffscreen(offscreenResources, pixelSurface, _offscreenProvider);
        handled = true;
    }

    partial void DisposePlatformResources()
        => _offscreenProvider.Dispose();

    // Metal: MTLDevice / MTLCommandQueue are thread-safe. Worker threads can
    // submit command buffers without per-thread setup, so this is a no-op.
    private partial IDisposable AcquireBackgroundRenderScopeCore() => MewVGNoOpRenderScope.Instance;
}
