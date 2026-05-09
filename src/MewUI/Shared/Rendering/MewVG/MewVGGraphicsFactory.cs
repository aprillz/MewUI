using System.Collections.Concurrent;

using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Rendering.Filters;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.MewVG;

public sealed partial class MewVGGraphicsFactory : IGraphicsFactory, IRenderDevice, IWindowResourceReleaser, IWindowSurfaceSelector, IWindowSurfacePresenter
{
    public static MewVGGraphicsFactory Instance => field ??= new MewVGGraphicsFactory();

    private readonly ConcurrentDictionary<nint, IDisposable> _windows = new();
    private readonly RenderResourceCache _renderResourceCache = new();

    private MewVGGraphicsFactory() { }

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
    /// Heuristic for whether async PBO upload is worth the per-image overhead
    /// (1 GL texture + 2 PBOs allocated, fence per upload). Tiny static images
    /// see no benefit; large or streaming sources do.
    /// </summary>
    private static bool QualifiesForPboUpload(IPixelBufferSource source)
    {
        // Below ~1 MB the per-frame sync upload cost (~30μs at PCIe 4.0) is dwarfed
        // by the PBO/fence overhead. Threshold tuned conservatively — profile with
        // representative streaming workloads to revise.
        const long MinByteSize = 1L * 1024 * 1024;
        long byteSize = (long)source.PixelWidth * source.PixelHeight * 4;
        if (byteSize < MinByteSize) return false;

        // GPU-resident sources already pay a sync barrier on Lock (LockMode.Readback)
        // — staging that through PBO doesn't help because the readback dominates.
        if (source.LockMode == LockMode.Readback) return false;

        return true;
    }

    private IImage CreateExternalLockedTextureImage(IExternalLockedTexture texture)
        => new MewVGExternalLockedImage(texture);

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

    public IBitmapRenderTarget CreateBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale = 1.0, bool hasAlpha = true)
    {
        IBitmapRenderTarget? rt = null;
        bool handled = false;
        TryCreateBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale, hasAlpha, ref handled, ref rt);
        if (handled && rt != null)
        {
            return rt;
        }

        throw new NotSupportedException("MewVG backend does not support bitmap render targets on this platform.");
    }

    public IRenderResourceCache? ResourceCache => _renderResourceCache;

    public IRenderEffectDevice? Effects => null;

    public IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor)
        => CreateBitmapRenderTarget(
            descriptor.PixelWidth,
            descriptor.PixelHeight,
            descriptor.DpiScale,
            descriptor.RequiredCapabilities.HasFlag(SurfaceCapabilities.Alpha));

    public IGraphicsContext CreateContext(IRenderSurface surface)
        => RenderDeviceFactoryHelpers.CreateContext(this, surface);

    public IImage CreateImageView(IRenderSurface surface)
        => RenderDeviceFactoryHelpers.CreateImageView(this, surface);

    public IImage CreateImageView(IExternalSampleSource source)
    {
        if (source is ExternalLockedTextureSampleSource locked)
        {
            return CreateExternalLockedTextureImage(locked.Texture);
        }

        throw new NotSupportedException(
            $"{GetType().Name} does not support external sample sources of type {source.GetType().Name}.");
    }

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

    partial void TryCreateBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha, ref bool handled, ref IBitmapRenderTarget? renderTarget);

    partial void TryGetImageDisposeHandler(ref Action<MewVGImage>? handler);

    partial void TryGetPreferredSurfaceKind(ref bool handled, ref WindowSurfaceKind kind);

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
