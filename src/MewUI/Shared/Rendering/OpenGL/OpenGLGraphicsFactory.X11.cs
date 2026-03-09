using Aprillz.MewUI.Rendering.FreeType;

namespace Aprillz.MewUI.Rendering.OpenGL;

public sealed partial class OpenGLGraphicsFactory
{
    private partial IFont CreateFontCore(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        var path = LinuxFontResolver.ResolveFontPath(family, weight, italic);
        int px = (int)Math.Max(1, Math.Round(size)); // Assume 96dpi for now.
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

    private partial IOpenGLWindowResources CreateWindowResources(Platform.IWindowSurface surface)
    {
        if (surface is not Platform.Linux.X11.IX11GlxWindowSurface glx)
        {
            throw new ArgumentException("OpenGL (X11) requires an X11 GLX window surface.", nameof(surface));
        }

        return GlxOpenGLWindowResources.Create(glx.Display, glx.Window, glx.VisualInfo);
    }

    private partial IGraphicsContext CreateMeasurementContextCore(uint dpi)
        => new OpenGLMeasurementContext(dpi);
}
