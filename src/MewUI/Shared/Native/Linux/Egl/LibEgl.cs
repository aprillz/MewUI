using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

// Linux EGL entrypoints, used by the optional EGL rendering path on X11 (EglOpenGLWindowResources
// / EglVisualChooser). Desktop GL is requested via eglBindAPI(EGL_OPENGL_API) so the existing
// NanoVG GL3 path runs unchanged; the EGL context is what enables dma_buf/EGLImage zero-copy.
internal static partial class LibEgl
{
    private const string LibraryName = "libEGL.so.1";

    public const uint EGL_OPENGL_API = 0x30A2;
    public const int EGL_NONE = 0x3038;
    public const int EGL_SURFACE_TYPE = 0x3033;
    public const int EGL_WINDOW_BIT = 0x0004;
    public const int EGL_PBUFFER_BIT = 0x0001;
    public const int EGL_RENDERABLE_TYPE = 0x3040;
    public const int EGL_OPENGL_BIT = 0x0008;
    public const int EGL_RED_SIZE = 0x3024;
    public const int EGL_GREEN_SIZE = 0x3023;
    public const int EGL_BLUE_SIZE = 0x3022;
    public const int EGL_ALPHA_SIZE = 0x3021;
    public const int EGL_DEPTH_SIZE = 0x3025;
    public const int EGL_STENCIL_SIZE = 0x3026;
    public const int EGL_NATIVE_VISUAL_ID = 0x302E;

    public static readonly nint EGL_DEFAULT_DISPLAY = 0;
    public static readonly nint EGL_NO_DISPLAY = 0;
    public static readonly nint EGL_NO_CONTEXT = 0;
    public static readonly nint EGL_NO_SURFACE = 0;

    [LibraryImport(LibraryName)]
    public static partial nint eglGetDisplay(nint nativeDisplay);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool eglInitialize(nint dpy, out int major, out int minor);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool eglBindAPI(uint api);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool eglChooseConfig(
        nint dpy,
        [MarshalAs(UnmanagedType.LPArray)] int[] attribList,
        [MarshalAs(UnmanagedType.LPArray), Out] nint[] configs,
        int configSize,
        out int numConfig);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool eglGetConfigAttrib(nint dpy, nint config, int attribute, out int value);

    [LibraryImport(LibraryName)]
    public static partial nint eglCreateContext(nint dpy, nint config, nint shareContext, [MarshalAs(UnmanagedType.LPArray)] int[] attribList);

    [LibraryImport(LibraryName)]
    public static partial nint eglCreateWindowSurface(nint dpy, nint config, nint win, [MarshalAs(UnmanagedType.LPArray)] int[]? attribList);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool eglMakeCurrent(nint dpy, nint draw, nint read, nint ctx);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool eglSwapBuffers(nint dpy, nint surface);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool eglSwapInterval(nint dpy, int interval);

    [LibraryImport(LibraryName)]
    public static partial nint eglGetCurrentContext();

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool eglDestroyContext(nint dpy, nint ctx);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool eglDestroySurface(nint dpy, nint surface);

    [LibraryImport(LibraryName)]
    public static partial int eglGetError();

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint eglGetProcAddress(string procName);
}
