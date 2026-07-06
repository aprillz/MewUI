using Aprillz.MewUI.Native;
using Aprillz.MewUI.Native.Structs;
using Aprillz.MewUI.Platform;
using Aprillz.MewUI.Platform.Win32;

namespace Aprillz.MewUI.Rendering.Direct2D;

/// <summary>
/// Layered-window (Bitmap) transparency present path, used when DirectComposition is
/// unavailable (Windows 7). The window content is rendered into a DIB-backed
/// <see cref="Direct2DPixelRenderSurface"/> via an <c>ID2D1DCRenderTarget</c>, then handed to
/// <c>UpdateLayeredWindow</c> for per-pixel-alpha composition by DWM. On Win8+ the factory
/// reports <see cref="Win32TransparencyMode.Surface"/> and this path is not exercised.
/// </summary>
public sealed unsafe partial class Direct2DGraphicsFactory
{
    private const uint ULW_ALPHA = 0x00000002;

    private readonly object _layeredLock = new();
    private readonly Dictionary<nint, Direct2DPixelRenderSurface> _layeredTargets = new();

    public bool Present(Window window, IWindowSurface surface, double opacity)
    {
        if (surface is not IWin32WindowSurface win32Surface || win32Surface.Hwnd == 0)
        {
            return false;
        }

        ArgumentNullException.ThrowIfNull(window);

        nint hwnd = win32Surface.Hwnd;
        int pixelWidth = Math.Max(1, win32Surface.PixelWidth);
        int pixelHeight = Math.Max(1, win32Surface.PixelHeight);
        double dpiScale = win32Surface.DpiScale <= 0 ? 1.0 : win32Surface.DpiScale;

        var target = GetOrCreateLayeredTarget(hwnd, pixelWidth, pixelHeight, dpiScale);

        window.RenderFrameToSurface(target);
        Gdi32.GdiFlush();

        // ID2D1DCRenderTarget blits to the GDI DC at EndDraw, which can leave the DIB's alpha
        // channel at 0 on drawn (RGB != 0) pixels. UpdateLayeredWindow with ULW_ALPHA would then
        // treat the whole window as fully transparent (blank). Mirror the GDI layered path:
        // infer opaque alpha from non-zero RGB, leaving genuinely-cleared pixels transparent so
        // rounded corners / shadows still cut out. No-op where D2D already wrote real alpha.
        FixOpaqueAlphaInPlace(target.GetPixelSpan());

        // UpdateLayeredWindow positions by the window top-left in screen space. Per-pixel
        // transparent windows are borderless (client-origin == window-origin), so the window
        // rect's top-left is the correct destination.
        if (!User32.GetWindowRect(hwnd, out var windowRect))
        {
            return true; // Rendered, but the window vanished before present.
        }

        var dst = new POINT(windowRect.left, windowRect.top);
        var size = new SIZE(pixelWidth, pixelHeight);
        var src = new POINT(0, 0);
        byte alpha = (byte)Math.Round(Math.Clamp(opacity, 0.0, 1.0) * 255.0);
        var blend = BLENDFUNCTION.SourceOver(alpha);

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

    // For each BGRA pixel: if alpha == 0 but RGB is non-zero, set alpha to 255. This rescues
    // content that the DC render target wrote without alpha, while preserving cleared
    // (transparent-black) regions. Pixels with real alpha (A != 0) are untouched.
    private static void FixOpaqueAlphaInPlace(Span<byte> bgra)
    {
        int pixelCount = bgra.Length / 4;
        for (int pixelIndex = 0; pixelIndex < pixelCount; pixelIndex++)
        {
            int offset = pixelIndex * 4;
            if (bgra[offset + 3] != 0)
            {
                continue;
            }

            if (bgra[offset] != 0 || bgra[offset + 1] != 0 || bgra[offset + 2] != 0)
            {
                bgra[offset + 3] = 255;
            }
        }
    }

    private Direct2DPixelRenderSurface GetOrCreateLayeredTarget(nint hwnd, int pixelWidth, int pixelHeight, double dpiScale)
    {
        lock (_layeredLock)
        {
            if (_layeredTargets.TryGetValue(hwnd, out var existing))
            {
                if (existing.PixelWidth == pixelWidth
                    && existing.PixelHeight == pixelHeight
                    && existing.DpiScale == dpiScale)
                {
                    return existing;
                }

                _layeredTargets.Remove(hwnd);
                existing.Dispose();
            }

            var created = new Direct2DPixelRenderSurface(pixelWidth, pixelHeight, dpiScale, hasAlpha: true);
            _layeredTargets[hwnd] = created;
            return created;
        }
    }

    private void ReleaseLayeredTarget(nint hwnd)
    {
        lock (_layeredLock)
        {
            if (_layeredTargets.Remove(hwnd, out var target))
            {
                target.Dispose();
            }
        }
    }

    private void DisposeLayeredTargets()
    {
        lock (_layeredLock)
        {
            foreach (var (_, target) in _layeredTargets)
            {
                target.Dispose();
            }

            _layeredTargets.Clear();
        }
    }
}
