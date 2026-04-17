using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Rendering.MewVG;

internal static unsafe class MewVGSkiaOpenGL
{
    public const uint GL_FRAMEBUFFER = 0x8D40;
    public const uint GL_RENDERBUFFER = 0x8D41;
    public const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
    public const uint GL_DEPTH_STENCIL_ATTACHMENT = 0x821A;
    public const uint GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const uint GL_DEPTH24_STENCIL8 = 0x88F0;

    public const uint GL_TEXTURE_2D = 0x0DE1;
    public const uint GL_RGBA = 0x1908;
    public const uint GL_UNSIGNED_BYTE = 0x1401;
    public const uint GL_TEXTURE_MIN_FILTER = 0x2801;
    public const uint GL_TEXTURE_MAG_FILTER = 0x2800;
    public const uint GL_TEXTURE_WRAP_S = 0x2802;
    public const uint GL_TEXTURE_WRAP_T = 0x2803;
    public const uint GL_LINEAR = 0x2601;
    public const uint GL_CLAMP_TO_EDGE = 0x812F;

    private static readonly object s_lock = new();
    private static bool s_loaded;

    private static delegate* unmanaged<int, uint*, void> s_glGenFramebuffers;
    private static delegate* unmanaged<int, uint*, void> s_glDeleteFramebuffers;
    private static delegate* unmanaged<uint, uint, void> s_glBindFramebuffer;
    private static delegate* unmanaged<uint, uint, uint, uint, int, void> s_glFramebufferTexture2D;
    private static delegate* unmanaged<int, uint*, void> s_glGenRenderbuffers;
    private static delegate* unmanaged<int, uint*, void> s_glDeleteRenderbuffers;
    private static delegate* unmanaged<uint, uint, void> s_glBindRenderbuffer;
    private static delegate* unmanaged<uint, uint, int, int, void> s_glRenderbufferStorage;
    private static delegate* unmanaged<uint, uint, uint, uint, void> s_glFramebufferRenderbuffer;
    private static delegate* unmanaged<uint, uint> s_glCheckFramebufferStatus;

    public static void GenTextures(int n, out uint textures)
    {
        if (OperatingSystem.IsWindows())
        {
            Win32Gl.glGenTextures(n, out textures);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            X11Gl.glGenTextures(n, out textures);
            return;
        }

        throw new PlatformNotSupportedException("OpenGL Skia hosting is only supported on Win32 and X11.");
    }

    public static void DeleteTextures(int n, ref uint textures)
    {
        if (OperatingSystem.IsWindows())
        {
            Win32Gl.glDeleteTextures(n, ref textures);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            X11Gl.glDeleteTextures(n, ref textures);
            return;
        }

        throw new PlatformNotSupportedException("OpenGL Skia hosting is only supported on Win32 and X11.");
    }

    public static void BindTexture(uint target, uint texture)
    {
        if (OperatingSystem.IsWindows())
        {
            Win32Gl.glBindTexture(target, texture);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            X11Gl.glBindTexture(target, texture);
            return;
        }

        throw new PlatformNotSupportedException("OpenGL Skia hosting is only supported on Win32 and X11.");
    }

    public static void TexParameteri(uint target, uint pname, int param)
    {
        if (OperatingSystem.IsWindows())
        {
            Win32Gl.glTexParameteri(target, pname, param);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            X11Gl.glTexParameteri(target, pname, param);
            return;
        }

        throw new PlatformNotSupportedException("OpenGL Skia hosting is only supported on Win32 and X11.");
    }

    public static void TexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels)
    {
        if (OperatingSystem.IsWindows())
        {
            Win32Gl.glTexImage2D(target, level, internalformat, width, height, border, format, type, pixels);
            return;
        }

        if (OperatingSystem.IsLinux())
        {
            X11Gl.glTexImage2D(target, level, internalformat, width, height, border, format, type, pixels);
            return;
        }

        throw new PlatformNotSupportedException("OpenGL Skia hosting is only supported on Win32 and X11.");
    }

    public static void GenFramebuffers(int n, uint* framebuffers)
    {
        EnsureExtensionsLoaded();
        s_glGenFramebuffers(n, framebuffers);
    }

    public static void DeleteFramebuffers(int n, uint* framebuffers)
    {
        EnsureExtensionsLoaded();
        s_glDeleteFramebuffers(n, framebuffers);
    }

    public static void BindFramebuffer(uint target, uint framebuffer)
    {
        EnsureExtensionsLoaded();
        s_glBindFramebuffer(target, framebuffer);
    }

    public static void FramebufferTexture2D(uint target, uint attachment, uint textarget, uint texture, int level)
    {
        EnsureExtensionsLoaded();
        s_glFramebufferTexture2D(target, attachment, textarget, texture, level);
    }

    public static void GenRenderbuffers(int n, uint* renderbuffers)
    {
        EnsureExtensionsLoaded();
        s_glGenRenderbuffers(n, renderbuffers);
    }

    public static void DeleteRenderbuffers(int n, uint* renderbuffers)
    {
        EnsureExtensionsLoaded();
        s_glDeleteRenderbuffers(n, renderbuffers);
    }

    public static void BindRenderbuffer(uint target, uint renderbuffer)
    {
        EnsureExtensionsLoaded();
        s_glBindRenderbuffer(target, renderbuffer);
    }

    public static void RenderbufferStorage(uint target, uint internalformat, int width, int height)
    {
        EnsureExtensionsLoaded();
        s_glRenderbufferStorage(target, internalformat, width, height);
    }

    public static void FramebufferRenderbuffer(uint target, uint attachment, uint renderbuffertarget, uint renderbuffer)
    {
        EnsureExtensionsLoaded();
        s_glFramebufferRenderbuffer(target, attachment, renderbuffertarget, renderbuffer);
    }

    public static uint CheckFramebufferStatus(uint target)
    {
        EnsureExtensionsLoaded();
        return s_glCheckFramebufferStatus(target);
    }

    private static void EnsureExtensionsLoaded()
    {
        if (s_loaded)
        {
            return;
        }

        lock (s_lock)
        {
            if (s_loaded)
            {
                return;
            }

            if (OperatingSystem.IsWindows())
            {
                s_glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)Win32Gl.wglGetProcAddress("glGenFramebuffers");
                s_glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)Win32Gl.wglGetProcAddress("glDeleteFramebuffers");
                s_glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)Win32Gl.wglGetProcAddress("glBindFramebuffer");
                s_glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)Win32Gl.wglGetProcAddress("glFramebufferTexture2D");
                s_glGenRenderbuffers = (delegate* unmanaged<int, uint*, void>)Win32Gl.wglGetProcAddress("glGenRenderbuffers");
                s_glDeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)Win32Gl.wglGetProcAddress("glDeleteRenderbuffers");
                s_glBindRenderbuffer = (delegate* unmanaged<uint, uint, void>)Win32Gl.wglGetProcAddress("glBindRenderbuffer");
                s_glRenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)Win32Gl.wglGetProcAddress("glRenderbufferStorage");
                s_glFramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)Win32Gl.wglGetProcAddress("glFramebufferRenderbuffer");
                s_glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)Win32Gl.wglGetProcAddress("glCheckFramebufferStatus");
            }
            else if (OperatingSystem.IsLinux())
            {
                s_glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)X11Gl.glXGetProcAddress("glGenFramebuffers");
                s_glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)X11Gl.glXGetProcAddress("glDeleteFramebuffers");
                s_glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)X11Gl.glXGetProcAddress("glBindFramebuffer");
                s_glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)X11Gl.glXGetProcAddress("glFramebufferTexture2D");
                s_glGenRenderbuffers = (delegate* unmanaged<int, uint*, void>)X11Gl.glXGetProcAddress("glGenRenderbuffers");
                s_glDeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)X11Gl.glXGetProcAddress("glDeleteRenderbuffers");
                s_glBindRenderbuffer = (delegate* unmanaged<uint, uint, void>)X11Gl.glXGetProcAddress("glBindRenderbuffer");
                s_glRenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)X11Gl.glXGetProcAddress("glRenderbufferStorage");
                s_glFramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)X11Gl.glXGetProcAddress("glFramebufferRenderbuffer");
                s_glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)X11Gl.glXGetProcAddress("glCheckFramebufferStatus");
            }
            else
            {
                throw new PlatformNotSupportedException("OpenGL Skia hosting is only supported on Win32 and X11.");
            }

            if (s_glGenFramebuffers == null ||
                s_glDeleteFramebuffers == null ||
                s_glBindFramebuffer == null ||
                s_glFramebufferTexture2D == null ||
                s_glGenRenderbuffers == null ||
                s_glDeleteRenderbuffers == null ||
                s_glBindRenderbuffer == null ||
                s_glRenderbufferStorage == null ||
                s_glFramebufferRenderbuffer == null ||
                s_glCheckFramebufferStatus == null)
            {
                throw new PlatformNotSupportedException("Required OpenGL framebuffer extension entry points are unavailable.");
            }

            s_loaded = true;
        }
    }

    private static class Win32Gl
    {
        private const string LibraryName = "opengl32.dll";

        [DllImport(LibraryName, CharSet = CharSet.Ansi, ExactSpelling = true)]
        public static extern nint wglGetProcAddress(string name);

        [DllImport(LibraryName, ExactSpelling = true)]
        public static extern void glBindTexture(uint target, uint texture);

        [DllImport(LibraryName, ExactSpelling = true)]
        public static extern void glGenTextures(int n, out uint textures);

        [DllImport(LibraryName, ExactSpelling = true)]
        public static extern void glDeleteTextures(int n, ref uint textures);

        [DllImport(LibraryName, ExactSpelling = true)]
        public static extern void glTexParameteri(uint target, uint pname, int param);

        [DllImport(LibraryName, ExactSpelling = true)]
        public static extern void glTexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels);
    }

    private static class X11Gl
    {
        private const string LibraryName = "libGL.so.1";

        [DllImport(LibraryName, CharSet = CharSet.Ansi, ExactSpelling = true)]
        public static extern nint glXGetProcAddress(string procName);

        [DllImport(LibraryName, ExactSpelling = true)]
        public static extern void glBindTexture(uint target, uint texture);

        [DllImport(LibraryName, ExactSpelling = true)]
        public static extern void glGenTextures(int n, out uint textures);

        [DllImport(LibraryName, ExactSpelling = true)]
        public static extern void glDeleteTextures(int n, ref uint textures);

        [DllImport(LibraryName, ExactSpelling = true)]
        public static extern void glTexParameteri(uint target, uint pname, int param);

        [DllImport(LibraryName, ExactSpelling = true)]
        public static extern void glTexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels);
    }
}
