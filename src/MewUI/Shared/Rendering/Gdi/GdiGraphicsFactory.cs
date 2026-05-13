using System.Runtime.Intrinsics;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Win32;
using Aprillz.MewUI.Rendering.Gdi.Core;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.Gdi;

/// <summary>
/// GDI+ graphics factory implementation.
/// </summary>
public sealed class GdiGraphicsFactory : IGraphicsFactory, IRenderDevice, IWindowResourceReleaser, IWindowSurfacePresenter, IDisposable
{
    public string Backend => "Gdi";

    /// <summary>
    /// Gets the singleton instance of the GDI graphics factory.
    /// </summary>
    public static GdiGraphicsFactory Instance => field ??= new GdiGraphicsFactory();

    private GdiGraphicsFactory() { }

    private readonly RenderResourceCache _renderResourceCache = new();

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
        // 1. Check FontRegistry (registered via FontResources.Register)
        var resolved = FontRegistry.Resolve(familyOrPath);
        if (resolved != null)
        {
            _ = Win32Fonts.EnsurePrivateFont(resolved.Value.FilePath);
            return resolved.Value.FamilyName;
        }

        // 2. Legacy: file path directly in FontFamily
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

            return CreateContextCore(win32Surface.Hwnd, win32Surface.Hdc, windowTarget.DpiScale, win32Surface.TransparentComposition);
        }

        if (target is GdiPixelRenderSurface pixelSurface)
        {
            // Use target's Hdc directly - no wrapper needed
            return new GdiPlusGraphicsContext(
                hwnd: 0,
                hdc: pixelSurface.Hdc,
                pixelWidth: pixelSurface.PixelWidth,
                pixelHeight: pixelSurface.PixelHeight,
                dpiScale: pixelSurface.DpiScale,
                imageScaleQuality: ImageScaleQuality,
                ownsDc: false,
                pixelSurface: pixelSurface);
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

    private IGraphicsContext CreateContextCore(nint hwnd, nint hdc, double dpiScale, bool transparentComposition = false)
        => IsDoubleBuffered
        ? GdiPlusGraphicsContext.CreateDoubleBuffered(hwnd, hdc, dpiScale, ImageScaleQuality, transparentComposition)
        : new GdiPlusGraphicsContext(hwnd, hdc, dpiScale, ImageScaleQuality);


    public IGraphicsContext CreateMeasurementContext(uint dpi)
    {
        var hdc = User32.GetDC(0);
        return new GdiMeasurementContext(hdc, dpi);
    }

    private IRenderSurface CreatePixelSurface(int pixelWidth, int pixelHeight, double dpiScale, bool hasAlpha)
        => new GdiPixelRenderSurface(pixelWidth, pixelHeight, dpiScale, hasAlpha: hasAlpha);

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

    public IImage CreateImageView(IPixelBufferSource source)
        => new GdiImage(source);

    public IImage CreateImageView(IExternalRasterSource source)
        => throw new NotSupportedException(
            $"{GetType().Name} does not support external raster sources of type {source.GetType().Name}.");

    public bool TryReadPixels(IRenderSurface source, Span<byte> destination, int destinationStrideBytes)
        => RenderDeviceFactoryHelpers.TryReadPixels(source, destination, destinationStrideBytes);

    public IRenderOperation RequestReadback(IRenderSurface source)
        => RenderDeviceFactoryHelpers.RequestReadback(source);

    public IRenderOperation FlushAsyncWork() => RenderOperation.Completed;

    public void Dispose()
    {
        _renderResourceCache.Dispose();

        lock (_layeredLock)
        {
            foreach (var (_, layered) in _layeredTargets)
                layered.Dispose();
            _layeredTargets.Clear();

            foreach (var (_, staging) in _layeredStagingTargets)
                staging.Dispose();
            _layeredStagingTargets.Clear();
        }

        GdiPlusGraphicsContext.ReleaseAllBackBuffers();
    }

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

        GdiPlusGraphicsContext.ReleaseForWindow(hwnd);
    }

    private readonly object _layeredLock = new();
    private readonly Dictionary<nint, GdiPixelRenderSurface> _layeredTargets = new();
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
        window.RenderFrameToSurface(target);

        // UpdateLayeredWindow expects premultiplied BGRA. The GDI pipeline already renders premultiplied
        // into the pixel surface; only fix up missing alpha from legacy GDI text/bitblt paths.
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

    private GdiPixelRenderSurface GetOrCreateLayeredTarget(nint hwnd, int pixelWidth, int pixelHeight, double dpiScale)
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

            var created = new GdiPixelRenderSurface(pixelWidth, pixelHeight, dpiScale, presentationMode: GdiPresentationMode.PerPixelAlpha);
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

    private static unsafe void CopyWithAlphaFix(ReadOnlySpan<byte> srcBgra, Span<byte> dstBgra)
    {
        int byteCount = Math.Min(srcBgra.Length, dstBgra.Length);
        if (byteCount <= 0) return;

        // GDI text/bitblt paths often leave A=0; infer opaque pixels from RGB.
        // Per pixel (BGRA little-endian = 0xAARRGGBB in uint):
        //   if (alpha == 0 && rgb != 0) alpha = 0xFF

        fixed (byte* srcPtr = srcBgra, dstPtr = dstBgra)
        {
            int pixelCount = byteCount / 4;
            int i = 0;

            if (System.Runtime.Intrinsics.X86.Sse2.IsSupported)
            {
                // 4 pixels per iteration (16 bytes).
                // alphaMask isolates A byte per pixel: 0x FF000000 repeated.
                var alphaMask = System.Runtime.Intrinsics.Vector128.Create(0xFF000000u).AsByte();
                var zero = System.Runtime.Intrinsics.Vector128<byte>.Zero;

                int simdCount = pixelCount & ~3; // round down to multiple of 4
                for (; i < simdCount; i += 4)
                {
                    var v = System.Runtime.Intrinsics.X86.Sse2.LoadVector128(srcPtr + i * 4);

                    // alphaBytes: A channels only (others zeroed)
                    var alphaBytes = System.Runtime.Intrinsics.X86.Sse2.And(v, alphaMask);
                    // alphaZero: 0xFFFFFFFF per pixel where A==0
                    var alphaZero = System.Runtime.Intrinsics.X86.Sse2.CompareEqual(alphaBytes.AsInt32(), zero.AsInt32()).AsByte();

                    // rgbBytes: RGB channels only (A zeroed)
                    var rgbBytes = System.Runtime.Intrinsics.X86.Sse2.AndNot(alphaMask, v);
                    // rgbNonZero: 0xFFFFFFFF per pixel where any RGB byte != 0
                    // CompareEqual gives 0xFFFFFFFF where all bytes are 0 => invert
                    var rgbAllZero = System.Runtime.Intrinsics.X86.Sse2.CompareEqual(rgbBytes.AsInt32(), zero.AsInt32()).AsByte();
                    var rgbHasColor = System.Runtime.Intrinsics.X86.Sse2.Xor(rgbAllZero,
                        System.Runtime.Intrinsics.Vector128.Create((byte)0xFF));

                    // needsFix: pixels where A==0 AND RGB!=0
                    var needsFix = System.Runtime.Intrinsics.X86.Sse2.And(alphaZero, rgbHasColor);
                    // OR 0xFF into the alpha byte position for those pixels
                    var fix = System.Runtime.Intrinsics.X86.Sse2.And(needsFix, alphaMask);
                    var result = System.Runtime.Intrinsics.X86.Sse2.Or(v, fix);

                    System.Runtime.Intrinsics.X86.Sse2.Store(dstPtr + i * 4, result);
                }
            }

            // Scalar remainder
            for (; i < pixelCount; i++)
            {
                uint pixel = ((uint*)srcPtr)[i];
                if ((pixel & 0xFF000000) == 0 && (pixel & 0x00FFFFFF) != 0)
                    pixel |= 0xFF000000;
                ((uint*)dstPtr)[i] = pixel;
            }
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
