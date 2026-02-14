using System.Collections.Concurrent;

using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Win32;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.OpenGL;

public sealed partial class OpenGLGraphicsFactory : IGraphicsFactory, IWindowResourceReleaser, IWindowSurfacePresenter
{
    public static OpenGLGraphicsFactory Instance => field ??= new OpenGLGraphicsFactory();

    private readonly ConcurrentDictionary<nint, IOpenGLWindowResources> _windows = new();

    private OpenGLGraphicsFactory() { }

    public GraphicsBackend Backend => GraphicsBackend.OpenGL;

    public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
        => CreateFontCore(family, size, weight, italic, underline, strikethrough);

    public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
        => CreateFontCore(family, size, dpi, weight, italic, underline, strikethrough);

    private partial IFont CreateFontCore(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough);

    private partial IFont CreateFontCore(string family, double size, uint dpi, FontWeight weight, bool italic, bool underline, bool strikethrough);

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
            return CreateContextCore(windowTarget.Surface, windowTarget.DpiScale);
        }

        if (target is OpenGLBitmapRenderTarget bitmapTarget)
        {
            // This is used by Win32 layered window presentation (per-pixel alpha).
            // It requires a valid, current GL context associated with a window.
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("OpenGL bitmap render target is only supported on Win32 at the moment.");
            }

            var hwnd = _bitmapPresentHwnd;
            var hdc = _bitmapPresentHdc;
            if (hwnd == 0 || hdc == 0)
            {
                throw new InvalidOperationException(
                    "OpenGLBitmapRenderTarget requires an active Win32 layered present context. " +
                    "It is intended to be used via Window.AllowsTransparency with the OpenGL backend.");
            }

            var resources = _windows.GetOrAdd(hwnd, _ => CreateWindowResources(CreateWin32HdcSurface(hwnd, hdc, bitmapTarget)));
            return new OpenGLGraphicsContext(hwnd, hdc, bitmapTarget.DpiScale, resources, bitmapTarget);
        }

        throw new NotSupportedException($"Unsupported render target type: {target.GetType().Name}");
    }
     
    private IGraphicsContext CreateContextCore(IWindowSurface surface, double dpiScale)
    {
        nint hwnd;
        nint hdc;

        if (surface is IWin32HdcWindowSurface win32)
        {
            hwnd = win32.Hwnd;
            hdc = win32.Hdc;
        }        
        else if (surface is Platform.Linux.X11.IX11GlxWindowSurface x11)
        {
            hwnd = x11.Window;
            hdc = x11.Display;
        }
        else
        {
            throw new NotSupportedException($"Unsupported window surface type: {surface.GetType().Name}");
        }

        if (hwnd == 0 || hdc == 0)
        {
            throw new ArgumentException("Invalid window handle or device context.");
        }

        var resources = _windows.GetOrAdd(hwnd, _ => CreateWindowResources(surface));
        return new OpenGLGraphicsContext(hwnd, hdc, dpiScale, resources);
    }

    public IGraphicsContext CreateMeasurementContext(uint dpi)
        => CreateMeasurementContextCore(dpi);

    private partial IOpenGLWindowResources CreateWindowResources(IWindowSurface surface);

    private partial IGraphicsContext CreateMeasurementContextCore(uint dpi);

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
        s_releaseLayered?.Invoke(hwnd);
    }

    public bool Present(Window window, IWindowSurface surface, double opacity)
    {
        if (surface.Kind != WindowSurfaceKind.Layered)
        {
            return false;
        }

        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        var present = s_presentLayered;
        return present != null && present(window, surface, opacity);
    }

#pragma warning disable CS0649
    [ThreadStatic] private static nint _bitmapPresentHwnd;
    [ThreadStatic] private static nint _bitmapPresentHdc;
#pragma warning restore CS0649

    private static Func<Window, IWindowSurface, double, bool>? s_presentLayered;
    private static Action<nint>? s_releaseLayered;

    internal static void RegisterWin32LayeredHooks(
        Func<Window, IWindowSurface, double, bool> present,
        Action<nint> release)
    {
        s_presentLayered = present ?? throw new ArgumentNullException(nameof(present));
        s_releaseLayered = release ?? throw new ArgumentNullException(nameof(release));
    }

    private sealed class Win32HdcSurface : IWin32HdcWindowSurface
    {
        public WindowSurfaceKind Kind => WindowSurfaceKind.OpenGL;
        public nint Handle => Hwnd;
        public nint Hwnd { get; }
        public nint Hdc { get; }
        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public double DpiScale { get; }

        public Win32HdcSurface(nint hwnd, nint hdc, int pixelWidth, int pixelHeight, double dpiScale)
        {
            Hwnd = hwnd;
            Hdc = hdc;
            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScale = dpiScale <= 0 ? 1.0 : dpiScale;
        }
    }

    private static IWindowSurface CreateWin32HdcSurface(nint hwnd, nint hdc, OpenGLBitmapRenderTarget bitmapTarget)
        => new Win32HdcSurface(hwnd, hdc, bitmapTarget.PixelWidth, bitmapTarget.PixelHeight, bitmapTarget.DpiScale);
}
