using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Win32;
using Aprillz.MewUI.Rendering.FreeType;
using Aprillz.MewUI.Rendering.Gdi;
using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.OpenGL;

public sealed partial class OpenGLGraphicsFactory
{
    static OpenGLGraphicsFactory()
    {
        // Register Win32-only layered presentation hooks.
        RegisterWin32LayeredHooks(
            present: (window, surface, opacity) => Instance.PresentLayeredWin32(window, surface, opacity),
            release: hwnd => Instance.ReleaseLayeredWin32(hwnd));
    }

    private readonly object _layeredLock = new();
    private readonly Dictionary<nint, OpenGLBitmapRenderTarget> _layeredTargets = new();
    private readonly Dictionary<nint, Win32LayeredBitmap> _layeredStagingTargets = new();

    private partial IFont CreateFontCore(string family, double size, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        if (OperatingSystem.IsWindows())
        {
            uint dpi = DpiHelper.GetSystemDpi();
            family = ResolveWin32FontFamilyOrFile(family);
            return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);
        }
        else if (OperatingSystem.IsLinux())
        {
            var path = LinuxFontResolver.ResolveFontPath(family, weight, italic);
            int px = (int)Math.Max(1, Math.Round(size)); // Assume 96dpi for now.
            return path != null
                ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px)
                : new BasicFont(family, size, weight, italic, underline, strikethrough);
        }
        else
        {
            throw new PlatformNotSupportedException();
        }
    }

    private partial IFont CreateFontCore(string family, double size, uint dpi, FontWeight weight, bool italic, bool underline, bool strikethrough)
    {
        if (OperatingSystem.IsWindows())
        {
            family = ResolveWin32FontFamilyOrFile(family);
            return new GdiFont(family, size, weight, italic, underline, strikethrough, dpi);
        }
        else if (OperatingSystem.IsLinux())
        {
            var path = LinuxFontResolver.ResolveFontPath(family, weight, italic);
            int px = (int)Math.Max(1, Math.Round(size * dpi / 96.0, MidpointRounding.AwayFromZero));
            return path != null
                ? new FreeTypeFont(family, size, weight, italic, underline, strikethrough, path, px)
                : new BasicFont(family, size, weight, italic, underline, strikethrough);
        }
        else
        {
            throw new PlatformNotSupportedException();
        }
    }

    private static string ResolveWin32FontFamilyOrFile(string familyOrPath)
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

    private partial IOpenGLWindowResources CreateWindowResources(IWindowSurface surface)
    {
        if (OperatingSystem.IsWindows())
        {
            if (surface is not IWin32HdcWindowSurface win32)
            {
                throw new ArgumentException("OpenGL (Win32) requires a Win32 HDC window surface.", nameof(surface));
            }

            // Immediate-mode OpenGL renderer does not need depth/stencil; avoid allocating them (especially with MSAA).
            return WglOpenGLWindowResources.Create(win32.Hwnd, win32.Hdc,
                new WglOpenGLWindowResources.WglPixelFormatOptions(
                    PreferredMsaaSamples: Math.Max(0, 4),
                    DepthBits: 0,
                    StencilBits: 0));
        }
        else if (OperatingSystem.IsLinux())
        {
            if (surface is not Platform.Linux.X11.IX11GlxWindowSurface glx)
            {
                throw new ArgumentException("OpenGL (X11) requires an X11 GLX window surface.", nameof(surface));
            }

            return GlxOpenGLWindowResources.Create(glx.Display, glx.Window, glx.VisualInfo);
        }
        else
        {
            throw new PlatformNotSupportedException();
        }
    }

    private partial IGraphicsContext CreateMeasurementContextCore(uint dpi)
    {
        if (OperatingSystem.IsWindows())
        {
            var hdc = User32.GetDC(0);
            return new GdiMeasurementContext(hdc, dpi);
        }
        else if (OperatingSystem.IsLinux())
        {
            return new OpenGLMeasurementContext(dpi);
        }
        else
        {
            throw new PlatformNotSupportedException();
        }
    }

    private bool PresentLayeredWin32(Window window, IWindowSurface surface, double opacity)
    {
        if (surface is not IWin32LayeredWindowSurface win32Surface ||
            win32Surface.Hwnd == 0 ||
            surface.Kind != WindowSurfaceKind.Layered)
        {
            return false;
        }

        var hwnd = win32Surface.Hwnd;
        int w = Math.Max(1, win32Surface.PixelWidth);
        int h = Math.Max(1, win32Surface.PixelHeight);
        double dpiScale = win32Surface.DpiScale <= 0 ? 1.0 : win32Surface.DpiScale;

        // OpenGL rendering requires an HDC to make the WGL context current.
        nint hdc = User32.GetDC(hwnd);
        if (hdc == 0)
        {
            return true;
        }

        try
        {
            var glTarget = GetOrCreateLayeredTarget(hwnd, w, h, dpiScale);

            _bitmapPresentHwnd = hwnd;
            _bitmapPresentHdc = hdc;
            try
            {
                window.RenderFrameToBitmap(glTarget);
            }
            finally
            {
                _bitmapPresentHwnd = 0;
                _bitmapPresentHdc = 0;
            }

            // Present via UpdateLayeredWindow using a staging top-down DIB.
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

            var created = new OpenGLBitmapRenderTarget(pixelWidth, pixelHeight, dpiScale);
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

    private void ReleaseLayeredWin32(nint hwnd)
    {
        lock (_layeredLock)
        {
            if (_layeredTargets.Remove(hwnd, out var gl))
            {
                gl.Dispose();
            }

            if (_layeredStagingTargets.Remove(hwnd, out var staging))
            {
                staging.Dispose();
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

    // Win32HdcSurface lives in the shared OpenGLGraphicsFactory (used for layered presentation).
}
