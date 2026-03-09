using Aprillz.MewUI.Rendering.CoreText;

namespace Aprillz.MewUI.Rendering.OpenGL;

public sealed partial class OpenGLGraphicsFactory
{
    private partial IFont CreateFontCore(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
        => CoreTextFont.Create(family, size, weight, italic, underline, strikethrough);

    private partial IFont CreateFontCore(string family, double size, uint dpi, FontWeight weight, bool italic, bool underline, bool strikethrough)
        => CoreTextFont.Create(family, size, dpi, weight, italic, underline, strikethrough);

    private partial IOpenGLWindowResources CreateWindowResources(Platform.IWindowSurface surface)
    {
        if (surface is not Platform.MacOS.IMacOSOpenGLWindowSurface mac)
        {
            throw new ArgumentException("OpenGL (macOS) requires a macOS OpenGL window surface.", nameof(surface));
        }

        // macOS: View = NSView*, OpenGLContext = NSOpenGLContext*
        return NsOpenGLWindowResources.Create(mac.OpenGLContext);
    }

    private partial IGraphicsContext CreateMeasurementContextCore(uint dpi)
        => new OpenGLMeasurementContext(dpi);
}
