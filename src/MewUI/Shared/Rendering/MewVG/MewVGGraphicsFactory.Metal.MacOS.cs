using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering.CoreText;

namespace Aprillz.MewUI.Rendering.MewVG;

public sealed partial class MewVGGraphicsFactory
{
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

        return new MewVGMetalGraphicsContext(metal.View, metal.MetalLayer, target.PixelWidth, target.PixelHeight, target.DpiScale, (MewVGMetalWindowResources)resources);
    }

    private partial IGraphicsContext CreateMeasurementContextCore(uint dpi)
        => new MewVGMetalMeasurementContext(dpi);

    static partial void TryGetPreferredSurfaceKind(ref bool handled, ref WindowSurfaceKind kind)
    {
        if (handled)
        {
            return;
        }

        kind = WindowSurfaceKind.Metal;
        handled = true;
    }
}
