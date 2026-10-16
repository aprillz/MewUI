using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

// Linux EGL entrypoints: the optional EGL path on X11 (dma_buf/EGLImage zero-copy) and the
// framebuffer backend (GBM window surfaces, surfaceless offscreen contexts).
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

    public const uint EGL_OPENGL_ES_API = 0x30A0;
    public const int EGL_OPENGL_ES3_BIT = 0x0040;
    public const int EGL_CONTEXT_CLIENT_VERSION = 0x3098;
    public const int EGL_WIDTH = 0x3057;
    public const int EGL_HEIGHT = 0x3056;
    public const int EGL_EXTENSIONS = 0x3055;
    public const int EGL_PLATFORM_GBM_KHR = 0x31D7;
    public const int EGL_PLATFORM_SURFACELESS_MESA = 0x31DD;
    public const int EGL_BUFFER_AGE_EXT = 0x313D;

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

    [LibraryImport(LibraryName)]
    public static partial nint eglQueryString(nint dpy, int name);

    [LibraryImport(LibraryName)]
    public static partial nint eglCreatePbufferSurface(nint dpy, nint config, [MarshalAs(UnmanagedType.LPArray)] int[] attribList);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool eglTerminate(nint dpy);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool eglQuerySurface(nint dpy, nint surface, int attribute, out int value);

    /// <summary>Returns the EGL display for a native platform (eglGetPlatformDisplayEXT), or 0 when the extension is missing.</summary>
    public static unsafe nint GetPlatformDisplay(int platform, nint nativeDisplay)
    {
        nint proc = eglGetProcAddress("eglGetPlatformDisplayEXT");
        if (proc == 0)
        {
            return 0;
        }

        var getPlatformDisplay = (delegate* unmanaged<int, nint, int*, nint>)proc;
        return getPlatformDisplay(platform, nativeDisplay, null);
    }

    /// <summary>True when <paramref name="extension"/> appears in the space-separated EGL extension string of <paramref name="dpy"/>.</summary>
    public static bool HasExtension(nint dpy, string extension)
    {
        string? extensions = Marshal.PtrToStringAnsi(eglQueryString(dpy, EGL_EXTENSIONS));
        if (string.IsNullOrEmpty(extensions))
        {
            return false;
        }

        foreach (var token in extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (token == extension)
            {
                return true;
            }
        }

        return false;
    }
}
