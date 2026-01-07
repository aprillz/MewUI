using System.Collections.Concurrent;

using Aprillz.MewUI.Core;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.FreeType;

namespace Aprillz.MewUI.Rendering.OpenGL;

public sealed class OpenGLGraphicsFactory : IGraphicsFactory, IWindowResourceReleaser
{
    public static OpenGLGraphicsFactory Instance => field ??= new OpenGLGraphicsFactory();

    private readonly ConcurrentDictionary<nint, IOpenGLWindowResources> _windows = new();

    private OpenGLGraphicsFactory() { }

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
    {
        if (OperatingSystem.IsWindows())
        {
            uint dpi = DpiHelper.GetSystemDpi();
            return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);
        }

        var path = LinuxFontResolver.ResolveFontPath(family, weight, italic);
        int px = (int)Math.Max(1, Math.Round(size)); // Assume 96dpi for now.
        return path != null
            ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px)
            : new BasicFont(family, size, weight, italic, underline, strikethrough);
    }

    public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
    {
        if (OperatingSystem.IsWindows())
            return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);

        var path = LinuxFontResolver.ResolveFontPath(family, weight, italic);
        int px = (int)Math.Max(1, Math.Round(size * dpi / 96.0, MidpointRounding.AwayFromZero));
        return path != null
            ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px)
            : new BasicFont(family, size, weight, italic, underline, strikethrough);
    }

    public IImage CreateImageFromFile(string path) =>
        throw new NotImplementedException("Image loading is not implemented yet.");

    public IImage CreateImageFromBytes(byte[] data) =>
        throw new NotImplementedException("Image loading is not implemented yet.");

    public IGraphicsContext CreateContext(nint hwnd, nint hdc, double dpiScale)
    {
        if (hwnd == 0 || hdc == 0)
            throw new ArgumentException("Invalid window handle or device context.");

        var resources = _windows.GetOrAdd(hwnd, _ =>
        {
            if (OperatingSystem.IsWindows())
                return WglOpenGLWindowResources.Create(hwnd, hdc);
            if (OperatingSystem.IsLinux())
                // Linux: hwnd = X11 Window (Drawable), hdc = Display*
                return GlxOpenGLWindowResources.Create(hdc, hwnd);

            throw new PlatformNotSupportedException("OpenGL backend is supported on Windows and Linux only.");
        });
        return new OpenGLGraphicsContext(hwnd, hdc, dpiScale, resources);
    }

    public IGraphicsContext CreateMeasurementContext(uint dpi)
    {
        if (OperatingSystem.IsWindows())
        {
            var hdc = Aprillz.MewUI.Native.User32.GetDC(0);
            return new GdiMeasurementContext(hdc, dpi);
        }

        return new OpenGLMeasurementContext(dpi);
    }

    public void ReleaseWindowResources(nint hwnd)
    {
        if (hwnd == 0)
            return;

        if (_windows.TryRemove(hwnd, out var resources))
            resources.Dispose();
    }
}
