using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Win32;
using Aprillz.MewUI.Rendering.Gdi.Core;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI graphics factory implementation.
/// </summary>
public sealed class GdiGraphicsFactory : IGraphicsFactory, IWindowResourceReleaser, IWindowSurfacePresenter
{
    public GraphicsBackend Backend => GraphicsBackend.Gdi;

    /// <summary>
    /// Gets the singleton instance of the GDI graphics factory.
    /// </summary>
    public static GdiGraphicsFactory Instance => field ??= new GdiGraphicsFactory();

    private GdiGraphicsFactory() { }

    public bool IsDoubleBuffered { get; set; } = true;

    public GdiCurveQuality CurveQuality { get; set; } = GdiCurveQuality.Supersample2x;

    // Keep backend default aligned with other backends: Default => Linear unless the app explicitly overrides.
    public ImageScaleQuality ImageScaleQuality { get; set; } = ImageScaleQuality.Normal;

    public ISolidColorBrush CreateSolidColorBrush(Color color) =>
        new GdiSolidColorBrush(color);

    public IPen CreatePen(Color color, double thickness = 1.0, StrokeStyle? strokeStyle = null) =>
        new GdiPen(color, thickness, strokeStyle ?? StrokeStyle.Default);

    public IPen CreatePen(IBrush brush, double thickness = 1.0, StrokeStyle? strokeStyle = null) =>
        new GdiPen(brush, thickness, strokeStyle ?? StrokeStyle.Default);

    public IFont CreateFont(string family, double size, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
    {
        uint dpi = DpiHelper.GetSystemDpi();
        family = ResolveFontFamilyOrFile(family);
        return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);
    }

    /// <summary>
    /// Creates a font with a specific DPI.
    /// </summary>
    public IFont CreateFont(string family, double size, uint dpi, FontWeight weight = FontWeight.Normal,
        bool italic = false, bool underline = false, bool strikethrough = false)
    {
        family = ResolveFontFamilyOrFile(family);
        return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);
    }

    private static string ResolveFontFamilyOrFile(string familyOrPath)
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
            ? CreateImage(bmp.WidthPx, bmp.HeightPx, bmp.Data)
            : throw new NotSupportedException(
                $"Unsupported image format. Built-in decoders: BMP/PNG/JPEG. Detected: {ImageDecoders.DetectFormatId(data) ?? "unknown"}.");

    public IImage CreateImageFromPixelSource(IPixelBufferSource source) => new GdiImage(source);

    /// <summary>
    /// Creates an empty 32-bit ARGB image.
    /// </summary>
    public IImage CreateImage(int width, int height) => new GdiImage(width, height);

    /// <summary>
    /// Creates a 32-bit ARGB image from raw pixel data.
    /// </summary>
    public IImage CreateImage(int width, int height, byte[] pixelData) => new GdiImage(width, height, pixelData);

    public IGraphicsContext CreateContext(IRenderTarget target)
    {
        ArgumentNullException.ThrowIfNull(target);

        if (target is WindowRenderTarget windowTarget)
        {
            if (windowTarget.Surface is not IWin32HdcWindowSurface win32Surface ||
                win32Surface.Hwnd == 0 ||
                win32Surface.Hdc == 0)
            {
                throw new ArgumentException("GDI backend requires a Win32 HDC window surface.", nameof(target));
            }

            return CreateContextCore(win32Surface.Hwnd, win32Surface.Hdc, windowTarget.DpiScale);
        }

        if (target is GdiBitmapRenderTarget bitmapTarget)
        {
            // Use target's Hdc directly - no wrapper needed
            return new GdiPlusGraphicsContext(
                hwnd: 0,
                hdc: bitmapTarget.Hdc,
                pixelWidth: bitmapTarget.PixelWidth,
                pixelHeight: bitmapTarget.PixelHeight,
                dpiScale: bitmapTarget.DpiScale,
                imageScaleQuality: ImageScaleQuality,
                ownsDc: false,
                bitmapTarget: bitmapTarget);
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

    private IGraphicsContext CreateContextCore(nint hwnd, nint hdc, double dpiScale)
        => IsDoubleBuffered
        ? new GdiDoubleBufferedContext(hwnd, hdc, dpiScale, CurveQuality, ImageScaleQuality)
        : new GdiPlusGraphicsContext(hwnd, hdc, dpiScale, ImageScaleQuality);


    public IGraphicsContext CreateMeasurementContext(uint dpi)
    {
        var hdc = Native.User32.GetDC(0);
        return new GdiMeasurementContext(hdc, dpi);
    }

    public IBitmapRenderTarget CreateBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale = 1.0)
        => new GdiBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale);

    public void ReleaseWindowResources(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        lock (_layeredLock)
        {
            if (_layeredTargets.Remove(hwnd, out var layered))
            {
                layered.Dispose();
            }

            if (_layeredStagingTargets.Remove(hwnd, out var staging))
            {
                staging.Dispose();
            }
        }

        GdiDoubleBufferedContext.ReleaseForWindow(hwnd);
    }

    private readonly object _layeredLock = new();
    private readonly Dictionary<nint, GdiBitmapRenderTarget> _layeredTargets = new();
    private readonly Dictionary<nint, Win32LayeredBitmap> _layeredStagingTargets = new();

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

        // UpdateLayeredWindow expects premultiplied BGRA. The GDI pipeline already renders premultiplied
        // into the bitmap target; only fix up missing alpha from legacy GDI text/bitblt paths.
        var staging = GetOrCreateLayeredStagingTarget(hwnd, w, h, dpiScale);
        CopyWithAlphaFix(target.GetPixelSpan(), staging.GetPixelSpan());

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
            hdcSrc: staging.Hdc,
            pptSrc: ref src,
            crKey: 0,
            pblend: ref blend,
            dwFlags: ULW_ALPHA);

        return true;
    }

    private GdiBitmapRenderTarget GetOrCreateLayeredTarget(nint hwnd, int pixelWidth, int pixelHeight, double dpiScale)
    {
        lock (_layeredLock)
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

            var created = new GdiBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale, presentationMode: GdiPresentationMode.PerPixelAlpha);
            _layeredTargets[hwnd] = created;
            return created;
        }
    }

    private Win32LayeredBitmap GetOrCreateLayeredStagingTarget(nint hwnd, int pixelWidth, int pixelHeight, double dpiScale)
    {
        lock (_layeredLock)
        {
            if (_layeredStagingTargets.TryGetValue(hwnd, out var existing) &&
                existing.PixelWidth == pixelWidth &&
                existing.PixelHeight == pixelHeight &&
                Math.Abs(existing.DpiScale - dpiScale) < 0.001)
            {
                return existing;
            }

            if (_layeredStagingTargets.Remove(hwnd, out var old))
            {
                old.Dispose();
            }

            var created = new Win32LayeredBitmap(pixelWidth, pixelHeight, dpiScale);
            _layeredStagingTargets[hwnd] = created;
            return created;
        }
    }

    private static void CopyWithAlphaFix(ReadOnlySpan<byte> srcBgra, Span<byte> dstBgra)
    {
        if (srcBgra.Length == 0 || dstBgra.Length == 0)
        {
            return;
        }

        int byteCount = Math.Min(srcBgra.Length, dstBgra.Length);
        if (byteCount <= 0)
        {
            return;
        }

        var src = srcBgra.Slice(0, byteCount);
        var dst = dstBgra.Slice(0, byteCount);

        // GDI text/bitblt paths often leave A=0; infer opaque pixels from RGB.
        for (int i = 0; i + 3 < byteCount; i += 4)
        {
            byte b = src[i + 0];
            byte g = src[i + 1];
            byte r = src[i + 2];
            byte a = src[i + 3];

            if (a == 0 && (b | g | r) != 0)
            {
                a = 255;
            }

            dst[i + 0] = b;
            dst[i + 1] = g;
            dst[i + 2] = r;
            dst[i + 3] = a;
        }
    }

    private sealed class Win32LayeredBitmap : IDisposable
    {
        private readonly nint _dibSection;
        private readonly nint _oldBitmap;
        private readonly nint _dibBits;
        private bool _disposed;

        public int PixelWidth { get; }
        public int PixelHeight { get; }
        public double DpiScale { get; }
        public nint Hdc { get; }

        public Win32LayeredBitmap(int pixelWidth, int pixelHeight, double dpiScale)
        {
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelWidth, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(pixelHeight, 0);
            ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(dpiScale, 0);

            PixelWidth = pixelWidth;
            PixelHeight = pixelHeight;
            DpiScale = dpiScale;

            var screenDc = User32.GetDC(0);
            Hdc = Gdi32.CreateCompatibleDC(screenDc);
            _ = User32.ReleaseDC(0, screenDc);

            if (Hdc == 0)
            {
                throw new InvalidOperationException("Failed to create memory DC for layered presentation.");
            }

            var bmi = BITMAPINFO.Create32bpp(pixelWidth, pixelHeight);
            _dibSection = Gdi32.CreateDIBSection(Hdc, ref bmi, 0, out _dibBits, 0, 0);
            if (_dibSection == 0 || _dibBits == 0)
            {
                Gdi32.DeleteDC(Hdc);
                throw new InvalidOperationException("Failed to create DIB section for layered presentation.");
            }

            _oldBitmap = Gdi32.SelectObject(Hdc, _dibSection);
        }

        public unsafe Span<byte> GetPixelSpan()
        {
            if (_disposed || _dibBits == 0)
            {
                return Span<byte>.Empty;
            }

            return new Span<byte>((void*)_dibBits, PixelWidth * PixelHeight * 4);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_oldBitmap != 0 && Hdc != 0)
            {
                Gdi32.SelectObject(Hdc, _oldBitmap);
            }

            if (_dibSection != 0)
            {
                Gdi32.DeleteObject(_dibSection);
            }

            if (Hdc != 0)
            {
                Gdi32.DeleteDC(Hdc);
            }
        }
    }
}
