using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.X11;

using NativeX11 = Aprillz.MewUI.Native.X11;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// GLX share-root worker context for background offscreen (FBO) rendering. Created with
/// shareList = 0, so it is the root all window contexts share with. Exposed as an
/// <see cref="IOpenGLWindowResources"/>; <see cref="SwapBuffers"/> / <see cref="SetSwapInterval"/>
/// are no-ops because the worker only renders into FBOs.
/// </summary>
internal sealed class GlxWorkerResources : IOpenGLWindowResources
{
    private readonly nint _display;
    // GLX has no surfaceless make-current, so the worker owns a private 1x1 unmapped window.
    // Binding an application window instead lets a worker make-current re-validate the drawable
    // the UI thread draws into, freeing a back buffer that thread is still reading during a resize.
    private readonly nint _drawable;
    private bool _disposed;

    public nint NativeContext { get; }
    public bool SupportsBgra => false;
    public bool SupportsNpotTextures => true;

    private GlxWorkerResources(nint display, nint drawable, nint ctx)
    {
        _display = display;
        _drawable = drawable;
        NativeContext = ctx;
    }

    public static GlxWorkerResources Create(nint display, X11GLVisualInfo visualInfo)
    {
        var (drawable, colormap) = CreateWorkerDrawable(display, visualInfo);

        var native = new XVisualInfo
        {
            visual = visualInfo.Visual,
            visualid = visualInfo.VisualId,
            screen = visualInfo.Screen,
            depth = visualInfo.Depth,
            @class = visualInfo.Class,
            red_mask = visualInfo.RedMask,
            green_mask = visualInfo.GreenMask,
            blue_mask = visualInfo.BlueMask,
            colormap_size = visualInfo.ColormapSize,
            bits_per_rgb = visualInfo.BitsPerRgb,
        };

        nint visualInfoMem = Marshal.AllocHGlobal(Marshal.SizeOf<XVisualInfo>());
        try
        {
            Marshal.StructureToPtr(native, visualInfoMem, fDeleteOld: false);
            // shareList = 0: worker context is the share-list root for all window contexts.
            nint ctx = LibGL.glXCreateContext(display, visualInfoMem, 0, 1);
            if (ctx == 0)
            {
                NativeX11.XDestroyWindow(display, drawable);
                NativeX11.XFreeColormap(display, colormap);
                throw new InvalidOperationException("Worker GLX context: glXCreateContext failed.");
            }

            DiagLog.Write($"[GLX] Worker context created ctx=0x{ctx.ToInt64():X} drawable=0x{drawable.ToInt64():X}");
            return new GlxWorkerResources(display, drawable, ctx);
        }
        finally
        {
            Marshal.FreeHGlobal(visualInfoMem);
        }
    }

    private static (nint Drawable, nint Colormap) CreateWorkerDrawable(nint display, X11GLVisualInfo visualInfo)
    {
        const int ALLOC_NONE = 0;
        const ulong CW_BORDER_PIXEL = 1UL << 3;
        const ulong CW_COLORMAP = 1UL << 13;
        const uint INPUT_OUTPUT = 1;

        nint root = NativeX11.XRootWindow(display, visualInfo.Screen);
        nint colormap = NativeX11.XCreateColormap(display, root, visualInfo.Visual, ALLOC_NONE);
        if (colormap == 0)
        {
            throw new InvalidOperationException("Worker GLX context: XCreateColormap failed.");
        }

        var attributes = new XSetWindowAttributes
        {
            colormap = colormap,
            // A visual depth differing from the parent's needs an explicit border pixel,
            // otherwise XCreateWindow fails with BadMatch.
            border_pixel = 0,
        };
        nint drawable = NativeX11.XCreateWindow(
            display, root, 0, 0, 1, 1, 0,
            visualInfo.Depth, INPUT_OUTPUT, visualInfo.Visual,
            CW_COLORMAP | CW_BORDER_PIXEL, ref attributes);
        if (drawable == 0)
        {
            NativeX11.XFreeColormap(display, colormap);
            throw new InvalidOperationException("Worker GLX context: XCreateWindow failed.");
        }

        return (drawable, colormap);
    }

    public void MakeCurrent(nint deviceOrDisplay)
    {
        if (_disposed) return;
        LibGL.glXMakeCurrent(_display, _drawable, NativeContext);
    }

    public void ReleaseCurrent() => LibGL.glXMakeCurrent(_display, 0, 0);

    public void SwapBuffers(nint deviceOrDisplay, nint nativeWindow) { }

    public void SetSwapInterval(int interval) { }

    public void TrackTexture(uint textureId) { }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        LibGL.glXDestroyContext(_display, NativeContext);
        // The private drawable is left to the X server: destroying it here would race threads
        // that may still hold this context current.
    }
}
