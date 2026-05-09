using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Win32;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Direct2D;

public sealed unsafe partial class Direct2DGraphicsFactory : IGraphicsFactory, IRenderDevice, IWindowResourceReleaser, IWindowSurfacePresenter, IWin32TransparencyCapabilities, IDisposable
{
    public static Direct2DGraphicsFactory Instance => field ??= new Direct2DGraphicsFactory();

    public GraphicsBackend Backend => GraphicsBackend.Direct2D;

    /// <summary>
    /// D2D presents transparent windows via a DXGI swap-chain (premultiplied alpha) attached
    /// to a <c>WS_EX_NOREDIRECTIONBITMAP</c> window. This avoids the layered-DIB readback
    /// path that <see cref="ID2D1DCRenderTarget"/> implicitly takes per frame.
    /// </summary>
    public Win32TransparencyMode TransparencyMode => Win32TransparencyMode.Surface;

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
    private readonly Dictionary<nint, Direct2DBitmapRenderTarget> _layeredTargets = new();
    private readonly Dictionary<StrokeStyle, nint> _strokeStyles = new();
    private readonly RenderResourceCache _renderResourceCache = new();

    private Direct2DGraphicsFactory() { }

    public void Dispose()
    {
        _renderResourceCache.Dispose();

        lock (_rtLock)
        {
            foreach (var (_, entry) in _windowTargets)
            {
                ComHelpers.Release(entry.RenderTarget);
            }

            _windowTargets.Clear();

            foreach (var (_, layered) in _layeredTargets)
            {
                layered.Dispose();
            }

            _layeredTargets.Clear();
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
        // access across the entire D2D resource graph — incompatible with our worker-
        // rendered SvgView pattern.
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

    public ISolidColorBrush CreateSolidColorBrush(Color color) =>
        new Direct2DSolidColorBrush(color);

    public IPen CreatePen(Color color, double thickness = 1.0, StrokeStyle? strokeStyle = null)
    {
        var ss = strokeStyle ?? StrokeStyle.Default;
        return new Direct2DPen(color, thickness, ss, GetOrCreateStrokeStyle(ss));
    }

    public IPen CreatePen(IBrush brush, double thickness = 1.0, StrokeStyle? strokeStyle = null)
    {
        var ss = strokeStyle ?? StrokeStyle.Default;
        return new Direct2DPen(brush, thickness, ss, GetOrCreateStrokeStyle(ss));
    }

    private nint GetOrCreateStrokeStyle(StrokeStyle ss)
    {
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

    /// <summary>
    /// Wraps an externally-owned <c>IDXGISurface*</c> as an <see cref="IImage"/>. The
    /// bitmap is materialized against the consuming render target on demand so each
    /// target gets a resource in its own Direct2D resource domain.
    /// </summary>
    public IImage CreateImageFromDxgiSurface(nint dxgiSurface, int pixelWidth, int pixelHeight, BitmapAlphaMode alphaMode = BitmapAlphaMode.Premultiplied)
        => new Direct2DDxgiSurfaceImage(dxgiSurface, pixelWidth, pixelHeight, alphaMode);

    public IGraphicsContext CreateContext(IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target is WindowRenderTarget windowTarget)
        {
            if (windowTarget.Surface is not IWin32WindowSurface win32Surface || win32Surface.Hwnd == 0)
            {
                throw new ArgumentException("Direct2D backend requires a Win32 window surface.", nameof(target));
            }

            return CreateContextCore(win32Surface.Hwnd, windowTarget.DpiScale, win32Surface.TransparentComposition);
        }

        if (target is Direct2DBitmapRenderTarget bitmapTarget)
        {
            return CreateBitmapContext(bitmapTarget);
        }

        if (target is Direct2DGpuBitmapRenderTarget gpuTarget)
        {
            return CreateGpuBitmapContext(gpuTarget);
        }

        if (target is IBitmapRenderTarget)
        {
            throw new ArgumentException(
                $"BitmapRenderTarget was created by a different backend. " +
                $"Use {nameof(CreateSurface)} from the same factory.",
                nameof(target));
        }

        throw new NotSupportedException($"Unsupported render target type: {target.GetType().Name}");
    }

    private IGraphicsContext CreateContextCore(nint hwnd, double dpiScale, bool transparentComposition = false)
    {
        EnsureInitialized();

        // Pre-warm the cached window target so the first BeginFrame doesn't pay the cost.
        GetOrCreateCachedWindowTarget(hwnd, dpiScale, transparentComposition);

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
                return GetOrCreateCachedWindowTarget(hwnd, t.DpiScale, transparent);
            });
        ctx.TextTracker = _textTracker;
        return ctx;
    }

    private IGraphicsContext CreateBitmapContext(Direct2DBitmapRenderTarget target)
    {
        EnsureInitialized();

        // The DC render target lifetime tracks the bitmap RT itself (RAII). The context
        // calls back into the RT for the (HDC-bound) DC RT handle each frame.
        var d2dFactory = _d2dFactory;
        var ctx = new Direct2DGraphicsContext(
            this,
            hwnd: 0,
            d2dFactory: _d2dFactory,
            dwriteFactory: _dwriteFactory,
            defaultStrokeStyle: _defaultFixedStrokeStyle,
            onRecreateTarget: null,
            onPresentTarget: null,
            ownsRenderTarget: false,
            resolveRenderTarget: t => ((Direct2DBitmapRenderTarget)t).GetOrCreateDcRenderTarget(d2dFactory));
        return ctx;
    }

    /// <summary>Builds a graphics context for a GPU-resident bitmap target. The context's
    /// <c>OnBeginFrame</c> branches into <c>BeginGpuBitmapFrame</c> when the target is a
    /// <see cref="Direct2DGpuBitmapRenderTarget"/>, using the shared filter device context
    /// + <c>SetTarget</c> instead of a DC render target — keeps the filter pipeline on-GPU
    /// end-to-end.</summary>
    private IGraphicsContext CreateGpuBitmapContext(Direct2DGpuBitmapRenderTarget target)
    {
        EnsureInitialized();

        // resolveRenderTarget is unused for GPU targets — the GPU branch in OnBeginFrame
        // wires _renderTarget directly from the shared DC.
        var ctx = new Direct2DGraphicsContext(
            this,
            hwnd: 0,
            d2dFactory: _d2dFactory,
            dwriteFactory: _dwriteFactory,
            defaultStrokeStyle: _defaultFixedStrokeStyle,
            onRecreateTarget: null,
            onPresentTarget: null,
            ownsRenderTarget: false,
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

    private IBitmapRenderTarget CreateCpuBitmapSurfaceTarget(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha)
        => new Direct2DBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale, hasAlpha);

    /// <summary>
    /// Returns a GPU-resident <see cref="Direct2DGpuBitmapRenderTarget"/> when the shared
    /// device context is available, falling back to the DIB-backed
    /// <see cref="Direct2DBitmapRenderTarget"/> otherwise. The GPU path keeps filter graphs
    /// fully on-GPU (CreateBitmap1 with TARGET option → effects sample directly →
    /// downstream draws via SetTarget + DrawImage); the only readback happens when CPU
    /// executors call <c>Lock</c>, in which case the lock release path also writes the
    /// modified buffer back to the GPU via <c>CopyFromMemory</c> so subsequent effects see
    /// the up-to-date pixels.
    /// </summary>
    private IBitmapRenderTarget CreateOffscreenSurfaceTarget(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha)
    {
        if (SharedFilterDeviceContext != 0)
        {
            try
            {
                return new Direct2DGpuBitmapRenderTarget(this, pixelWidth, pixelHeight, dpiScale, hasAlpha);
            }
            catch
            {
                // GPU init failed (out of GPU memory, requested size > device limit, etc.).
                // Fall through to DIB so the filter pipeline continues to render — slowly,
                // but it renders.
            }
        }
        return new Direct2DBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale, hasAlpha);
    }

    public IRenderResourceCache? ResourceCache => _renderResourceCache;

    public IRenderEffectDevice? Effects => null;

    public IRenderSurface CreateSurface(RenderSurfaceDescriptor descriptor)
    {
        var target = RenderDeviceFactoryHelpers.RequiresCpuBitmap(descriptor)
            ? CreateCpuBitmapSurfaceTarget(
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
        => RenderDeviceFactoryHelpers.CreateContext(this, surface);

    public IImage CreateImageView(IRenderSurface surface)
        => RenderDeviceFactoryHelpers.CreateImageView(this, surface);

    public IImage CreateImageView(IPixelBufferSource source)
        => new Direct2DImage(source);

    public IImage CreateImageView(IExternalSampleSource source)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support external sample sources of type {source.GetType().Name}.");

    public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes)
        => RenderDeviceFactoryHelpers.TryReadPixels(source, destination, destinationStrideBytes);

    public IRenderOperation RequestReadback(IRenderSurface source)
        => RenderDeviceFactoryHelpers.RequestReadback(source);

    public IRenderOperation FlushAsyncWork() => RenderOperation.Completed;

    public Filters.IImageFilterExecutor CreateImageFilterExecutor()
        => new Direct2DImageFilterExecutor(this);

    public bool Present(Window window, IWindowSurface surface, double opacity)
    {
        if (surface is not IWin32LayeredWindowSurface win32Surface ||
            surface.Kind != WindowSurfaceKind.Layered ||
            win32Surface.Hwnd == 0)
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(window);

        var hwnd = win32Surface.Hwnd;
        int w = Math.Max(1, win32Surface.PixelWidth);
        int h = Math.Max(1, win32Surface.PixelHeight);
        double dpiScale = win32Surface.DpiScale <= 0 ? 1.0 : win32Surface.DpiScale;

        var target = GetOrCreateLayeredTarget(hwnd, w, h, dpiScale);
        window.RenderFrameToBitmap(target);

        // NOTE: UpdateLayeredWindow interprets pptDst as the WINDOW top-left in screen coordinates.
        // Passing ClientToScreen(0,0) will move the window every time we present (drift), because
        // client-origin != window-origin for any style with a non-client border.
        //
        // For per-pixel transparency we enforce a borderless popup window style on Win32 so
        // client-size == window-size and input/render stay aligned.
        if (!User32.GetWindowRect(hwnd, out var windowRect))
        {
            return true; // Best-effort: rendered but couldn't present.
        }
        var dst = new POINT(windowRect.left, windowRect.top);
        var size = new SIZE(w, h);
        var src = new POINT(0, 0);
        byte alpha = (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255.0);
        var blend = BLENDFUNCTION.SourceOver(alpha);

        const uint ULW_ALPHA = 0x00000002;
        _ = User32.UpdateLayeredWindow(
            hwnd: hwnd,
            hdcDst: 0,
            pptDst: ref dst,
            psize: ref size,
            hdcSrc: target.Hdc,
            pptSrc: ref src,
            crKey: 0,
            pblend: ref blend,
            dwFlags: ULW_ALPHA);

        return true;
    }

    private Direct2DBitmapRenderTarget GetOrCreateLayeredTarget(nint hwnd, int pixelWidth, int pixelHeight, double dpiScale)
    {
        lock (_rtLock)
        {
            if (_layeredTargets.TryGetValue(hwnd, out var existing) &&
                existing.PixelWidth == pixelWidth &&
                existing.PixelHeight == pixelHeight &&
                Math.Abs(existing.DpiScale - dpiScale) < 0.001)
            {
                return existing;
            }

            if (_layeredTargets.Remove(hwnd, out var old))
            {
                old.Dispose();
            }

            var created = new Direct2DBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale);
            _layeredTargets[hwnd] = created;
            return created;
        }
    }

    public void ReleaseWindowResources(nint hwnd)
    {
        lock (_rtLock)
        {
            // The context is owned by the Window and disposed there. Here we only
            // release factory-owned native handles (COM render targets, layered bitmaps).
            InvalidateCachedWindowTargetLocked(hwnd);

            if (_layeredTargets.Remove(hwnd, out var layered))
            {
                // The bitmap RT owns its own DC render target now (RAII) — disposing the
                // bitmap releases the DC RT in lockstep.
                layered.Dispose();
            }
        }
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

            foreach (var (_, layered) in _layeredTargets)
            {
                layered.Dispose();
            }

            _layeredTargets.Clear();
        }
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

    private (nint renderTarget, int generation) GetOrCreateCachedWindowTarget(nint hwnd, double dpiScale, bool transparentComposition = false)
    {
        var rc = D2D1VTable.GetClientRect(hwnd);
        uint w = (uint)Math.Max(1, rc.Width);
        uint h = (uint)Math.Max(1, rc.Height);
        float dpi = (float)(96.0 * dpiScale);
        var presentOptions = GetPresentOptions();

        lock (_rtLock)
        {
            int generation = 0;
            if (SharedFilterDeviceContext != 0 && _d2dDevice != 0 && _d3dDevice != 0)
            {
                // Both opaque and transparent windows now go through the swap-chain path.
                // Transparent windows must be created with WS_EX_NOREDIRECTIONBITMAP (the Win32
                // platform sets this when the backend reports Win32TransparencyMode.Surface);
                // the swap-chain is created with PREMULTIPLIED alpha and DWM composes it
                // directly without a redirection bitmap, eliminating the per-frame readback
                // that the legacy ID2D1HwndRenderTarget / ID2D1DCRenderTarget paths incur.
                if (TryGetOrCreateCachedSwapChainTargetLocked(hwnd, w, h, dpi, presentOptions, transparentComposition, out var swapChainTarget))
                {
                    return swapChainTarget;
                }
            }

            // HWND render targets always use IGNORE — the DWM redirection surface
            // does not preserve per-pixel alpha from D2D Present.
            // For system backdrop, DWM extended frame treats opaque black (0,0,0) as glass.
            var requiredAlpha = D2D1_ALPHA_MODE.IGNORE;

            if (_windowTargets.TryGetValue(hwnd, out var entry) && entry.RenderTarget != 0)
            {
                if (entry.Width == w && entry.Height == h && entry.DpiX == dpi
                    && entry.PresentOptions == presentOptions && entry.AlphaMode == requiredAlpha && !entry.UsesSwapChain)
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
            _windowTargets[hwnd] = new CachedWindowTarget(renderTarget, w, h, dpi, presentOptions, alphaMode, generation, usesSwapChain: false, swapChain: 0, targetBitmap: 0);
            return (renderTarget, generation);
        }
    }

    private bool TryGetOrCreateCachedSwapChainTargetLocked(nint hwnd, uint width, uint height, float dpi, D2D1_PRESENT_OPTIONS presentOptions, bool transparentComposition, out (nint renderTarget, int generation) target)
    {
        target = default;

        // Alpha mode is part of the cache key — switching between transparent and opaque
        // requires a new swap-chain (different DXGI_ALPHA_MODE) and a fresh window (the
        // WS_EX_NOREDIRECTIONBITMAP bit is fixed at CreateWindow time anyway, but if the
        // window survives a recreate we still need the cached target reissued).
        var requiredAlpha = transparentComposition ? D2D1_ALPHA_MODE.PREMULTIPLIED : D2D1_ALPHA_MODE.IGNORE;

        int generation = 0;
        if (_windowTargets.TryGetValue(hwnd, out var existing) && existing.RenderTarget != 0)
        {
            if (existing.UsesSwapChain
                && existing.Width == width
                && existing.Height == height
                && existing.DpiX == dpi
                && existing.AlphaMode == requiredAlpha)
            {
                target = (existing.RenderTarget, existing.Generation);
                return true;
            }

            existing.DisposeNativeHandles();
            existing.Generation++;
            generation = existing.Generation;
            _windowTargets.Remove(hwnd);
        }

        if (!TryCreateSwapChainWindowTarget(hwnd, width, height, dpi, presentOptions, generation, transparentComposition, out var created))
        {
            return false;
        }

        _windowTargets[hwnd] = created;
        target = (created.RenderTarget, created.Generation);
        return true;
    }

    private bool TryCreateSwapChainWindowTarget(nint hwnd, uint width, uint height, float dpi, D2D1_PRESENT_OPTIONS presentOptions, int generation, bool transparentComposition, out CachedWindowTarget target)
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

        // Transparent surfaces must use PREMULTIPLIED alpha end-to-end so DWM (via the
        // DirectComposition visual that wraps the swap-chain) blends against the desktop.
        // Opaque windows keep IGNORE so D2D can skip per-fragment blend math and
        // ClearType subpixel rendering remains valid.
        var alphaMode = transparentComposition ? DXGI_ALPHA_MODE.PREMULTIPLIED : DXGI_ALPHA_MODE.IGNORE;
        var d2dAlphaMode = transparentComposition ? D2D1_ALPHA_MODE.PREMULTIPLIED : D2D1_ALPHA_MODE.IGNORE;

        try
        {
            int hr = Dxgi.CreateDXGIFactory2(flags: 0, Dxgi.IID_IDXGIFactory2, out dxgiFactory);
            if (hr < 0 || dxgiFactory == 0)
            {
                return false;
            }

            // CreateSwapChainForHwnd cannot be used on a WS_EX_NOREDIRECTIONBITMAP window
            // (no redirection surface to bind to). Transparent windows therefore go through
            // CreateSwapChainForComposition + a DirectComposition visual — DWM composes the
            // visual's swap-chain content with per-pixel alpha, no readback.
            var swapChainDesc = new DXGI_SWAP_CHAIN_DESC1(
                width,
                height,
                D2D1.DXGI_FORMAT_B8G8R8A8_UNORM,
                stereo: 0,
                sampleDesc: new DXGI_SAMPLE_DESC(count: 1, quality: 0),
                bufferUsage: Dxgi.DXGI_USAGE_RENDER_TARGET_OUTPUT,
                bufferCount: 2,
                scaling: DXGI_SCALING.STRETCH,
                swapEffect: DXGI_SWAP_EFFECT.FLIP_SEQUENTIAL,
                alphaMode: alphaMode,
                flags: 0);

            if (transparentComposition)
            {
                hr = Dxgi.CreateSwapChainForComposition(dxgiFactory, _d3dDevice, swapChainDesc, out swapChain);
                if (hr < 0 || swapChain == 0)
                {
                    return false;
                }

                // Need an IDXGIDevice for DCompositionCreateDevice — the D3D11 device
                // implements it.
                if (ComHelpers.QueryInterface(_d3dDevice, Dcomp.IID_IDXGIDevice, out var dxgiDevice) < 0 || dxgiDevice == 0)
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
                hr = Dxgi.CreateSwapChainForHwnd(dxgiFactory, _d3dDevice, hwnd, swapChainDesc, out swapChain);
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

            hr = D2D1VTable.CreateDeviceContext(_d2dDevice, options: 0, out deviceContext);
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

            target = new CachedWindowTarget(deviceContext, width, height, dpi, presentOptions, d2dAlphaMode, generation, usesSwapChain: true, swapChain, targetBitmap)
            {
                DcompDevice = dcompDevice,
                DcompTarget = dcompTarget,
                DcompVisual = dcompVisual,
            };
            deviceContext = 0;
            swapChain = 0;
            targetBitmap = 0;
            dcompDevice = 0;
            dcompTarget = 0;
            dcompVisual = 0;
            return true;
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

    private int PresentCachedWindowTarget(nint hwnd)
    {
        lock (_rtLock)
        {
            if (!_windowTargets.TryGetValue(hwnd, out var entry) || !entry.UsesSwapChain || entry.SwapChain == 0)
            {
                return 0;
            }

            uint syncInterval = Application.IsRunning && !Application.Current.RenderLoopSettings.VSyncEnabled ? 0u : 1u;
            return Dxgi.Present(entry.SwapChain, syncInterval, flags: 0);
        }
    }

    private static D2D1_PRESENT_OPTIONS GetPresentOptions()
    {
        if (Application.IsRunning && !Application.Current.RenderLoopSettings.VSyncEnabled)
        {
            return D2D1_PRESENT_OPTIONS.IMMEDIATELY;
        }

        return D2D1_PRESENT_OPTIONS.NONE;
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
        public uint Width;
        public uint Height;
        public float DpiX;
        public D2D1_PRESENT_OPTIONS PresentOptions;
        public D2D1_ALPHA_MODE AlphaMode;
        public int Generation;
        public bool UsesSwapChain;

        public CachedWindowTarget(nint renderTarget, uint width, uint height, float dpiX, D2D1_PRESENT_OPTIONS presentOptions, D2D1_ALPHA_MODE alphaMode, int generation, bool usesSwapChain, nint swapChain, nint targetBitmap)
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
            UsesSwapChain = usesSwapChain;
        }

        public void DisposeNativeHandles()
        {
            if (UsesSwapChain && RenderTarget != 0)
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

            // DComp handles released last — Visual references the swap-chain (already cleared
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
