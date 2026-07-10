using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Win32;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Direct2D;

public sealed unsafe partial class Direct2DGraphicsFactory : IGraphicsFactory, IRenderDevice, IGpuInteropInvalidationSource, IWindowResourceReleaser, IWin32TransparencyCapabilities, IWindowSurfacePresenter, IDisposable
{
    public const string BackendIdentifier = "Direct2D";

    public string Backend => BackendIdentifier;

    public event EventHandler<GpuInteropInvalidatedEventArgs>? GpuInteropInvalidated;

    /// <summary>
    /// When DirectComposition is present (Win8+), D2D presents transparent windows via a DXGI
    /// swap-chain (premultiplied alpha) attached to a <c>WS_EX_NOREDIRECTIONBITMAP</c> window,
    /// avoiding the per-frame layered-DIB readback. On Win7 (no DirectComposition) it falls back
    /// to the layered (Bitmap) path that <see cref="Win32TransparencyMode.Bitmap"/> drives.
    /// </summary>
    public Win32TransparencyMode TransparencyMode =>
        Dcomp.IsAvailable ? Win32TransparencyMode.Surface : Win32TransparencyMode.Bitmap;

    private nint _d2dFactory;
    private nint _dwriteFactory;
    private bool _initialized;
    private bool _hasFactory1;
    private nint _defaultFixedStrokeStyle;

    private readonly TextResourceTracker _textTracker = new()
    {
        ReleaseNativeHandle = handle => { if (handle != 0) ComHelpers.Release(handle); }
    };

    internal readonly DWriteTextFormatCache TextFormatCache = new();

    private readonly object _rtLock = new();
    private readonly Dictionary<nint, CachedWindowTarget> _windowTargets = new();
    private readonly HashSet<nint> _externalDxgiDeviceContextWindowTargets = [];
    private readonly Dictionary<nint, long> _lastExternalMismatchNotificationTicks = new();
    private readonly Dictionary<StrokeStyle, nint> _strokeStyles = new();
    private readonly RenderResourceCache _renderResourceCache = new();
    // Opaque windows use ID2D1HwndRenderTarget by default. Windows that render external
    // DXGI images are promoted to a device-context swap-chain target because those
    // images need ID2D1DeviceContext bitmap creation and same-device resource binding.
    private const bool UseSwapChainForOpaqueWindows = false;

    private enum WindowTargetMode
    {
        HwndRenderTarget,
        SwapChainDeviceContext,
        TransparentComposition,
    }

    internal Direct2DGraphicsFactory() { }

    public void Dispose()
    {
        _renderResourceCache.Dispose();
        DisposeLayeredTargets();

        lock (_rtLock)
        {
            foreach (var (_, entry) in _windowTargets)
            {
                ComHelpers.Release(entry.RenderTarget);
            }

            _windowTargets.Clear();
        }

        lock (_rtLock)
        {
            foreach (var (_, ss) in _strokeStyles)
                ComHelpers.Release(ss);
            _strokeStyles.Clear();
        }

        TextFormatCache.ReleaseAll();

        foreach (var (_, col) in _privateFontCollections)
            ComHelpers.Release(col);
        _privateFontCollections.Clear();

        ComHelpers.Release(_defaultFixedStrokeStyle);
        _defaultFixedStrokeStyle = 0;

        _gpuDeviceState = GpuDeviceState.Disposed;
        ResetGpuDeviceChain();

        ComHelpers.Release(_dwriteFactory);
        _dwriteFactory = 0;
        ComHelpers.Release(_d2dFactory);
        _d2dFactory = 0;
        _hasFactory1 = false;
        _initialized = false;
    }

    private void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        Ole32.CoInitializeEx(0, Ole32.COINIT_APARTMENTTHREADED);

        // MULTI_THREADED: D2D adds internal locking on factory and resources so multiple
        // threads can call into the same factory / device / RTs. Required for background
        // rendering (offscreen RT created on a worker thread, presented on the UI thread).
        // SINGLE_THREADED is faster (no locks) but caller must guarantee single-threaded
        // access across the entire D2D resource graph - incompatible with our worker-
        // rendered pattern tile.
        // Try ID2D1Factory1 (Windows 8+) for D2D1_STROKE_TRANSFORM_TYPE_FIXED support.
        int hr = D2D1.D2D1CreateFactory(D2D1_FACTORY_TYPE.MULTI_THREADED, D2D1.IID_ID2D1Factory1, 0, out _d2dFactory);
        if (hr >= 0 && _d2dFactory != 0)
        {
            _hasFactory1 = true;
        }
        else
        {
            hr = D2D1.D2D1CreateFactory(D2D1_FACTORY_TYPE.MULTI_THREADED, D2D1.IID_ID2D1Factory, 0, out _d2dFactory);
            if (hr < 0 || _d2dFactory == 0)
            {
                throw new InvalidOperationException($"D2D1CreateFactory failed: 0x{hr:X8}");
            }
        }

        hr = DWrite.DWriteCreateFactory(DWRITE_FACTORY_TYPE.SHARED, DWrite.IID_IDWriteFactory, out _dwriteFactory);
        if (hr < 0 || _dwriteFactory == 0)
        {
            throw new InvalidOperationException($"DWriteCreateFactory failed: 0x{hr:X8}");
        }

        if (_hasFactory1)
        {
            // NORMAL transform type: stroke width scales with the render target's transform
            // (matches Skia/WPF/Avalonia/SVG defaults). Crispness is preserved by
            // device-pixel snapping in the context's QuantizeStrokeDip.
            var defaultProps = new D2D1_STROKE_STYLE_PROPERTIES1(
                startCap: D2D1_CAP_STYLE.FLAT,
                endCap: D2D1_CAP_STYLE.FLAT,
                dashCap: D2D1_CAP_STYLE.FLAT,
                lineJoin: D2D1_LINE_JOIN.MITER,
                miterLimit: 10f,
                dashStyle: D2D1_DASH_STYLE.SOLID,
                dashOffset: 0f,
                transformType: D2D1_STROKE_TRANSFORM_TYPE.NORMAL);
            D2D1VTable.CreateStrokeStyle1(
                (ID2D1Factory*)_d2dFactory, defaultProps,
                ReadOnlySpan<float>.Empty, out _defaultFixedStrokeStyle);
        }

        _initialized = true;
    }

    /// <summary>
    /// Returns the cached <c>ID2D1StrokeStyle*</c> for <paramref name="ss"/>, creating it on
    /// first use. <see cref="StrokeStyle.Default"/> takes a lock-free fast path via the
    /// pre-created <see cref="_defaultFixedStrokeStyle"/> handle, since draw calls query this
    /// on every stroke.
    /// </summary>
    internal nint GetOrCreateStrokeStyle(StrokeStyle ss)
    {
        if (_defaultFixedStrokeStyle != 0 && ss == StrokeStyle.Default)
        {
            return _defaultFixedStrokeStyle;
        }

        EnsureInitialized();
        lock (_rtLock)
        {
            if (_strokeStyles.TryGetValue(ss, out nint handle)) return handle;

            float[]? dashes = null;
            if (ss.IsDashed && ss.DashArray != null)
            {
                dashes = new float[ss.DashArray.Count];
                for (int i = 0; i < ss.DashArray.Count; i++)
                    dashes[i] = (float)ss.DashArray[i];
            }

            int hr;
            if (_hasFactory1)
            {
                var props1 = new D2D1_STROKE_STYLE_PROPERTIES1(
                    startCap: MapLineCap(ss.LineCap),
                    endCap: MapLineCap(ss.LineCap),
                    dashCap: MapLineCap(ss.LineCap),
                    lineJoin: MapLineJoin(ss.LineJoin),
                    miterLimit: (float)Math.Max(1.0, ss.MiterLimit),
                    dashStyle: ss.IsDashed ? D2D1_DASH_STYLE.CUSTOM : D2D1_DASH_STYLE.SOLID,
                    dashOffset: (float)ss.DashOffset,
                    transformType: D2D1_STROKE_TRANSFORM_TYPE.NORMAL);
                hr = D2D1VTable.CreateStrokeStyle1(
                    (ID2D1Factory*)_d2dFactory, props1,
                    dashes != null ? dashes.AsSpan() : ReadOnlySpan<float>.Empty,
                    out handle);
            }
            else
            {
                var props = new D2D1_STROKE_STYLE_PROPERTIES(
                    startCap: MapLineCap(ss.LineCap),
                    endCap: MapLineCap(ss.LineCap),
                    dashCap: MapLineCap(ss.LineCap),
                    lineJoin: MapLineJoin(ss.LineJoin),
                    miterLimit: (float)Math.Max(1.0, ss.MiterLimit),
                    dashStyle: ss.IsDashed ? D2D1_DASH_STYLE.CUSTOM : D2D1_DASH_STYLE.SOLID,
                    dashOffset: (float)ss.DashOffset);
                hr = D2D1VTable.CreateStrokeStyle(
                    (ID2D1Factory*)_d2dFactory, props,
                    dashes != null ? dashes.AsSpan() : ReadOnlySpan<float>.Empty,
                    out handle);
            }

            if (hr >= 0 && handle != 0)
                _strokeStyles[ss] = handle;

            return handle;
        }
    }

    private static D2D1_CAP_STYLE MapLineCap(StrokeLineCap cap) => cap switch
    {
        StrokeLineCap.Round => D2D1_CAP_STYLE.ROUND,
        StrokeLineCap.Square => D2D1_CAP_STYLE.SQUARE,
        _ => D2D1_CAP_STYLE.FLAT,
    };

    private static D2D1_LINE_JOIN MapLineJoin(StrokeLineJoin join) => join switch
    {
        StrokeLineJoin.Round => D2D1_LINE_JOIN.ROUND,
        StrokeLineJoin.Bevel => D2D1_LINE_JOIN.BEVEL,
        _ => D2D1_LINE_JOIN.MITER_OR_BEVEL, // auto-bevel when miter limit is exceeded
    };

    public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal, bool italic = false, bool underline = false, bool strikethrough = false)
    {
        EnsureInitialized();
        family = ValidateFamilyName(family);
        var (resolvedFamily, fontCollection) = ResolveWithCollection(family);
        return new DirectWriteFont(resolvedFamily, size, weight, italic, underline, strikethrough, _dwriteFactory, fontCollection);
    }

    public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal, bool italic = false, bool underline = false, bool strikethrough = false)
    {
        EnsureInitialized();
        family = ValidateFamilyName(family);
        var (resolvedFamily, fontCollection) = ResolveWithCollection(family);
        return new DirectWriteFont(resolvedFamily, size, weight, italic, underline, strikethrough, _dwriteFactory, fontCollection, dpi);
    }

    // Cache: familyName → DWrite custom font collection (nint)
    private readonly Dictionary<string, nint> _privateFontCollections = new(StringComparer.OrdinalIgnoreCase);

    private (string family, nint fontCollection) ResolveWithCollection(string familyOrPath)
    {
        familyOrPath = ValidateFamilyName(familyOrPath);
        var resolved = FontRegistry.Resolve(familyOrPath);
        if (resolved != null)
        {
            // Ensure GDI registration (for GDI backend compatibility)
            if (OperatingSystem.IsWindows())
                Win32Fonts.EnsurePrivateFontFamily(resolved.Value.FilePath);

            // Get or create DWrite custom font collection for this private font
            var fontCollection = GetOrCreatePrivateCollection(resolved.Value.FamilyName, resolved.Value.FilePath);
            return (resolved.Value.FamilyName, fontCollection);
        }

        // Legacy file path
        if (OperatingSystem.IsWindows() && FontResources.LooksLikeFontFilePath(familyOrPath))
        {
            var path = Path.GetFullPath(familyOrPath);
            Win32Fonts.EnsurePrivateFontFamily(path);
            var family = FontResources.TryGetParsedFamilyName(path, out var parsed) && !string.IsNullOrWhiteSpace(parsed)
                ? parsed : Path.GetFileNameWithoutExtension(path);
            var fontCollection = GetOrCreatePrivateCollection(family, path);
            return (family, fontCollection);
        }

        return (familyOrPath, 0); // System font, no custom collection
    }

    private static string ValidateFamilyName(string? family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            throw new ArgumentException("Font family must be provided by the caller.", nameof(family));
        }

        return family.Trim();
    }

    private nint GetOrCreatePrivateCollection(string familyName, string filePath)
    {
        if (_privateFontCollections.TryGetValue(familyName, out var cached))
            return cached;

        var factory = (IDWriteFactory*)_dwriteFactory;
        var collection = DWritePrivateFontCollection.CreateCollection(factory, [filePath]);
        if (collection != 0)
            _privateFontCollections[familyName] = collection;
        return collection;
    }

    private void RefreshSystemFontCollection()
    {
        EnsureInitialized();
        var factory = (IDWriteFactory*)_dwriteFactory;
        int hr = DWriteVTable.GetSystemFontCollection(factory, out var collection, checkForUpdates: true);
        if (hr >= 0 && collection != 0)
        {
            ComHelpers.Release(collection);
        }
    }

    private string ResolveWin32FontFamilyOrFile(string familyOrPath)
    {
        // 1. Check FontRegistry (registered via FontResources.Register)
        var resolved = FontRegistry.Resolve(familyOrPath);
        if (resolved != null)
        {
            if (OperatingSystem.IsWindows() && Win32Fonts.EnsurePrivateFontFamily(resolved.Value.FilePath))
            {
                RefreshSystemFontCollection();
            }
            return resolved.Value.FamilyName;
        }

        // 2. Legacy: file path directly in FontFamily
        if (!OperatingSystem.IsWindows() || !FontResources.LooksLikeFontFilePath(familyOrPath))
        {
            return familyOrPath;
        }

        var path = Path.GetFullPath(familyOrPath);
        if (Win32Fonts.EnsurePrivateFontFamily(path))
        {
            RefreshSystemFontCollection();
        }

        return FontResources.TryGetParsedFamilyName(path, out var parsed) && !string.IsNullOrWhiteSpace(parsed)
            ? parsed
            : Path.GetFileNameWithoutExtension(path);
    }

    public IImage CreateImageFromFile(string path) =>
        CreateImageFromBytes(File.ReadAllBytes(path));

    public IImage CreateImageFromBytes(byte[] data) =>
        ImageDecoders.TryDecode(data, out var bmp)
            ? new Direct2DImage(bmp)
            : throw new NotSupportedException(
                $"Unsupported image format. Built-in decoders: BMP/PNG/JPEG. Detected: {ImageDecoders.DetectFormatId(data) ?? "unknown"}.");

    /// <summary>
    /// Wraps an externally-created native <c>ID2D1Bitmap*</c> as an <see cref="IImage"/>.
    /// Intended for cross-API interop scenarios where the caller produced the bitmap on
    /// the same <c>ID2D1Device</c> as this factory (use <see cref="NativeD2DDevice"/>),
    /// typically via <c>ID2D1DeviceContext::CreateBitmapFromDxgiSurface</c> against a
    /// D3D11 video frame or similar shared GPU resource.
    /// </summary>
    /// <param name="nativeBitmap"><c>ID2D1Bitmap*</c> (or derived). The factory calls
    /// <c>AddRef</c> internally so the caller can <c>Release</c> their own reference
    /// independently after this returns.</param>
    /// <param name="pixelWidth">Bitmap pixel width.</param>
    /// <param name="pixelHeight">Bitmap pixel height.</param>
    /// <returns>An <see cref="IImage"/> that draws the wrapped bitmap. Disposing it
    /// releases the factory's <c>AddRef</c>; the underlying bitmap is freed when its
    /// total refcount reaches zero.</returns>
    public IImage CreateImageFromNativeBitmap(nint nativeBitmap, int pixelWidth, int pixelHeight)
        => new Direct2DNativeBitmapImage(nativeBitmap, pixelWidth, pixelHeight);

    public IGraphicsContext CreateContext(IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target is WindowRenderTarget windowTarget)
        {
            if (windowTarget.Surface is not IWin32WindowSurface win32Surface || win32Surface.Hwnd == 0)
            {
                throw new ArgumentException("Direct2D backend requires a Win32 window surface.", nameof(target));
            }

            return CreateContextCore(
                win32Surface.Hwnd,
                windowTarget.DpiScale,
                win32Surface.TransparentComposition,
                win32Surface.DisplayIdentity);
        }

        if (target is Direct2DPixelRenderSurface surfaceTarget)
        {
            return CreatePixelSurfaceContext(surfaceTarget);
        }

        if (target is Direct2DGpuPixelRenderSurface gpuTarget)
        {
            return CreateGpuPixelSurfaceContext(gpuTarget);
        }

        if (target is ICpuPixelSurface)
        {
            throw new ArgumentException(
                $"Render surface was created by a different backend. " +
                $"Use {nameof(CreateSurface)} from the same factory.",
                nameof(target));
        }

        throw new NotSupportedException($"Unsupported render target type: {target.GetType().Name}");
    }

    private IGraphicsContext CreateContextCore(nint hwnd, double dpiScale, bool transparentComposition = false, PlatformDisplayIdentity displayIdentity = default)
    {
        EnsureInitialized();

        // Pre-warm the cached window target so the first BeginFrame doesn't pay the cost.
        GetOrCreateCachedWindowTarget(hwnd, dpiScale, transparentComposition, displayIdentity);

        var ctx = new Direct2DGraphicsContext(
            this,
            hwnd,
            _d2dFactory,
            _dwriteFactory,
            _defaultFixedStrokeStyle,
            onRecreateTarget: () => InvalidateCachedWindowTarget(hwnd),
            onPresentTarget: () => PresentCachedWindowTarget(hwnd),
            ownsRenderTarget: false,
            textFormatCache: TextFormatCache,
            resolveRenderTarget: t =>
            {
                bool transparent = t is WindowRenderTarget wrt
                    && wrt.Surface is Platform.Win32.IWin32WindowSurface ws
                    && ws.TransparentComposition;
                var currentDisplay = t is WindowRenderTarget currentWrt
                    ? currentWrt.Surface.DisplayIdentity
                    : displayIdentity;
                return GetOrCreateCachedWindowTarget(hwnd, t.DpiScale, transparent, currentDisplay);
            });
        ctx.TextTracker = _textTracker;
        return ctx;
    }

    private IGraphicsContext CreatePixelSurfaceContext(Direct2DPixelRenderSurface target)
    {
        EnsureInitialized();

        var d2dFactory = _d2dFactory;
        var ctx = new Direct2DGraphicsContext(
            this,
            hwnd: 0,
            d2dFactory: _d2dFactory,
            dwriteFactory: _dwriteFactory,
            defaultStrokeStyle: _defaultFixedStrokeStyle,
            onRecreateTarget: null,
            onPresentTarget: () => { target.IncrementVersion(); return 0; },
            ownsRenderTarget: false,
            textFormatCache: TextFormatCache,
            resolveRenderTarget: t => ((Direct2DPixelRenderSurface)t).GetOrCreateDcRenderTarget(d2dFactory));
        return ctx;
    }

    /// <summary>Builds a graphics context for a GPU-resident pixel surface. The context's
    /// <c>OnBeginFrame</c> branches into <c>BeginGpuPixelSurfaceFrame</c> when the target is a
    /// <see cref="Direct2DGpuPixelRenderSurface"/>, using the shared filter device context
    /// + <c>SetTarget</c> instead of a DC render target - keeps the filter pipeline on-GPU
    /// end-to-end.</summary>
    private IGraphicsContext CreateGpuPixelSurfaceContext(Direct2DGpuPixelRenderSurface target)
    {
        EnsureInitialized();

        // resolveRenderTarget is unused for GPU targets - the GPU branch in OnBeginFrame
        // wires _renderTarget directly from the shared DC.
        var ctx = new Direct2DGraphicsContext(
            this,
            hwnd: 0,
            d2dFactory: _d2dFactory,
            dwriteFactory: _dwriteFactory,
            defaultStrokeStyle: _defaultFixedStrokeStyle,
            onRecreateTarget: null,
            onPresentTarget: () => { target.IncrementVersion(); return 0; },
            ownsRenderTarget: false,
            textFormatCache: TextFormatCache,
            resolveRenderTarget: null);
        return ctx;
    }

    public IGraphicsContext CreateMeasurementContext(uint dpi)
    {
        EnsureInitialized();
        var ctx = new Direct2DMeasurementContext(_dwriteFactory, TextFormatCache);
        ctx.TextTracker = _textTracker;
        return ctx;
    }

    private IRenderSurface CreateCpuPixelSurface(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha)
        => new Direct2DPixelRenderSurface(pixelWidth, pixelHeight, dpiScale, hasAlpha);

    /// <summary>
    /// Returns a GPU-resident <see cref="Direct2DGpuPixelRenderSurface"/> when the shared
    /// device context is available, falling back to the DIB-backed
    /// <see cref="Direct2DPixelRenderSurface"/> otherwise. The GPU path keeps filter graphs
    /// fully on-GPU (CreateBitmap1 with TARGET option → effects sample directly →
    /// downstream draws via SetTarget + DrawImage); the only readback happens when CPU
    /// executors call <c>Lock</c>, in which case the lock release path also writes the
    /// modified buffer back to the GPU via <c>CopyFromMemory</c> so subsequent effects see
    /// the up-to-date pixels.
    /// </summary>
    private IRenderSurface CreateOffscreenSurfaceTarget(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha)
    {
        if (SharedFilterDeviceContext != 0)
        {
            try
            {
                return new Direct2DGpuPixelRenderSurface(this, pixelWidth, pixelHeight, dpiScale, hasAlpha);
            }
            catch
            {
                // GPU init failed (out of GPU memory, requested size > device limit, etc.).
                // Fall through to DIB so the filter pipeline continues to render - slowly,
                // but it renders.
            }
        }
        return new Direct2DPixelRenderSurface(pixelWidth, pixelHeight, dpiScale, hasAlpha);
    }

    public IRenderResourceCache? ResourceCache => _renderResourceCache;

    public IRenderEffectDevice? Effects => null;

    public IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor)
    {
        var target = RenderDeviceFactoryHelpers.RequiresCpuPixels(descriptor)
            ? CreateCpuPixelSurface(
                descriptor.PixelWidth,
                descriptor.PixelHeight,
                descriptor.DpiScale,
                descriptor.RequiredCapabilities.HasFlag(SurfaceCapabilities.Alpha))
            : CreateOffscreenSurfaceTarget(
                descriptor.PixelWidth,
                descriptor.PixelHeight,
                descriptor.DpiScale,
                descriptor.RequiredCapabilities.HasFlag(SurfaceCapabilities.Alpha));

        return target;
    }

    public IGraphicsContext CreateContext(IRenderSurface surface)
        => surface.Capabilities.HasFlag(SurfaceCapabilities.Renderable)
            ? CreateContext((IRenderTarget)surface)
            : throw new NotSupportedException(
                $"{GetType().Name} can only create contexts for renderable surfaces.");

    public IImage CreateImageView(IRenderSurface surface)
    {
        // GPU-resident surfaces that also implement IExternalRasterSource go through the
        // DXGI bridge so any D2D DC can sample them zero-copy. Prefer this path over the
        // IPixelBufferSource fallback - otherwise Direct2DGpuPixelRenderSurface (created
        // on the SharedFilterDC) shows stale cached pixels when drawn from a window DC.
        // surface.DpiScale is forwarded so the bridge bitmap reports the correct logical
        // size when sampled from an RT with a different dpi.
        if (surface is IExternalRasterSource externalSource)
            return CreateImageView(externalSource, surface.DpiScale);

        if (surface is IPixelBufferSource pixelSource)
            return CreateImageView(pixelSource);

        throw new NotSupportedException(
            $"{GetType().Name} can only create image views for pixel-backed or externally-rastered surfaces.");
    }

    public IImage CreateImageView(IPixelBufferSource source)
        => new Direct2DImage(source);

    public IImage CreateImageView(IExternalRasterSource source)
        => CreateImageView(source, dpiScale: 1.0);

    internal IImage CreateImageView(IExternalRasterSource source, double dpiScale)
    {
        ArgumentNullException.ThrowIfNull(source);

        var lease = source.Acquire();
        nint cachedDxgiSurface = lease.NativeAlternateHandle;
        nint dxgiSurface = cachedDxgiSurface;
        if (dxgiSurface == 0 && lease.NativeHandle != 0)
        {
            _ = ComHelpers.QueryInterface(lease.NativeHandle, D2D1.IID_IDXGISurface, out dxgiSurface);
        }

        if (dxgiSurface == 0)
        {
            lease.Dispose();
            throw new NotSupportedException(
                $"{GetType().Name} could not acquire an IDXGISurface from {source.GetType().Name}.");
        }

        try
        {
            var affinity = lease is IGpuResourceAffinityProvider affinityProvider
                ? affinityProvider.Affinity
                : null;
            return new Direct2DDxgiSurfaceImage(
                dxgiSurface,
                lease.PixelWidth,
                lease.PixelHeight,
                source.AlphaMode,
                affinity,
                lease: null,
                preferDeviceContextBitmap: false,
                dpiScale: dpiScale);
        }
        finally
        {
            if (dxgiSurface != cachedDxgiSurface)
            {
                ComHelpers.Release(dxgiSurface);
            }

            lease.Dispose();
        }
    }

    public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes)
        => RenderDeviceFactoryHelpers.TryReadPixels(source, destination, destinationStrideBytes);

    public IRenderOperation RequestReadback(IRenderSurface source)
        => RenderDeviceFactoryHelpers.RequestReadback(source);

    public IRenderOperation FlushAsyncWork() => RenderOperation.Completed;

    public Filters.IImageFilterExecutor CreateImageFilterExecutor()
        => new Direct2DImageFilterExecutor(this);

    public void ReleaseWindowResources(nint windowHandle)
    {
        lock (_rtLock)
        {
            // The context is owned by the Window and disposed there. Here we only
            // release factory-owned native handles (COM render targets).
            _externalDxgiDeviceContextWindowTargets.Remove(windowHandle);
            InvalidateCachedWindowTargetLocked(windowHandle);
        }

        ReleaseLayeredTarget(windowHandle);
    }

    private void InvalidateAllFactoryRenderTargetsForDeviceLost()
    {
        lock (_rtLock)
        {
            foreach (var (_, entry) in _windowTargets)
            {
                ComHelpers.Release(entry.RenderTarget);
            }

            _windowTargets.Clear();
        }

        OnGpuInteropInvalidated(new GpuInteropInvalidatedEventArgs(
            GpuInteropInvalidationReason.DeviceLost,
            renderTargetDeviceChanged: true,
            externalResourceMismatch: true));
    }

    private void OnGpuInteropInvalidated(GpuInteropInvalidatedEventArgs e)
        => GpuInteropInvalidated?.Invoke(this, e);

    private void QueueGpuInteropInvalidated(GpuInteropInvalidatedEventArgs e)
    {
        if (GpuInteropInvalidated is null)
        {
            return;
        }

        System.Threading.ThreadPool.QueueUserWorkItem(static state =>
        {
            var (factory, args) = ((Direct2DGraphicsFactory, GpuInteropInvalidatedEventArgs))state!;
            factory.OnGpuInteropInvalidated(args);
        }, (this, e));
    }

    private void InvalidateCachedWindowTarget(nint hwnd)
    {
        lock (_rtLock)
        {
            InvalidateCachedWindowTargetLocked(hwnd);
        }
    }

    private void InvalidateCachedWindowTargetLocked(nint hwnd)
    {
        if (_windowTargets.Remove(hwnd, out var entry))
        {
            entry.DisposeNativeHandles();
            // Context is left intact; it re-resolves the render target on next BeginFrame
            // via the resolveRenderTarget delegate.
        }
    }

    private (nint renderTarget, int generation) GetOrCreateCachedWindowTarget(nint hwnd, double dpiScale, bool transparentComposition = false, PlatformDisplayIdentity displayIdentity = default)
    {
        var rc = D2D1VTable.GetClientRect(hwnd);
        uint w = (uint)Math.Max(1, rc.Width);
        uint h = (uint)Math.Max(1, rc.Height);
        float dpi = (float)(96.0 * dpiScale);
        var presentOptions = GetPresentOptions();
        nint monitor = GetMonitor(hwnd, displayIdentity);

        lock (_rtLock)
        {
            int generation = 0;
            var mode = GetWindowTargetMode(hwnd, transparentComposition);
            // TransparentComposition uses CreateSwapChainForComposition + DirectComposition, which
            // require Windows 8+. SwapChainDeviceContext (opaque external-DXGI content, e.g. the GPU
            // bitmap cache) uses CreateSwapChainForHwnd and works on Win7+PU, so it must NOT be
            // gated on DirectComposition: forcing it to an HwndRenderTarget gives a separate D2D
            // device that cannot bind the shared-device GPU bitmaps, breaking the bitmap cache.
            bool swapChainEligible = mode == WindowTargetMode.SwapChainDeviceContext
                || (mode == WindowTargetMode.TransparentComposition && Dcomp.IsAvailable);
            if (swapChainEligible
                && SharedFilterDeviceContext != 0
                && _d2dDevice != 0
                && _d3dDevice != 0)
            {
                if (TryGetOrCreateCachedSwapChainTargetLocked(hwnd, w, h, dpi, presentOptions, mode, monitor, out var swapChainTarget))
                {
                    return swapChainTarget;
                }
            }

            // HWND render targets always use IGNORE - the DWM redirection surface
            // does not preserve per-pixel alpha from D2D Present.
            // For system backdrop, DWM extended frame treats opaque black (0,0,0) as glass.
            var requiredAlpha = D2D1_ALPHA_MODE.IGNORE;

            if (_windowTargets.TryGetValue(hwnd, out var entry) && entry.RenderTarget != 0)
            {
                if (entry.Width == w && entry.Height == h && entry.DpiX == dpi
                    && entry.PresentOptions == presentOptions && entry.AlphaMode == requiredAlpha && entry.Mode == WindowTargetMode.HwndRenderTarget)
                {
                    return (entry.RenderTarget, entry.Generation);
                }

                // If size/DPI changed, recreate the target. (Safer than calling ID2D1HwndRenderTarget::Resize via vtable indices.)
                entry.DisposeNativeHandles();
                entry.Generation++;
                generation = entry.Generation;
                _windowTargets.Remove(hwnd);
            }

            // Use PREMULTIPLIED alpha for transparent composition (system backdrop); IGNORE otherwise to keep ClearType.
            var alphaMode = requiredAlpha;
            var pixelFormat = new D2D1_PIXEL_FORMAT(0, alphaMode);
            var rtProps = new D2D1_RENDER_TARGET_PROPERTIES(D2D1_RENDER_TARGET_TYPE.DEFAULT, pixelFormat, 0, 0, 0, 0);
            var hwndProps = new D2D1_HWND_RENDER_TARGET_PROPERTIES(hwnd, new D2D1_SIZE_U(w, h), presentOptions);

            int hr = D2D1VTable.CreateHwndRenderTarget((ID2D1Factory*)_d2dFactory, ref rtProps, ref hwndProps, out var renderTarget);
            if (hr < 0 || renderTarget == 0)
            {
                throw new InvalidOperationException($"CreateHwndRenderTarget failed: 0x{hr:X8}");
            }

            D2D1VTable.SetDpi((ID2D1RenderTarget*)renderTarget, dpi, dpi);
            var created = new CachedWindowTarget(renderTarget, w, h, dpi, presentOptions, alphaMode, generation, WindowTargetMode.HwndRenderTarget, swapChain: 0, targetBitmap: 0)
            {
                Monitor = monitor,
                DeviceIdentity = GetFactoryDeviceIdentity(),
            };
            _windowTargets[hwnd] = created;
            return (renderTarget, generation);
        }
    }

    private WindowTargetMode GetWindowTargetMode(nint hwnd, bool transparentComposition)
    {
        if (transparentComposition)
        {
            return WindowTargetMode.TransparentComposition;
        }

        if (UseSwapChainForOpaqueWindows || _externalDxgiDeviceContextWindowTargets.Contains(hwnd))
        {
            return WindowTargetMode.SwapChainDeviceContext;
        }

        return WindowTargetMode.HwndRenderTarget;
    }

    internal bool RequireDeviceContextTargetForExternalDxgiContent(nint hwnd)
    {
        if (hwnd == 0)
        {
            return false;
        }

        lock (_rtLock)
        {
            if (!_externalDxgiDeviceContextWindowTargets.Add(hwnd))
            {
                return false;
            }

            if (_windowTargets.TryGetValue(hwnd, out var entry) && entry.Mode == WindowTargetMode.HwndRenderTarget)
            {
                entry.DisposeNativeHandles();
                _windowTargets.Remove(hwnd);
            }

            return true;
        }
    }

    private bool TryGetOrCreateCachedSwapChainTargetLocked(nint hwnd, uint width, uint height, float dpi, D2D1_PRESENT_OPTIONS presentOptions, WindowTargetMode mode, nint monitor, out (nint renderTarget, int generation) target)
    {
        target = default;

        // Alpha mode is part of the cache key - switching between transparent and opaque
        // requires a new swap-chain (different DXGI_ALPHA_MODE) and a fresh window (the
        // WS_EX_NOREDIRECTIONBITMAP bit is fixed at CreateWindow time anyway, but if the
        // window survives a recreate we still need the cached target reissued).
        bool transparentComposition = mode == WindowTargetMode.TransparentComposition;
        var requiredAlpha = transparentComposition ? D2D1_ALPHA_MODE.PREMULTIPLIED : D2D1_ALPHA_MODE.IGNORE;
        int generation = 0;
        if (_windowTargets.TryGetValue(hwnd, out var existing) && existing.RenderTarget != 0)
        {
            if (existing.Mode == WindowTargetMode.HwndRenderTarget)
            {
                return false;
            }

            if (existing.Mode == mode
                && existing.Width == width
                && existing.Height == height
                && existing.DpiX == dpi
                && existing.AlphaMode == requiredAlpha)
            {
                target = (existing.RenderTarget, existing.Generation);
                return true;
            }

            if (existing.Mode == mode
                && existing.AlphaMode == requiredAlpha
                && existing.PresentOptions == presentOptions
                && existing.SwapChain != 0
                && existing.RenderTarget != 0
                && TryResizeSwapChainWindowTarget(existing, width, height, dpi, monitor))
            {
                target = (existing.RenderTarget, existing.Generation);
                return true;
            }

            existing.DisposeNativeHandles();
            existing.Generation++;
            generation = existing.Generation;
            _windowTargets.Remove(hwnd);
        }

        if (!TryCreateSwapChainWindowTarget(hwnd, width, height, dpi, presentOptions, generation, mode, monitor, out var created))
        {
            return false;
        }

        _windowTargets[hwnd] = created;
        target = (created.RenderTarget, created.Generation);
        return true;
    }

    private bool TryCreateSwapChainWindowTarget(nint hwnd, uint width, uint height, float dpi, D2D1_PRESENT_OPTIONS presentOptions, int generation, WindowTargetMode mode, nint monitor, out CachedWindowTarget target)
    {
        target = null!;

        nint dxgiFactory = 0;
        nint swapChain = 0;
        nint dxgiSurface = 0;
        nint deviceContext = 0;
        nint targetBitmap = 0;
        nint dcompDevice = 0;
        nint dcompTarget = 0;
        nint dcompVisual = 0;
        nint windowD3DDevice = _d3dDevice;
        nint windowD2DDevice = _d2dDevice;
        bool transparentComposition = mode == WindowTargetMode.TransparentComposition;

        // Transparent surfaces must use PREMULTIPLIED alpha end-to-end so DWM (via the
        // DirectComposition visual that wraps the swap-chain) blends against the desktop.
        // Opaque windows keep IGNORE so D2D can skip per-fragment blend math and
        // ClearType subpixel rendering remains valid.
        var alphaMode = transparentComposition ? DXGI_ALPHA_MODE.PREMULTIPLIED : DXGI_ALPHA_MODE.IGNORE;
        var d2dAlphaMode = transparentComposition ? D2D1_ALPHA_MODE.PREMULTIPLIED : D2D1_ALPHA_MODE.IGNORE;

        try
        {
            // CreateDXGIFactory2 is Win8.1+; on Win7 Platform Update / Win8.0 this falls back to
            // CreateDXGIFactory1 (still yields an IDXGIFactory2). Keeps the call from throwing
            // EntryPointNotFoundException on those OSes.
            int hr = Dxgi.CreateFactory2OrFallback(out dxgiFactory);
            if (hr < 0 || dxgiFactory == 0)
            {
                return false;
            }

            // CreateSwapChainForHwnd cannot be used on a WS_EX_NOREDIRECTIONBITMAP window
            // (no redirection surface to bind to). Transparent windows therefore go through
            // CreateSwapChainForComposition + a DirectComposition visual - DWM composes the
            // visual's swap-chain content with per-pixel alpha, no readback.
            var swapChainDesc = new DXGI_SWAP_CHAIN_DESC1(
                width,
                height,
                D2D1.DXGI_FORMAT_B8G8R8A8_UNORM,
                stereo: 0,
                sampleDesc: new DXGI_SAMPLE_DESC(count: 1, quality: 0),
                bufferUsage: Dxgi.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                bufferCount: 2,
                // STRETCH for both: composition swap-chains reject DXGI_SCALING_NONE
                // (DXGI_ERROR_INVALID_CALL - flip-hwnd only), and BitBlt needs STRETCH too.
                scaling: DXGI_SCALING.STRETCH,
                // Composition (transparent) requires the flip model. Opaque CreateSwapChainForHwnd
                // windows use the BitBlt model (DISCARD) instead: the flip model composes the
                // swap-chain directly in DWM, bypassing the redirection bitmap, so live resize shows
                // a black flash that WM_NCCALCSIZE/WVR_VALIDRECTS cannot fix. BitBlt presents through
                // the redirection surface, so DWM's resize honors the valid-rects (smooth like GDI).
                swapEffect: transparentComposition ? DXGI_SWAP_EFFECT.FLIP_SEQUENTIAL : DXGI_SWAP_EFFECT.DISCARD,
                alphaMode: alphaMode,
                flags: 0);

            if (transparentComposition)
            {
                // Transparent composition requires DirectComposition (Win8+). When absent the
                // window is routed to the layered (Bitmap) transparency path instead, so this
                // branch should not be reached; guard defensively to avoid the Win7 DComp calls.
                if (!Dcomp.IsAvailable)
                {
                    return false;
                }

                hr = Dxgi.CreateSwapChainForComposition(dxgiFactory, windowD3DDevice, swapChainDesc, out swapChain);
                if (hr < 0 || swapChain == 0)
                {
                    return false;
                }

                // Need an IDXGIDevice for DCompositionCreateDevice - the D3D11 device
                // implements it.
                if (ComHelpers.QueryInterface(windowD3DDevice, Dcomp.IID_IDXGIDevice, out var dxgiDevice) < 0 || dxgiDevice == 0)
                {
                    return false;
                }

                try
                {
                    hr = Dcomp.DCompositionCreateDevice(dxgiDevice, Dcomp.IID_IDCompositionDevice, out dcompDevice);
                }
                finally
                {
                    ComHelpers.Release(dxgiDevice);
                }

                if (hr < 0 || dcompDevice == 0)
                {
                    return false;
                }

                hr = Dcomp.CreateTargetForHwnd(dcompDevice, hwnd, topmost: true, out dcompTarget);
                if (hr < 0 || dcompTarget == 0)
                {
                    return false;
                }

                hr = Dcomp.CreateVisual(dcompDevice, out dcompVisual);
                if (hr < 0 || dcompVisual == 0)
                {
                    return false;
                }

                hr = Dcomp.SetContent(dcompVisual, swapChain);
                if (hr < 0)
                {
                    return false;
                }

                hr = Dcomp.SetRoot(dcompTarget, dcompVisual);
                if (hr < 0)
                {
                    return false;
                }

                hr = Dcomp.Commit(dcompDevice);
                if (hr < 0)
                {
                    return false;
                }
            }
            else
            {
                hr = Dxgi.CreateSwapChainForHwnd(dxgiFactory, windowD3DDevice, hwnd, swapChainDesc, out swapChain);
                if (hr < 0 || swapChain == 0)
                {
                    return false;
                }

                _ = Dxgi.MakeWindowAssociation(dxgiFactory, hwnd, Dxgi.DXGI_MWA_NO_ALT_ENTER);
            }

            hr = Dxgi.GetBuffer(swapChain, 0, D2D1.IID_IDXGISurface, out dxgiSurface);
            if (hr < 0 || dxgiSurface == 0)
            {
                return false;
            }

            hr = D2D1VTable.CreateDeviceContext(windowD2DDevice, options: 0, out deviceContext);
            if (hr < 0 || deviceContext == 0)
            {
                return false;
            }

            var bitmapProps = new D2D1_BITMAP_PROPERTIES1(
                new D2D1_PIXEL_FORMAT(D2D1.DXGI_FORMAT_B8G8R8A8_UNORM, d2dAlphaMode),
                dpi,
                dpi,
                D2D1_BITMAP_OPTIONS.TARGET | D2D1_BITMAP_OPTIONS.CANNOT_DRAW,
                colorContext: 0);

            hr = D2D1VTable.CreateBitmapFromDxgiSurface((ID2D1DeviceContext*)deviceContext, dxgiSurface, bitmapProps, out targetBitmap);
            if (hr < 0 || targetBitmap == 0)
            {
                return false;
            }

            D2D1VTable.SetTarget((ID2D1DeviceContext*)deviceContext, targetBitmap);
            D2D1VTable.SetDpi((ID2D1RenderTarget*)deviceContext, dpi, dpi);

            target = new CachedWindowTarget(deviceContext, width, height, dpi, presentOptions, d2dAlphaMode, generation, mode, swapChain, targetBitmap)
            {
                DcompDevice = dcompDevice,
                DcompTarget = dcompTarget,
                DcompVisual = dcompVisual,
                Monitor = monitor,
                DeviceIdentity = GetD3D11DeviceIdentity(windowD3DDevice),
            };
            deviceContext = 0;
            swapChain = 0;
            targetBitmap = 0;
            dcompDevice = 0;
            dcompTarget = 0;
            dcompVisual = 0;
            return true;
        }
        catch (EntryPointNotFoundException)
        {
            // A Win8+ DXGI/DComp export is missing (older OS reached this path). Fall back to
            // the HwndRenderTarget / layered route rather than propagating a crash.
            return false;
        }
        catch (DllNotFoundException)
        {
            // dcomp.dll (or another optional library) is absent on this OS. Same fallback.
            return false;
        }
        finally
        {
            if (targetBitmap != 0)
            {
                ComHelpers.Release(targetBitmap);
            }

            if (deviceContext != 0)
            {
                ComHelpers.Release(deviceContext);
            }

            if (dxgiSurface != 0)
            {
                ComHelpers.Release(dxgiSurface);
            }

            if (swapChain != 0)
            {
                ComHelpers.Release(swapChain);
            }

            if (dcompVisual != 0)
            {
                ComHelpers.Release(dcompVisual);
            }

            if (dcompTarget != 0)
            {
                ComHelpers.Release(dcompTarget);
            }

            if (dcompDevice != 0)
            {
                ComHelpers.Release(dcompDevice);
            }

            if (dxgiFactory != 0)
            {
                ComHelpers.Release(dxgiFactory);
            }
        }
    }

    private bool TryResizeSwapChainWindowTarget(CachedWindowTarget entry, uint width, uint height, float dpi, nint monitor)
    {
        if (!entry.IsSwapChainBacked || entry.SwapChain == 0 || entry.RenderTarget == 0)
        {
            return false;
        }

        nint dxgiSurface = 0;
        nint targetBitmap = 0;
        try
        {
            D2D1VTable.SetTarget((ID2D1DeviceContext*)entry.RenderTarget, 0);

            if (entry.TargetBitmap != 0)
            {
                ComHelpers.Release(entry.TargetBitmap);
                entry.TargetBitmap = 0;
            }

            // Keep the DirectComposition target/visual/content graph intact; only swap-chain
            // buffers and the D2D target bitmap change. Replacing the visual during live
            // resize can expose a blank transparent frame before the first present.
            int hr = Dxgi.ResizeBuffers(
                entry.SwapChain,
                bufferCount: 2,
                width,
                height,
                D2D1.DXGI_FORMAT_B8G8R8A8_UNORM,
                flags: 0);
            if (hr < 0)
            {
                return false;
            }

            hr = Dxgi.GetBuffer(entry.SwapChain, 0, D2D1.IID_IDXGISurface, out dxgiSurface);
            if (hr < 0 || dxgiSurface == 0)
            {
                return false;
            }

            var bitmapProps = new D2D1_BITMAP_PROPERTIES1(
                new D2D1_PIXEL_FORMAT(D2D1.DXGI_FORMAT_B8G8R8A8_UNORM, entry.AlphaMode),
                dpi,
                dpi,
                D2D1_BITMAP_OPTIONS.TARGET | D2D1_BITMAP_OPTIONS.CANNOT_DRAW,
                colorContext: 0);

            hr = D2D1VTable.CreateBitmapFromDxgiSurface((ID2D1DeviceContext*)entry.RenderTarget, dxgiSurface, bitmapProps, out targetBitmap);
            if (hr < 0 || targetBitmap == 0)
            {
                return false;
            }

            D2D1VTable.SetTarget((ID2D1DeviceContext*)entry.RenderTarget, targetBitmap);
            D2D1VTable.SetDpi((ID2D1RenderTarget*)entry.RenderTarget, dpi, dpi);

            entry.TargetBitmap = targetBitmap;
            targetBitmap = 0;
            entry.Width = width;
            entry.Height = height;
            entry.DpiX = dpi;
            entry.Monitor = monitor;
            entry.Generation++;
            return true;
        }
        finally
        {
            if (targetBitmap != 0)
            {
                ComHelpers.Release(targetBitmap);
            }

            if (dxgiSurface != 0)
            {
                ComHelpers.Release(dxgiSurface);
            }
        }
    }

    private int PresentCachedWindowTarget(nint hwnd)
    {
        lock (_rtLock)
        {
            if (!_windowTargets.TryGetValue(hwnd, out var entry) || !entry.IsSwapChainBacked || entry.SwapChain == 0)
            {
                return 0;
            }

            uint syncInterval = Application.IsRunning && !Application.Current.RenderLoopSettings.VSyncEnabled ? 0u : 1u;
            int hr = Dxgi.Present(entry.SwapChain, syncInterval, flags: 0);
            return hr;
        }
    }

    internal bool IsExternalDxgiImageCompatible(nint hwnd, int renderTargetGeneration, Direct2DDxgiSurfaceImage image)
    {
        if (hwnd == 0 || image.Affinity?.Device is not { } sourceDevice || sourceDevice.IsEmpty)
        {
            return true;
        }

        GpuDeviceIdentity? targetDevice = null;
        lock (_rtLock)
        {
            if (_windowTargets.TryGetValue(hwnd, out var entry) && entry.Generation == renderTargetGeneration)
            {
                targetDevice = entry.DeviceIdentity;
            }
        }

        if (targetDevice is not { } target || target.IsEmpty || AreSameDevice(sourceDevice, target))
        {
            return true;
        }

        if (image.MarkMismatchNotified())
        {
            bool shouldNotify = false;
            lock (_rtLock)
            {
                long now = System.Diagnostics.Stopwatch.GetTimestamp();
                if (!_lastExternalMismatchNotificationTicks.TryGetValue(hwnd, out long last)
                    || now - last >= System.Diagnostics.Stopwatch.Frequency)
                {
                    _lastExternalMismatchNotificationTicks[hwnd] = now;
                    shouldNotify = true;
                }
            }

            if (shouldNotify)
            {
                QueueGpuInteropInvalidated(new GpuInteropInvalidatedEventArgs(
                    GpuInteropInvalidationReason.ExternalResourceMismatch,
                    renderTargetDeviceChanged: true,
                    externalResourceMismatch: true,
                    renderTargetHandle: hwnd));
            }
        }

        return false;
    }

    private GpuDeviceIdentity? GetFactoryDeviceIdentity()
        => GetD3D11DeviceIdentity(_d3dDevice);

    private static GpuDeviceIdentity? GetD3D11DeviceIdentity(nint d3d11Device)
        => d3d11Device == 0 ? null : new GpuDeviceIdentity((ulong)d3d11Device, 0, d3d11Device);

    // Record-struct equality already compares all fields structurally; the helper is kept for
    // readability at the call site.
    private static bool AreSameDevice(GpuDeviceIdentity left, GpuDeviceIdentity right)
        => left == right;

    private static D2D1_PRESENT_OPTIONS GetPresentOptions()
    {
        if (Application.IsRunning && !Application.Current.RenderLoopSettings.VSyncEnabled)
        {
            return D2D1_PRESENT_OPTIONS.IMMEDIATELY;
        }

        return D2D1_PRESENT_OPTIONS.NONE;
    }

    private static nint GetMonitor(nint hwnd, PlatformDisplayIdentity displayIdentity)
    {
        // Win32 places HMONITOR in NativeHandle; any non-zero value from the platform side
        // is preferred over a fresh MonitorFromWindow lookup. (Win32 is the only platform
        // hitting this factory, so a typed-discriminator check isn't needed.)
        if (displayIdentity.NativeHandle != 0)
        {
            return displayIdentity.NativeHandle;
        }

        return hwnd == 0 ? 0 : User32.MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
    }

    private sealed class CachedWindowTarget
    {
        public nint RenderTarget;
        public nint SwapChain;
        public nint TargetBitmap;
        // DirectComposition handles for transparent (NOREDIRECTIONBITMAP) windows. The swap-chain
        // attaches to a visual which is the root of a target bound to the HWND; DWM then
        // composes the swap-chain output with per-pixel alpha. All three handles are 0 when
        // the cache entry is for an opaque (CreateSwapChainForHwnd) target.
        public nint DcompDevice;
        public nint DcompTarget;
        public nint DcompVisual;
        public nint Monitor;
        public GpuDeviceIdentity? DeviceIdentity;
        public uint Width;
        public uint Height;
        public float DpiX;
        public D2D1_PRESENT_OPTIONS PresentOptions;
        public D2D1_ALPHA_MODE AlphaMode;
        public int Generation;
        public WindowTargetMode Mode;

        public bool IsSwapChainBacked => Mode != WindowTargetMode.HwndRenderTarget;

        public CachedWindowTarget(nint renderTarget, uint width, uint height, float dpiX, D2D1_PRESENT_OPTIONS presentOptions, D2D1_ALPHA_MODE alphaMode, int generation, WindowTargetMode mode, nint swapChain, nint targetBitmap)
        {
            RenderTarget = renderTarget;
            SwapChain = swapChain;
            TargetBitmap = targetBitmap;
            Width = width;
            Height = height;
            DpiX = dpiX;
            PresentOptions = presentOptions;
            AlphaMode = alphaMode;
            Generation = generation;
            Mode = mode;
        }

        public void DisposeNativeHandles()
        {
            if (IsSwapChainBacked && RenderTarget != 0)
            {
                D2D1VTable.SetTarget((ID2D1DeviceContext*)RenderTarget, 0);
            }

            if (TargetBitmap != 0)
            {
                ComHelpers.Release(TargetBitmap);
                TargetBitmap = 0;
            }

            if (RenderTarget != 0)
            {
                ComHelpers.Release(RenderTarget);
                RenderTarget = 0;
            }

            if (SwapChain != 0)
            {
                ComHelpers.Release(SwapChain);
                SwapChain = 0;
            }

            // DComp handles released last - Visual references the swap-chain (already cleared
            // above) and Target references the Visual; reverse-order release matches DComp's
            // ownership graph without leaving dangling internal references.
            if (DcompVisual != 0)
            {
                ComHelpers.Release(DcompVisual);
                DcompVisual = 0;
            }
            if (DcompTarget != 0)
            {
                ComHelpers.Release(DcompTarget);
                DcompTarget = 0;
            }
            if (DcompDevice != 0)
            {
                ComHelpers.Release(DcompDevice);
                DcompDevice = 0;
            }
        }
    }

}
