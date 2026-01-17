using System.Collections.Concurrent;

using Aprillz.MewUI;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.OpenGL;

#if MEWUI_OPENGL_WIN32
using Aprillz.MewUI.Rendering.Gdi;
#endif

#if MEWUI_OPENGL_X11
using Aprillz.MewUI.Rendering.FreeType;
#endif

public sealed class OpenGLGraphicsFactory : IGraphicsFactory, IWindowResourceReleaser
{
    public static OpenGLGraphicsFactory Instance => field ??= new OpenGLGraphicsFactory();

    private readonly ConcurrentDictionary<nint, IOpenGLWindowResources> _windows = new();

    private OpenGLGraphicsFactory() { }

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
    {
#if MEWUI_OPENGL_WIN32
        uint dpi = DpiHelper.GetSystemDpi();
        return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);
#elif MEWUI_OPENGL_X11
        var path = LinuxFontResolver.ResolveFontPath(family, weight, italic);
        int px = (int)Math.Max(1, Math.Round(size)); // Assume 96dpi for now.
        return path != null
            ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px)
            : new BasicFont(family, size, weight, italic, underline, strikethrough);
#else
        return new BasicFont(family, size, weight, italic, underline, strikethrough);
#endif
    }

    public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
    {
#if MEWUI_OPENGL_WIN32
        return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);
#elif MEWUI_OPENGL_X11
        var path = LinuxFontResolver.ResolveFontPath(family, weight, italic);
        int px = (int)Math.Max(1, Math.Round(size * dpi / 96.0, MidpointRounding.AwayFromZero));
        return path != null
            ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px)
            : new BasicFont(family, size, weight, italic, underline, strikethrough);
#else
        return new BasicFont(family, size, weight, italic, underline, strikethrough);
#endif
    }

    public IImage CreateImageFromFile(string path) =>
        CreateImageFromBytes(File.ReadAllBytes(path));

    public IImage CreateImageFromBytes(byte[] data) =>
        ImageDecoders.TryDecode(data, out var bmp)
            ? new OpenGLImage(bmp.WidthPx, bmp.HeightPx, bmp.Data)
            : throw new NotSupportedException(
                $"Unsupported image format. Built-in decoders: BMP/PNG/JPEG. Detected: {ImageDecoders.DetectFormatId(data) ?? "unknown"}.");

    public IImage CreateImageFromPixelSource(IPixelBufferSource source) => new OpenGLImage(source);

    public IGraphicsContext CreateContext(nint hwnd, nint hdc, double dpiScale)
    {
        if (hwnd == 0 || hdc == 0)
        {
            throw new ArgumentException("Invalid window handle or device context.");
        }

        var resources = _windows.GetOrAdd(hwnd, _ =>
        {
#if MEWUI_OPENGL_WIN32
            return WglOpenGLWindowResources.Create(hwnd, hdc);
#elif MEWUI_OPENGL_X11
            // Linux: hwnd = X11 Window (Drawable), hdc = Display*
            return GlxOpenGLWindowResources.Create(hdc, hwnd);
#else
            throw new PlatformNotSupportedException("This OpenGL backend build is not configured for the current platform.");
#endif
        });
        return new OpenGLGraphicsContext(hwnd, hdc, dpiScale, resources);
    }

    public IGraphicsContext CreateMeasurementContext(uint dpi)
    {
#if MEWUI_OPENGL_WIN32
        var hdc = Native.User32.GetDC(0);
        return new GdiMeasurementContext(hdc, dpi);
#elif MEWUI_OPENGL_X11
        return new OpenGLMeasurementContext(dpi);
#else
        return new OpenGLMeasurementContext(dpi);
#endif
    }

    public void ReleaseWindowResources(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        if (_windows.TryRemove(hwnd, out var resources))
        {
            resources.Dispose();
        }
    }
}
