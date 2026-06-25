using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.X11;

using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// EGL implementation of <see cref="IX11GLVisualChooser"/>: chooses an EGL framebuffer config for
/// desktop GL (<c>EGL_OPENGL_API</c>) and resolves its <c>EGL_NATIVE_VISUAL_ID</c> to an X11
/// <c>XVisualInfo</c> so the window can be created with a visual the EGL context can render to.
/// Used when the EGL rendering path is selected; enables dma_buf/EGLImage zero-copy.
/// </summary>
internal sealed class EglVisualChooser : IX11GLVisualChooser
{
    private const long VisualIDMask = 0x1;

    public bool TryChooseVisual(nint display, int screen, bool allowsTransparency, out X11GLVisualInfo visual)
    {
        visual = default;

        nint eglDisplay = LibEgl.eglGetDisplay(display);
        if (eglDisplay == LibEgl.EGL_NO_DISPLAY)
        {
            return false;
        }

        if (!LibEgl.eglInitialize(eglDisplay, out _, out _))
        {
            return false;
        }

        LibEgl.eglBindAPI(LibEgl.EGL_OPENGL_API);

        int[] configAttribs =
        {
            LibEgl.EGL_SURFACE_TYPE, LibEgl.EGL_WINDOW_BIT,
            LibEgl.EGL_RENDERABLE_TYPE, LibEgl.EGL_OPENGL_BIT,
            LibEgl.EGL_RED_SIZE, 8,
            LibEgl.EGL_GREEN_SIZE, 8,
            LibEgl.EGL_BLUE_SIZE, 8,
            LibEgl.EGL_ALPHA_SIZE, allowsTransparency ? 8 : 0,
            LibEgl.EGL_DEPTH_SIZE, 24,
            LibEgl.EGL_STENCIL_SIZE, 8,
            LibEgl.EGL_NONE,
        };

        var configs = new nint[32];
        if (!LibEgl.eglChooseConfig(eglDisplay, configAttribs, configs, configs.Length, out int numConfigs) || numConfigs == 0)
        {
            return false;
        }

        bool haveFallback = false;
        X11GLVisualInfo fallback = default;

        for (int i = 0; i < numConfigs; i++)
        {
            if (!LibEgl.eglGetConfigAttrib(eglDisplay, configs[i], LibEgl.EGL_NATIVE_VISUAL_ID, out int visualId) || visualId == 0)
            {
                continue;
            }

            XVisualInfo template = default;
            template.visualid = visualId;
            nint viPtr = NativeX11.XGetVisualInfo(display, VisualIDMask, ref template, out int count);
            if (viPtr == 0 || count == 0)
            {
                continue;
            }

            var vi = Marshal.PtrToStructure<XVisualInfo>(viPtr);
            NativeX11.XFree(viPtr);

            var candidate = new X11GLVisualInfo(
                vi.visual, vi.visualid, vi.screen, vi.depth, vi.@class,
                vi.red_mask, vi.green_mask, vi.blue_mask, vi.colormap_size, vi.bits_per_rgb);

            // For transparency we need a 32-bit ARGB visual; prefer it, else keep the first valid
            // config as a fallback (opaque window).
            if (!allowsTransparency || vi.depth == 32)
            {
                visual = candidate;
                return true;
            }

            if (!haveFallback)
            {
                fallback = candidate;
                haveFallback = true;
            }
        }

        if (haveFallback)
        {
            visual = fallback;
            return true;
        }

        return false;
    }
}
