using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.X11;

using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// GLX implementation of <see cref="IX11GLVisualChooser"/>: picks an X11 visual compatible with
/// GLX rendering - a 32-bit ARGB FBConfig when transparency is requested, otherwise an
/// RGBA/double-buffer visual preferring a stencil buffer (for rounded clip and path fills).
/// Registered by the MewVG X11 backend so the platform window layer carries no GL-API calls.
/// (Moved verbatim out of X11WindowBackend.)
/// </summary>
internal sealed class GlxVisualChooser : IX11GLVisualChooser
{
    private const int GLX_X_RENDERABLE = 0x8012;
    private const int GLX_DRAWABLE_TYPE = 0x8010;
    private const int GLX_RENDER_TYPE = 0x8011;
    private const int GLX_WINDOW_BIT = 0x00000001;
    private const int GLX_RGBA_BIT = 0x00000001;
    private const int GLX_ALPHA_SIZE = 11;
    private const int GLX_DEPTH_SIZE = 12;
    private const int GLX_STENCIL_SIZE = 13;

    public unsafe bool TryChooseVisual(nint display, int screen, bool allowsTransparency, out X11GLVisualInfo visual)
    {
        visual = default;

        XVisualInfo visualInfo = default;
        bool usedFbConfig = false;
        bool hasVisualInfo = false;

        // When transparency is requested, prefer a 32-bit ARGB visual via FBConfig.
        if (allowsTransparency)
        {
            int[] fbAttribs =
            {
                GLX_X_RENDERABLE, 1,
                GLX_DRAWABLE_TYPE, GLX_WINDOW_BIT,
                GLX_RENDER_TYPE, GLX_RGBA_BIT,
                4,  // GLX_RGBA
                5,  // GLX_DOUBLEBUFFER
                8,  // GLX_RED_SIZE
                8,
                9,  // GLX_GREEN_SIZE
                8,
                10, // GLX_BLUE_SIZE
                8,
                GLX_ALPHA_SIZE,
                8,
                GLX_DEPTH_SIZE,
                24,
                GLX_STENCIL_SIZE,
                8,
                0
            };

            nint fbConfigs = LibGL.glXChooseFBConfig(display, screen, fbAttribs, out int fbCount);
            if (fbConfigs != 0 && fbCount > 0)
            {
                XVisualInfo? best = null;
                int bestStencil = -1;

                for (int i = 0; i < fbCount; i++)
                {
                    nint fb = Marshal.ReadIntPtr(fbConfigs, i * IntPtr.Size);
                    if (fb == 0)
                    {
                        continue;
                    }

                    int alphaSize = 0;
                    _ = LibGL.glXGetFBConfigAttrib(display, fb, GLX_ALPHA_SIZE, out alphaSize);
                    if (alphaSize < 8)
                    {
                        continue;
                    }

                    int depthSize = 0;
                    _ = LibGL.glXGetFBConfigAttrib(display, fb, GLX_DEPTH_SIZE, out depthSize);

                    int stencilSize = 0;
                    _ = LibGL.glXGetFBConfigAttrib(display, fb, GLX_STENCIL_SIZE, out stencilSize);
                    if (depthSize < 16 || stencilSize < 8)
                    {
                        continue;
                    }

                    nint viPtr = LibGL.glXGetVisualFromFBConfig(display, fb);
                    if (viPtr == 0)
                    {
                        continue;
                    }

                    var vi = Marshal.PtrToStructure<XVisualInfo>(viPtr);
                    NativeX11.XFree(viPtr);

                    if (vi.depth != 32)
                    {
                        continue;
                    }

                    // Prefer configs with a stencil buffer for rounded clip and path fills.
                    if (best == null || stencilSize > bestStencil)
                    {
                        best = vi;
                        bestStencil = stencilSize;
                    }
                }

                NativeX11.XFree(fbConfigs);

                if (best.HasValue)
                {
                    visualInfo = best.Value;
                    usedFbConfig = true;
                    hasVisualInfo = true;
                }
            }
        }

        if (!usedFbConfig)
        {
            int[] attribs = allowsTransparency
                ? [
                    4,  // GLX_RGBA
                    5,  // GLX_DOUBLEBUFFER
                    8,  // GLX_RED_SIZE
                    8,
                    9,  // GLX_GREEN_SIZE
                    8,
                    10, // GLX_BLUE_SIZE
                    8,
                    GLX_ALPHA_SIZE,
                    8,
                    GLX_DEPTH_SIZE,
                    24,
                    GLX_STENCIL_SIZE,
                    8,
                    0
                ]
                : [
                    4,  // GLX_RGBA
                    5,  // GLX_DOUBLEBUFFER
                    8,  // GLX_RED_SIZE
                    8,
                    9,  // GLX_GREEN_SIZE
                    8,
                    10, // GLX_BLUE_SIZE
                    8,
                    GLX_DEPTH_SIZE,
                    24,
                    GLX_STENCIL_SIZE,
                    8,
                    0
                ];

            nint visualInfoPtr;
            fixed (int* p = attribs)
            {
                visualInfoPtr = LibGL.glXChooseVisual(display, screen, (nint)p);
            }

            if (visualInfoPtr == 0)
            {
                // Last resort: no stencil (rounded clip may not work) and no alpha even when
                // transparency was requested, so the window still comes up (opaque) on servers
                // without ARGB GL visuals.
                int[] attribsNoStencil =
                {
                    4,  // GLX_RGBA
                    5,  // GLX_DOUBLEBUFFER
                    8,  // GLX_RED_SIZE
                    8,
                    9,  // GLX_GREEN_SIZE
                    8,
                    10, // GLX_BLUE_SIZE
                    8,
                    GLX_DEPTH_SIZE,
                    24,
                    0
                };

                fixed (int* p = attribsNoStencil)
                {
                    visualInfoPtr = LibGL.glXChooseVisual(display, screen, (nint)p);
                }

                if (visualInfoPtr == 0)
                {
                    return false;
                }
            }

            visualInfo = Marshal.PtrToStructure<XVisualInfo>(visualInfoPtr);
            NativeX11.XFree(visualInfoPtr);
            hasVisualInfo = true;
        }

        if (!hasVisualInfo)
        {
            return false;
        }

        visual = new X11GLVisualInfo(
            visualInfo.visual,
            visualInfo.visualid,
            visualInfo.screen,
            visualInfo.depth,
            visualInfo.@class,
            visualInfo.red_mask,
            visualInfo.green_mask,
            visualInfo.blue_mask,
            visualInfo.colormap_size,
            visualInfo.bits_per_rgb);
        return true;
    }
}
