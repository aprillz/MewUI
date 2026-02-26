using System.Collections.Concurrent;

using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.MewVG;

public sealed partial class MewVGGraphicsFactory : IGraphicsFactory, IWindowResourceReleaser, IWindowSurfaceSelector, IWindowSurfacePresenter
{
    public static MewVGGraphicsFactory Instance => field ??= new MewVGGraphicsFactory();

    private readonly ConcurrentDictionary<nint, IDisposable> _windows = new();

    private MewVGGraphicsFactory() { }

    public GraphicsBackend Backend => GraphicsBackend.Custom;

    public WindowSurfaceKind PreferredSurfaceKind
    {
        get
        {
            var kind = WindowSurfaceKind.Default;
            bool handled = false;
            TryGetPreferredSurfaceKind(ref handled, ref kind);
            return handled ? kind : WindowSurfaceKind.Default;
        }
    }

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
            ? new MewVGImage(bmp.WidthPx, bmp.HeightPx, bmp.Data)
            : throw new NotSupportedException(
                $"Unsupported image format. Built-in decoders: BMP/PNG/JPEG. Detected: {ImageDecoders.DetectFormatId(data) ?? "unknown"}.");

    public IImage CreateImageFromPixelSource(IPixelBufferSource source) => new MewVGImage(source);

    public IGraphicsContext CreateContext(IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target is WindowRenderTarget windowTarget)
        {
            return CreateContextCore(windowTarget);
        }

        IGraphicsContext? context = null;
        bool handled = false;
        TryCreateContextForTarget(target, ref handled, ref context);
        if (handled && context != null)
        {
            return context;
        }

        throw new NotSupportedException($"Unsupported render target type: {target.GetType().Name}");
    }

    partial void TryCreateContextForTarget(IRenderTarget target, ref bool handled, ref IGraphicsContext? context);

    private IGraphicsContext CreateContextCore(WindowRenderTarget target)
    {
        var surface = target.Surface;
        if (surface == null)
        {
            throw new ArgumentException("Invalid window surface.");
        }

        var handle = surface.Handle;
        if (handle == 0)
        {
            throw new ArgumentException("Invalid window surface handles.");
        }

        var resources = _windows.GetOrAdd(handle, _ => CreateWindowResources(surface));
        return CreateContextCore(target, resources);
    }

    public IGraphicsContext CreateMeasurementContext(uint dpi)
        => CreateMeasurementContextCore(dpi);

    public IBitmapRenderTarget CreateBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale = 1.0)
    {
        IBitmapRenderTarget? rt = null;
        bool handled = false;
        TryCreateBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale, ref handled, ref rt);
        if (handled && rt != null)
        {
            return rt;
        }

        throw new NotSupportedException("MewVG backend does not support bitmap render targets on this platform.");
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

        TryReleaseWindowResources(hwnd);
    }

    private partial IDisposable CreateWindowResources(IWindowSurface surface);

    private partial IGraphicsContext CreateContextCore(WindowRenderTarget target, IDisposable resources);

    private partial IGraphicsContext CreateMeasurementContextCore(uint dpi);

    static partial void TryReleaseWindowResources(nint hwnd);

    static partial void TryCreateBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale, ref bool handled, ref IBitmapRenderTarget? renderTarget);

    static partial void TryGetPreferredSurfaceKind(ref bool handled, ref WindowSurfaceKind kind);

    public bool Present(Window window, IWindowSurface surface, double opacity)
    {
        bool handled = false;
        bool result = false;
        TryPresentWindowSurface(window, surface, opacity, ref handled, ref result);
        return handled && result;
    }

    static partial void TryPresentWindowSurface(Window window, IWindowSurface surface, double opacity, ref bool handled, ref bool result);
}
