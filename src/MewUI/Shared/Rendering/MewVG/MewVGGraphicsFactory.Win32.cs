using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Win32;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Rendering.OpenGL;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.MewVG;

public sealed partial class MewVGGraphicsFactory
{
#pragma warning disable CS0649
    [ThreadStatic] private static nint _bitmapPresentHwnd;
    [ThreadStatic] private static nint _bitmapPresentHdc;
#pragma warning restore CS0649

    private partial IFont CreateFontCore(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        uint dpi = DpiHelper.GetSystemDpi();
        family = ResolveWin32FontFamilyOrFile(family);
        return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);
    }

    private partial IFont CreateFontCore(string family, double size, uint dpi, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        family = ResolveWin32FontFamilyOrFile(family);
        return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);
    }

    private static string ResolveWin32FontFamilyOrFile(string familyOrPath)
    {
        if (!FontResources.LooksLikeFontFilePath(familyOrPath))
        {
            return familyOrPath;
        }

        var path = Path.GetFullPath(familyOrPath);
        _ = Win32Fonts.EnsurePrivateFontFamily(path);

        return FontResources.TryGetParsedFamilyName(path, out var parsed) && !string.IsNullOrWhiteSpace(parsed)
            ? parsed
            : "Segoe UI";
    }

    private partial IDisposable CreateWindowResources(IWindowSurface surface)
    {
        if (surface is not IWin32HdcWindowSurface win32 || win32.Hwnd == 0 || win32.Hdc == 0)
        {
            throw new ArgumentException("MewVG (Win32) requires a Win32 HDC window surface.", nameof(surface));
        }

        return MewVGWindowResources.Create(win32.Hwnd, win32.Hdc);
    }

    private partial IGraphicsContext CreateContextCore(WindowRenderTarget target, IDisposable resources)
    {
        if (target.Surface is not IWin32HdcWindowSurface win32 || win32.Hwnd == 0 || win32.Hdc == 0)
        {
            throw new ArgumentException("MewVG (Win32) requires a Win32 HDC window surface.", nameof(target));
        }

        return new MewVGGraphicsContext(win32.Hwnd, win32.Hdc, target.PixelWidth, target.PixelHeight, target.DpiScale, (MewVGWindowResources)resources);
    }

    private partial IGraphicsContext CreateMeasurementContextCore(uint dpi)
        => new GdiMeasurementContext(User32.GetDC(0), dpi);

    static partial void TryCreateBitmapRenderTarget(int pixelWidth, int pixelHeight, double dpiScale, ref bool handled, ref IBitmapRenderTarget? renderTarget)
    {
        if (handled)
        {
            return;
        }

        renderTarget = new OpenGLBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale);
        handled = true;
    }

    partial void TryCreateContextForTarget(IRenderTarget target, ref bool handled, ref IGraphicsContext? context)
    {
        if (handled)
        {
            return;
        }

        if (target is not OpenGLBitmapRenderTarget bitmapTarget)
        {
            return;
        }

        var hwnd = _bitmapPresentHwnd;
        var hdc = _bitmapPresentHdc;
        if (hwnd == 0 || hdc == 0)
        {
            throw new InvalidOperationException(
                "OpenGLBitmapRenderTarget requires an active Win32 layered present context. " +
                "It is intended to be used via Window.AllowsTransparency with the MewVG Win32 backend.");
        }

        var resources = MewVGWin32LayeredSupport.Instance.GetOrCreateWindowResources(hwnd, hdc);
        context = new MewVGGraphicsContext(hwnd, hdc, bitmapTarget.PixelWidth, bitmapTarget.PixelHeight, bitmapTarget.DpiScale, resources, bitmapTarget);
        handled = true;
    }

    static partial void TryReleaseWindowResources(nint hwnd)
    {
        MewVGWin32LayeredSupport.Instance.Release(hwnd);
    }

    static partial void TryPresentWindowSurface(Window window, IWindowSurface surface, double opacity, ref bool handled, ref bool result)
    {
        if (handled)
        {
            return;
        }

        if (surface is not IWin32LayeredWindowSurface win32Surface ||
            win32Surface.Hwnd == 0 ||
            surface.Kind != WindowSurfaceKind.Layered)
        {
            return;
        }

        handled = true;
        result = MewVGWin32LayeredSupport.Instance.Present(
            window,
            win32Surface,
            opacity,
            render: ctx =>
            {
                _bitmapPresentHwnd = ctx.Hwnd;
                _bitmapPresentHdc = ctx.Hdc;
                try
                {
                    window.RenderFrameToBitmap(ctx.RenderTarget);
                }
                finally
                {
                    _bitmapPresentHwnd = 0;
                    _bitmapPresentHdc = 0;
                }
            });
    }
}

internal sealed class MewVGWin32LayeredSupport
{
    public static MewVGWin32LayeredSupport Instance => field ??= new MewVGWin32LayeredSupport();

    private readonly object _lock = new();
    private readonly Dictionary<nint, OpenGLBitmapRenderTarget> _layeredTargets = new();
    private readonly Dictionary<nint, Win32LayeredBitmap> _layeredStagingTargets = new();
    private readonly Dictionary<nint, MewVGWindowResources> _layeredWindowResources = new();

    private MewVGWin32LayeredSupport() { }

    internal readonly record struct LayeredRenderContext(nint Hwnd, nint Hdc, OpenGLBitmapRenderTarget RenderTarget);

    internal bool Present(Window window, IWin32LayeredWindowSurface surface, double opacity, Action<LayeredRenderContext> render)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(surface);
        ArgumentNullException.ThrowIfNull(render);

        var hwnd = surface.Hwnd;
        if (hwnd == 0 || surface.Kind != WindowSurfaceKind.Layered)
        {
            return false;
        }

        int w = Math.Max(1, surface.PixelWidth);
        int h = Math.Max(1, surface.PixelHeight);
        double dpiScale = surface.DpiScale <= 0 ? 1.0 : surface.DpiScale;

        // OpenGL rendering requires an HDC to make the WGL context current.
        nint hdc = User32.GetDC(hwnd);
        if (hdc == 0)
        {
            return true;
        }

        try
        {
            var glTarget = GetOrCreateLayeredTarget(hwnd, w, h, dpiScale);
            render(new LayeredRenderContext(hwnd, hdc, glTarget));

            var staging = GetOrCreateLayeredStagingTarget(hwnd, w, h, dpiScale);
            var pixelSrc = glTarget.GetPixelSpan();
            var pixelDst = staging.GetPixelSpan();
            CopyPixels(pixelSrc, pixelDst);

            // NOTE: UpdateLayeredWindow interprets pptDst as the WINDOW top-left in screen coordinates.
            // Passing ClientToScreen(0,0) will move the window every time we present (drift), because
            // client-origin != window-origin for any style with a non-client border.
            //
            // For per-pixel transparency we enforce a borderless popup window style on Win32 so
            // client-size == window-size and input/render stay aligned.
            if (!User32.GetWindowRect(hwnd, out var windowRect))
            {
                return true;
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
        finally
        {
            _ = User32.ReleaseDC(hwnd, hdc);
        }
    }

    internal MewVGWindowResources GetOrCreateWindowResources(nint hwnd, nint hdc)
    {
        if (hwnd == 0 || hdc == 0)
        {
            throw new ArgumentException("Invalid window handles.");
        }

        lock (_lock)
        {
            if (_layeredWindowResources.TryGetValue(hwnd, out var existing))
            {
                return existing;
            }

            var created = MewVGWindowResources.Create(hwnd, hdc);
            _layeredWindowResources[hwnd] = created;
            return created;
        }
    }

    internal void Release(nint hwnd)
    {
        if (hwnd == 0)
        {
            return;
        }

        lock (_lock)
        {
            if (_layeredTargets.Remove(hwnd, out var gl))
            {
                gl.Dispose();
            }

            if (_layeredStagingTargets.Remove(hwnd, out var staging))
            {
                staging.Dispose();
            }

            if (_layeredWindowResources.Remove(hwnd, out var resources))
            {
                resources.Dispose();
            }
        }
    }

    private static void CopyPixels(ReadOnlySpan<byte> srcBgra, Span<byte> dstBgra)
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

        srcBgra.Slice(0, byteCount).CopyTo(dstBgra);
    }

    private OpenGLBitmapRenderTarget GetOrCreateLayeredTarget(nint hwnd, int pixelWidth, int pixelHeight, double dpiScale)
    {
        lock (_lock)
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

            var created = new OpenGLBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale);
            _layeredTargets[hwnd] = created;
            return created;
        }
    }

    private Win32LayeredBitmap GetOrCreateLayeredStagingTarget(nint hwnd, int pixelWidth, int pixelHeight, double dpiScale)
    {
        lock (_lock)
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
