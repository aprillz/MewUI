using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewUI.Core;

namespace Aprillz.MewUI.Rendering.OpenGL;

internal sealed class GlxOpenGLWindowResources : IOpenGLWindowResources
{
    private readonly nint _display;
    private readonly nint _window;
    private bool _disposed;

    public nint GlxContext { get; }
    public bool SupportsBgra { get; }
    public OpenGLTextCache TextCache { get; } = new();

    private GlxOpenGLWindowResources(nint display, nint window, nint ctx, bool supportsBgra)
    {
        _display = display;
        _window = window;
        GlxContext = ctx;
        SupportsBgra = supportsBgra;
    }

    public static GlxOpenGLWindowResources Create(nint display, nint window)
    {
        DiagLog.Write($"GLX create: display=0x{display.ToInt64():X} window=0x{window.ToInt64():X}");

        // Prefer the visual selected when the X11 window was created.
        if (!OpenGLLinuxWindowInfoRegistry.TryGetVisual(window, out var visualInfo))
        {
            int screen = X11.XDefaultScreen(display);

            // Minimal RGBA + double-buffer visual.
            int[] attribs =
            {
                4,  // GLX_RGBA
                5,  // GLX_DOUBLEBUFFER
                8,  // GLX_RED_SIZE
                8,
                9,  // GLX_GREEN_SIZE
                8,
                10, // GLX_BLUE_SIZE
                8,
                11, // GLX_ALPHA_SIZE
                8,
                0   // None
            }
            ;

            unsafe
            {
                fixed (int* p = attribs)
                {
                    nint ptr = LibGL.glXChooseVisual(display, screen, (nint)p);
                    if (ptr == 0)
                        throw new InvalidOperationException("glXChooseVisual failed.");
                    visualInfo = Marshal.PtrToStructure<XVisualInfo>(ptr);
                    X11.XFree(ptr);
                }
            }
        }

        nint visualInfoMem = Marshal.AllocHGlobal(Marshal.SizeOf<XVisualInfo>());
        try
        {
            Marshal.StructureToPtr(visualInfo, visualInfoMem, fDeleteOld: false);

            nint ctx = LibGL.glXCreateContext(display, visualInfoMem, 0, 1);
            if (ctx == 0)
                throw new InvalidOperationException("glXCreateContext failed.");

            if (!LibGL.glXMakeCurrent(display, window, ctx))
                throw new InvalidOperationException("glXMakeCurrent failed.");

            bool supportsBgra = DetectBgraSupport();
            DiagLog.Write($"GLX context ok: ctx=0x{ctx.ToInt64():X} BGRA={supportsBgra}");

            GL.Disable(0x0B71 /* GL_DEPTH_TEST */);
            GL.Disable(0x0B44 /* GL_CULL_FACE */);
            GL.Enable(GL.GL_BLEND);
            GL.BlendFunc(GL.GL_SRC_ALPHA, GL.GL_ONE_MINUS_SRC_ALPHA);
            GL.Enable(GL.GL_TEXTURE_2D);
            GL.Enable(GL.GL_LINE_SMOOTH);
            GL.Hint(GL.GL_LINE_SMOOTH_HINT, GL.GL_NICEST);

            LibGL.glXMakeCurrent(display, 0, 0);

            return new GlxOpenGLWindowResources(display, window, ctx, supportsBgra);
        }
        finally
        {
            Marshal.FreeHGlobal(visualInfoMem);
        }
    }

    private static bool DetectBgraSupport()
    {
        string? extensions = GL.GetExtensions();
        return !string.IsNullOrEmpty(extensions) &&
               extensions.Contains("GL_EXT_bgra", StringComparison.OrdinalIgnoreCase);
    }

    public void MakeCurrent(nint deviceOrDisplay)
    {
        if (_disposed)
            return;
        LibGL.glXMakeCurrent(deviceOrDisplay, _window, GlxContext);
    }

    public void ReleaseCurrent() => LibGL.glXMakeCurrent(_display, 0, 0);

    public void SwapBuffers(nint deviceOrDisplay, nint nativeWindow)
        => LibGL.glXSwapBuffers(deviceOrDisplay, nativeWindow);

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        MakeCurrent(_display);
        TextCache.Dispose();
        ReleaseCurrent();

        LibGL.glXDestroyContext(_display, GlxContext);
    }
}
