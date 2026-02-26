using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Com;
using Aprillz.MewUI.Native.Direct2D;
using Aprillz.MewUI.Native.DirectWrite;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Win32;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Direct2D;

public sealed unsafe class Direct2DGraphicsFactory : IGraphicsFactory, IWindowResourceReleaser, IWindowSurfacePresenter, IDisposable
{
    public static Direct2DGraphicsFactory Instance => field ??= new Direct2DGraphicsFactory();

    public GraphicsBackend Backend => GraphicsBackend.Direct2D;

    private nint _d2dFactory;
    private nint _dwriteFactory;
    private bool _initialized;
    private bool _hasFactory1;
    private nint _defaultFixedStrokeStyle;

    private readonly object _rtLock = new();
    private readonly Dictionary<nint, CachedWindowTarget> _windowTargets = new();
    private readonly Dictionary<nint, Direct2DBitmapRenderTarget> _layeredTargets = new();
    private readonly Dictionary<StrokeStyle, nint> _strokeStyles = new();
    private int _dcRenderTargetGeneration;

    private Direct2DGraphicsFactory() { }

    public void Dispose()
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

        lock (_rtLock)
        {
            foreach (var (_, ss) in _strokeStyles)
                ComHelpers.Release(ss);
            _strokeStyles.Clear();
        }

        ComHelpers.Release(_defaultFixedStrokeStyle);
        _defaultFixedStrokeStyle = 0;
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

        // Try ID2D1Factory1 (Windows 8+, guaranteed on .NET 8+ targets) for D2D1_STROKE_TRANSFORM_TYPE_FIXED support.
        int hr = D2D1.D2D1CreateFactory(D2D1_FACTORY_TYPE.SINGLE_THREADED, D2D1.IID_ID2D1Factory1, 0, out _d2dFactory);
        if (hr >= 0 && _d2dFactory != 0)
        {
            _hasFactory1 = true;
        }
        else
        {
            hr = D2D1.D2D1CreateFactory(D2D1_FACTORY_TYPE.SINGLE_THREADED, D2D1.IID_ID2D1Factory, 0, out _d2dFactory);
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
            var defaultProps = new D2D1_STROKE_STYLE_PROPERTIES1(
                startCap: D2D1_CAP_STYLE.FLAT,
                endCap: D2D1_CAP_STYLE.FLAT,
                dashCap: D2D1_CAP_STYLE.FLAT,
                lineJoin: D2D1_LINE_JOIN.MITER,
                miterLimit: 10f,
                dashStyle: D2D1_DASH_STYLE.SOLID,
                dashOffset: 0f,
                transformType: D2D1_STROKE_TRANSFORM_TYPE.FIXED);
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
                    transformType: D2D1_STROKE_TRANSFORM_TYPE.FIXED);
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

    public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal, bool italic = false, bool underline = false, bool strikethrough = false) =>
        new DirectWriteFont(ResolveWin32FontFamilyOrFile(family), size, weight, italic, underline, strikethrough);

    public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal, bool italic = false, bool underline = false, bool strikethrough = false) =>
        new DirectWriteFont(ResolveWin32FontFamilyOrFile(family), size, weight, italic, underline, strikethrough);

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
            : "Segoe UI";
    }

    public IImage CreateImageFromFile(string path) =>
        CreateImageFromBytes(File.ReadAllBytes(path));

    public IImage CreateImageFromBytes(byte[] data) =>
        ImageDecoders.TryDecode(data, out var bmp)
            ? new Direct2DImage(bmp)
            : throw new NotSupportedException(
                $"Unsupported image format. Built-in decoders: BMP/PNG/JPEG. Detected: {ImageDecoders.DetectFormatId(data) ?? "unknown"}.");

    public IImage CreateImageFromPixelSource(IPixelBufferSource source) => new Direct2DImage(source);

    public IGraphicsContext CreateContext(IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target is WindowRenderTarget windowTarget)
        {
            if (windowTarget.Surface is not IWin32WindowSurface win32Surface || win32Surface.Hwnd == 0)
            {
                throw new ArgumentException("Direct2D backend requires a Win32 window surface.", nameof(target));
            }

            return CreateContextCore(win32Surface.Hwnd, windowTarget.DpiScale);
        }

        if (target is Direct2DBitmapRenderTarget bitmapTarget)
        {
            return CreateBitmapContext(bitmapTarget);
        }

        if (target is IBitmapRenderTarget)
        {
            throw new ArgumentException(
                $"BitmapRenderTarget was created by a different backend. " +
                $"Use {nameof(CreateBitmapRenderTarget)} from the same factory.",
                nameof(target));
        }

        throw new NotSupportedException($"Unsupported render target type: {target.GetType().Name}");
    }

    private IGraphicsContext CreateContextCore(nint hwnd, double dpiScale)
    {
        EnsureInitialized();

        var (rt, generation) = GetOrCreateCachedWindowTarget(hwnd, dpiScale);
        return new Direct2DGraphicsContext(
            hwnd,
            dpiScale,
            rt,
            generation,
            _d2dFactory,
            _dwriteFactory,
            _defaultFixedStrokeStyle,
            onRecreateTarget: () => InvalidateCachedWindowTarget(hwnd),
            ownsRenderTarget: false);
    }

    private IGraphicsContext CreateBitmapContext(Direct2DBitmapRenderTarget target)
    {
        EnsureInitialized();

        // Create DC render target and bind to target's Hdc
        var (rt, generation) = CreateDcRenderTargetForBitmap(target);
        return new Direct2DGraphicsContext(
            hwnd: 0,
            dpiScale: target.DpiScale,
            renderTarget: rt,
            renderTargetGeneration: generation,
            d2dFactory: _d2dFactory,
            dwriteFactory: _dwriteFactory,
            defaultStrokeStyle: _defaultFixedStrokeStyle,
            onRecreateTarget: null,
            ownsRenderTarget: true);
    }

    private (nint renderTarget, int generation) CreateDcRenderTargetForBitmap(Direct2DBitmapRenderTarget target)
    {
        // DC render target with premultiplied alpha for offscreen rendering
        var pixelFormat = new D2D1_PIXEL_FORMAT(87, D2D1_ALPHA_MODE.PREMULTIPLIED); // DXGI_FORMAT_B8G8R8A8_UNORM
        float dpi = (float)(96.0 * target.DpiScale);
        var rtProps = new D2D1_RENDER_TARGET_PROPERTIES(D2D1_RENDER_TARGET_TYPE.DEFAULT, pixelFormat, dpi, dpi, 0, 0);

        int hr = D2D1VTable.CreateDcRenderTarget((ID2D1Factory*)_d2dFactory, ref rtProps, out var dcRenderTarget);
        if (hr < 0 || dcRenderTarget == 0)
        {
            throw new InvalidOperationException($"CreateDcRenderTarget failed: 0x{hr:X8}");
        }

        // Bind to the target's memory DC
        var rect = new RECT(0, 0, target.PixelWidth, target.PixelHeight);
        hr = D2D1VTable.BindDC((ID2D1DCRenderTarget*)dcRenderTarget, target.Hdc, ref rect);
        if (hr < 0)
        {
            ComHelpers.Release(dcRenderTarget);
            throw new InvalidOperationException($"ID2D1DCRenderTarget::BindDC failed: 0x{hr:X8}");
        }

        // DCRenderTarget is created per-frame for layered window presentation. COM can reuse pointer
        // values across instances, so we must provide a monotonically increasing generation token
        // to ensure per-render-target resources (e.g., Direct2DImage bitmaps) are not reused across
        // different render target domains.
        int generation = Interlocked.Increment(ref _dcRenderTargetGeneration);
        return (dcRenderTarget, generation);
    }

    public IGraphicsContext CreateMeasurementContext(uint dpi)
    {
        EnsureInitialized();
        return new Direct2DMeasurementContext(_dwriteFactory);
    }

    public IBitmapRenderTarget CreateBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale = 1.0)
        => new Direct2DBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale);

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
            if (_windowTargets.Remove(hwnd, out var entry))
            {
                ComHelpers.Release(entry.RenderTarget);
            }

            if (_layeredTargets.Remove(hwnd, out var layered))
            {
                layered.Dispose();
            }
        }
    }

    private void InvalidateCachedWindowTarget(nint hwnd) => ReleaseWindowResources(hwnd);

    private (nint renderTarget, int generation) GetOrCreateCachedWindowTarget(nint hwnd, double dpiScale)
    {
        var rc = D2D1VTable.GetClientRect(hwnd);
        uint w = (uint)Math.Max(1, rc.Width);
        uint h = (uint)Math.Max(1, rc.Height);
        float dpi = (float)(96.0 * dpiScale);
        var presentOptions = GetPresentOptions();

        lock (_rtLock)
        {
            int generation = 0;
            if (_windowTargets.TryGetValue(hwnd, out var entry) && entry.RenderTarget != 0)
            {
                if (entry.Width == w && entry.Height == h && entry.DpiX == dpi && entry.PresentOptions == presentOptions)
                {
                    return (entry.RenderTarget, entry.Generation);
                }

                // If size/DPI changed, recreate the target. (Safer than calling ID2D1HwndRenderTarget::Resize via vtable indices.)
                ComHelpers.Release(entry.RenderTarget);
                entry.RenderTarget = 0;
                entry.Generation++;
                generation = entry.Generation;
                _windowTargets.Remove(hwnd);
            }

            // HWND render target: use alpha IGNORE to keep ClearType enabled (PREMULTIPLIED will force grayscale).
            var pixelFormat = new D2D1_PIXEL_FORMAT(0, D2D1_ALPHA_MODE.IGNORE);
            var rtProps = new D2D1_RENDER_TARGET_PROPERTIES(D2D1_RENDER_TARGET_TYPE.DEFAULT, pixelFormat, 0, 0, 0, 0);
            var hwndProps = new D2D1_HWND_RENDER_TARGET_PROPERTIES(hwnd, new D2D1_SIZE_U(w, h), presentOptions);

            int hr = D2D1VTable.CreateHwndRenderTarget((ID2D1Factory*)_d2dFactory, ref rtProps, ref hwndProps, out var renderTarget);
            if (hr < 0 || renderTarget == 0)
            {
                throw new InvalidOperationException($"CreateHwndRenderTarget failed: 0x{hr:X8}");
            }

            D2D1VTable.SetDpi((ID2D1RenderTarget*)renderTarget, dpi, dpi);
            _windowTargets[hwnd] = new CachedWindowTarget(renderTarget, w, h, dpi, presentOptions, generation);
            return (renderTarget, generation);
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
        public uint Width;
        public uint Height;
        public float DpiX;
        public D2D1_PRESENT_OPTIONS PresentOptions;
        public int Generation;

        public CachedWindowTarget(nint renderTarget, uint width, uint height, float dpiX, D2D1_PRESENT_OPTIONS presentOptions, int generation)
        {
            RenderTarget = renderTarget;
            Width = width;
            Height = height;
            DpiX = dpiX;
            PresentOptions = presentOptions;
            Generation = generation;
        }
    }
}
