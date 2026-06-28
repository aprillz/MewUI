using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Platform.Linux.X11;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// GLX share-root worker context for background offscreen (FBO) rendering. Created with
/// shareList = 0, so it is the root all window contexts share with; reuses the first window's
/// drawable for make-current (GLX has no surfaceless current). Exposed as an
/// <see cref="IOpenGLWindowResources"/>; <see cref="SwapBuffers"/> / <see cref="SetSwapInterval"/>
/// are no-ops because the worker only renders into FBOs.
/// </summary>
internal sealed class GlxWorkerResources : IOpenGLWindowResources
{
    private readonly nint _display;
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

    public static GlxWorkerResources Create(nint display, nint drawable, X11GLVisualInfo visualInfo)
    {
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
                throw new InvalidOperationException("Worker GLX context: glXCreateContext failed.");
            }

            DiagLog.Write($"[GLX] Worker context created ctx=0x{ctx.ToInt64():X}");
            return new GlxWorkerResources(display, drawable, ctx);
        }
        finally
        {
            Marshal.FreeHGlobal(visualInfoMem);
        }
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
    }
}
