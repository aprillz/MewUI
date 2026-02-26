using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Linux.X11;
using Aprillz.MewUI.Rendering.FreeType;
using Aprillz.MewUI.Rendering.OpenGL;

namespace Aprillz.MewUI.Rendering.MewVG;

public sealed partial class MewVGGraphicsFactory
{
    private partial IFont CreateFontCore(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        var path = LinuxFontResolver.ResolveFontPath(family, weight, italic);
        int px = (int)Math.Max(1, Math.Round(size)); // Assume 96 dpi.
        return path != null
            ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px)
            : new BasicFont(family, size, weight, italic, underline, strikethrough);
    }

    private partial IFont CreateFontCore(string family, double size, uint dpi, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        var path = LinuxFontResolver.ResolveFontPath(family, weight, italic);
        int px = (int)Math.Max(1, Math.Round(size * dpi / 96.0, MidpointRounding.AwayFromZero));
        return path != null
            ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px)
            : new BasicFont(family, size, weight, italic, underline, strikethrough);
    }

    private partial IDisposable CreateWindowResources(IWindowSurface surface)
    {
        if (surface is not IX11GlxWindowSurface glx)
        {
            throw new ArgumentException("MewVG (X11) requires an X11 GLX window surface.", nameof(surface));
        }

        return MewVGX11WindowResources.Create(glx.Display, glx.Window, glx.VisualInfo);
    }

    private partial IGraphicsContext CreateContextCore(WindowRenderTarget target, IDisposable resources)
    {
        if (target.Surface is not IX11GlxWindowSurface glx)
        {
            throw new ArgumentException("MewVG (X11) requires an X11 GLX window surface.", nameof(target));
        }

        return new MewVGX11GraphicsContext(
            glx.Display,
            glx.Window,
            target.PixelWidth,
            target.PixelHeight,
            target.DpiScale,
            (MewVGX11WindowResources)resources);
    }

    private partial IGraphicsContext CreateMeasurementContextCore(uint dpi)
        => new OpenGLMeasurementContext(dpi);
}
