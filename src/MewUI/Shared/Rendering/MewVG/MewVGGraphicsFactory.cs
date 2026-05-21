using System.Collections.Concurrent;

using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering.Filters;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.MewVG;

#if MEWUI_MEWVG_MACOS
public sealed partial class MewVGMacOSGraphicsFactory

#elif MEWUI_MEWVG_X11
public sealed partial class MewVGX11GraphicsFactory 

#else
public sealed partial class MewVGWin32GraphicsFactory 
#endif
    : IGraphicsFactory, IRenderDevice, IGpuInteropInvalidationSource, IWindowResourceReleaser, IWindowSurfacePresenter
{
    public event EventHandler<GpuInteropInvalidatedEventArgs>? GpuInteropInvalidated;

    internal void RaiseGpuInteropInvalidated(GpuInteropInvalidatedEventArgs e)
        => GpuInteropInvalidated?.Invoke(this, e);


#if MEWUI_MEWVG_MACOS
    public static MewVGMacOSGraphicsFactory Instance => field ??= new();
    private MewVGMacOSGraphicsFactory() { }
#elif MEWUI_MEWVG_X11
    public static MewVGX11GraphicsFactory Instance => field ??= new ();
    private MewVGX11GraphicsFactory() { }
#else
        public static MewVGWin32GraphicsFactory Instance => field ??= new ();
    private MewVGWin32GraphicsFactory() { }
#endif

    private readonly ConcurrentDictionary<nint, IDisposable> _windows = new();
    private readonly RenderResourceCache _renderResourceCache = new();


    /// <summary>
    /// When <see langword="true"/>, CPU pixel sources (<see cref="IPixelBufferSource"/>) are
    /// uploaded to GL textures via a Pixel Buffer Object ring + fence sync instead of a
    /// blocking <c>glTexImage2D</c>. The upload becomes async DMA so the producer thread
    /// returns immediately; the next sample waits on the fence to ensure the upload landed.
    /// <para/>
    /// Tradeoffs:
    /// <list type="bullet">
    ///   <item>+ frees the producer thread (no GPU stall on upload)</item>
    ///   <item>+ smoother frame pacing for animated CPU sources (plasma, charts, color picker)</item>
    ///   <item>– 1 frame of latency between version bump and on-screen update (double-PBO ring)</item>
    ///   <item>– per-image PBO allocation (small fixed GPU memory overhead)</item>
    /// </list>
    /// Default <see langword="false"/> while the implementation lands; flip to true after
    /// jitter measurement on representative workloads.
    /// </summary>
    /// <remarks>
    /// Internal — exposed only to MewUI.dll callers (<c>MewVGImage</c>) and test surface.
    /// Public callers should not depend on this knob; if a stable opt-in is needed it
    /// will go through <c>IGraphicsFactory</c> as a versioned option.
    /// </remarks>
    internal bool UseAsyncPboUpload { get; set; } = true;

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
            ? new MewVGImage(bmp.WidthPx, bmp.HeightPx, bmp.Data, GetImageDisposeHandler())
            : throw new NotSupportedException(
                $"Unsupported image format. Built-in decoders: BMP/PNG/JPEG. Detected: {ImageDecoders.DetectFormatId(data) ?? "unknown"}.");

    public IImage CreateImageView(IPixelBufferSource source)
    {
        if (UseAsyncPboUpload && QualifiesForPboUpload(source))
        {
            IImage? asyncImage = null;
            TryCreateAsyncUploadImage(source, ref asyncImage);
            if (asyncImage is not null) return asyncImage;
        }

        return new MewVGImage(source, GetImageDisposeHandler());
    }

    /// <summary>
    /// Platform hook for the async (PBO+fence) upload variant. GL-backed builds
    /// (Win32, X11) wrap the source as a <c>PboFenceUploader</c>-backed external
    /// texture; Metal builds leave the parameter unset and the caller falls back
    /// to the default sync upload path. Failure is silent (no exception bubbles
    /// out) — async is a performance opt-in, never required for correctness.
    /// </summary>
    partial void TryCreateAsyncUploadImage(IPixelBufferSource source, ref IImage? image);

    /// <summary>
    /// Routes to async PBO upload only when the producer explicitly flags itself as
    /// streaming. Static sources (file-decoded PNG, opaque <c>WriteableBitmap</c> drawn
    /// once) stay on the sync path so <see cref="ImageScaleQuality.HighQuality"/> can
    /// emit mipmaps. Readback sources skip PBO because the lock-time sync barrier
    /// already dominates.
    /// </summary>
    private static bool QualifiesForPboUpload(IPixelBufferSource source)
    {
        if (!source.IsStreaming) return false;
        if (source.LockMode == LockMode.Readback) return false;
        return true;
    }

    private IImage CreateExternalRasterImage(IExternalRasterSource source)
        => new MewVGExternalRasterImage(source);

    public IImageFilterExecutor CreateImageFilterExecutor()
    {
        IImageFilterExecutor? executor = null;
        TryCreateImageFilterExecutor(ref executor);
        return executor ?? new CpuImageFilterExecutor();
    }

    /// <summary>
    /// Per-platform hook to install a backend-specific filter executor (e.g. OpenGL shaders
    /// on Win32/X11, Metal on macOS). Leave <paramref name="executor"/> null to fall back to
    /// the CPU reference implementation.
    /// </summary>
    partial void TryCreateImageFilterExecutor(ref IImageFilterExecutor? executor);

    private Action<MewVGImage>? GetImageDisposeHandler()
    {
        Action<MewVGImage>? handler = null;
        TryGetImageDisposeHandler(ref handler);
        return handler;
    }

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

    private IRenderSurface CreatePixelSurface(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha)
    {
        IRenderSurface? rt = null;
        bool handled = false;
        TryCreatePixelSurface(pixelWidth, pixelHeight, dpiScale, hasAlpha, ref handled, ref rt);
        if (handled && rt != null)
        {
            return rt;
        }

        throw new NotSupportedException("MewVG backend does not support pixel render surfaces on this platform.");
    }

    public IRenderResourceCache? ResourceCache => _renderResourceCache;

    public IRenderEffectDevice? Effects => null;

    public IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor)
        => CreatePixelSurface(
            descriptor.PixelWidth,
            descriptor.PixelHeight,
            descriptor.DpiScale,
            descriptor.RequiredCapabilities.HasFlag(SurfaceCapabilities.Alpha));

    public IGraphicsContext CreateContext(IRenderSurface surface)
        => surface.Capabilities.HasFlag(SurfaceCapabilities.Renderable)
            ? CreateContext((IRenderTarget)surface)
            : throw new NotSupportedException(
                $"{GetType().Name} can only create contexts for renderable surfaces.");

    public IImage CreateImageView(IRenderSurface surface)
        => surface is IPixelBufferSource pixelSource
            ? CreateImageView(pixelSource)
            : throw new NotSupportedException(
                $"{GetType().Name} can only create image views for pixel-backed surfaces.");

    public IImage CreateImageView(IExternalRasterSource source)
        => CreateExternalRasterImage(source);

    public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes)
        => RenderDeviceFactoryHelpers.TryReadPixels(source, destination, destinationStrideBytes);

    public IRenderOperation RequestReadback(IRenderSurface source)
        => RenderDeviceFactoryHelpers.RequestReadback(source);

    public IRenderOperation FlushAsyncWork() => RenderOperation.Completed;

    public void Dispose()
    {
        _renderResourceCache.Dispose();

        foreach (var (_, resources) in _windows)
            resources.Dispose();
        _windows.Clear();
        DisposePlatformResources();
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

    partial void TryReleaseWindowResources(nint hwnd);

    partial void TryCreatePixelSurface(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha, ref bool handled, ref IRenderSurface? renderTarget);

    partial void TryGetImageDisposeHandler(ref Action<MewVGImage>? handler);

    partial void DisposePlatformResources();

    public bool Present(Window window, IWindowSurface surface, double opacity)
    {
        bool handled = false;
        bool result = false;
        TryPresentWindowSurface(window, surface, opacity, ref handled, ref result);
        return handled && result;
    }

    partial void TryPresentWindowSurface(Window window, IWindowSurface surface, double opacity, ref bool handled, ref bool result);

    /// <summary>
    /// Activates the platform-specific worker rendering context on the calling thread
    /// (Win32/X11: shared GL HGLRC; macOS Metal: no-op since MTLDevice is thread-free).
    /// Returns an <see cref="IDisposable"/> that releases the activation when disposed.
    /// Required wrapper for any worker thread that intends to call <see cref="IRenderDevice.CreateSurface"/>
    /// or <see cref="IRenderDevice.CreateContext(IRenderSurface)"/>.
    /// </summary>
    public IDisposable AcquireBackgroundRenderScope() => AcquireBackgroundRenderScopeCore();

    private partial IDisposable AcquireBackgroundRenderScopeCore();
}

/// <summary>Returned from backends whose <see cref="IGraphicsFactory.AcquireBackgroundRenderScope"/>
/// has nothing per-thread to manage (D2D MULTI_THREADED, Metal, CPU/GDI).</summary>
internal sealed class MewVGNoOpRenderScope : IDisposable
{
    public static readonly MewVGNoOpRenderScope Instance = new();
    public void Dispose() { }
}
