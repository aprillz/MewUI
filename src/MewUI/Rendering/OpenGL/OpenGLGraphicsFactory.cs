using System.Collections.Concurrent;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Rendering.FreeType;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.OpenGL;

public sealed class OpenGLGraphicsFactory : IGraphicsFactory, IWindowResourceReleaser
{
    public static OpenGLGraphicsFactory Instance => field ??= new OpenGLGraphicsFactory();

    private readonly ConcurrentDictionary<nint, IOpenGLWindowResources> _windows = new();

    private OpenGLGraphicsFactory()
    { }

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
    {
        if (OperatingSystem.IsWindows())
        {
            uint dpi = DpiHelper.GetSystemDpi();
            family = ResolveWin32FontFamilyOrFile(family);
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
        {
            family = ResolveWin32FontFamilyOrFile(family);
            return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);
        }

        var path = LinuxFontResolver.ResolveFontPath(family, weight, italic);
        int px = (int)Math.Max(1, Math.Round(size * dpi / 96.0, MidpointRounding.AwayFromZero));
        return path != null
            ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px)
            : new BasicFont(family, size, weight, italic, underline, strikethrough);
    }

    private static string ResolveWin32FontFamilyOrFile(string familyOrPath)
    {
        if (!FontResources.LooksLikeFontFilePath(familyOrPath))
        {
            return familyOrPath;
        }

        var path = Path.GetFullPath(familyOrPath);
        _ = Win32Fonts.EnsurePrivateFont(path);

        return FontResources.TryGetParsedFamilyName(path, out var parsed) && !string.IsNullOrWhiteSpace(parsed)
            ? parsed
            : "Segoe UI";
    }

    public IImage CreateImageFromFile(string path) =>
        CreateImageFromBytes(File.ReadAllBytes(path));

    public IImage CreateImageFromBytes(byte[] data) =>
        ImageDecoders.TryDecode(data, out var bmp)
            ? new OpenGLImage(bmp.WidthPx, bmp.HeightPx, bmp.Data)
            : throw new NotSupportedException(
                $"Unsupported image format. Built-in decoders: BMP/PNG/JPEG. Detected: {ImageDecoders.DetectFormatId(data) ?? "unknown"}.");

    public IImage CreateImageFromPixelSource(IPixelBufferSource source) => new OpenGLImage(source);

    public IGraphicsContext CreateContext(IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target is WindowRenderTarget windowTarget)
        {
            return CreateContextCore(windowTarget.Hwnd, windowTarget.DeviceContext, windowTarget.DpiScale);
        }

        if (target is OpenGLBitmapRenderTarget)
        {
            // Note: Full IGraphicsContext support for OpenGL FBO requires a valid GL context.
            // For now, use direct pixel manipulation via LockForWrite() or GetPixelSpan().
            throw new NotSupportedException(
                "OpenGL backend does not yet support IGraphicsContext for OpenGLBitmapRenderTarget. " +
                "Use direct pixel manipulation via GetPixelSpan() or LockForWrite(), or use Direct2D/GDI backend for full graphics context support.");
        }

        throw new NotSupportedException($"Unsupported render target type: {target.GetType().Name}");
    }
     
    private IGraphicsContext CreateContextCore(nint hwnd, nint hdc, double dpiScale)
    {
        if (hwnd == 0 || hdc == 0)
        {
            throw new ArgumentException("Invalid window handle or device context.");
        }

        var resources = _windows.GetOrAdd(hwnd, _ =>
        {
            if (OperatingSystem.IsWindows())
            {
                return WglOpenGLWindowResources.Create(hwnd, hdc);
            }

            if (OperatingSystem.IsLinux())
            {
                // Linux: hwnd = X11 Window (Drawable), hdc = Display*
                return GlxOpenGLWindowResources.Create(hdc, hwnd);
            }

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

    public IBitmapRenderTarget CreateBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale = 1.0)
        => new OpenGLBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale);

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