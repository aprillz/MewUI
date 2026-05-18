using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Skia.Rendering;

/// <summary>
/// Raw OpenGL entry points needed to allocate color textures + FBOs for Skia's
/// <c>GRBackendRenderTarget</c>. Owning the GL texture handle lets us hand it back to MewUI
/// via the external raster source contract for zero-copy sampling. Core GL 1.x calls come
/// from <c>opengl32.dll</c> / <c>libGL.so.1</c>; FBO calls are resolved at runtime via
/// <c>wglGetProcAddress</c> / <c>glXGetProcAddress</c>.
/// </summary>
internal static unsafe class SkiaGLInterop
{
    public const uint GL_FRAMEBUFFER = 0x8D40;
    public const uint GL_RENDERBUFFER = 0x8D41;
    public const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
    public const uint GL_DEPTH_STENCIL_ATTACHMENT = 0x821A;
    public const uint GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const uint GL_DEPTH24_STENCIL8 = 0x88F0;

    public const uint GL_TEXTURE_2D = 0x0DE1;
    public const uint GL_RGBA = 0x1908;
    public const uint GL_RGBA8 = 0x8058;
    public const uint GL_UNSIGNED_BYTE = 0x1401;
    public const uint GL_TEXTURE_MIN_FILTER = 0x2801;
    public const uint GL_TEXTURE_MAG_FILTER = 0x2800;
    public const uint GL_TEXTURE_WRAP_S = 0x2802;
    public const uint GL_TEXTURE_WRAP_T = 0x2803;
    public const uint GL_LINEAR = 0x2601;
    public const uint GL_CLAMP_TO_EDGE = 0x812F;

    private static readonly object _lock = new();
    private static bool _loaded;

    private static delegate* unmanaged<int, uint*, void> _glGenFramebuffers;
    private static delegate* unmanaged<int, uint*, void> _glDeleteFramebuffers;
    private static delegate* unmanaged<uint, uint, void> _glBindFramebuffer;
    private static delegate* unmanaged<uint, uint, uint, uint, int, void> _glFramebufferTexture2D;
    private static delegate* unmanaged<int, uint*, void> _glGenRenderbuffers;
    private static delegate* unmanaged<int, uint*, void> _glDeleteRenderbuffers;
    private static delegate* unmanaged<uint, uint, void> _glBindRenderbuffer;
    private static delegate* unmanaged<uint, uint, int, int, void> _glRenderbufferStorage;
    private static delegate* unmanaged<uint, uint, uint, uint, void> _glFramebufferRenderbuffer;
    private static delegate* unmanaged<uint, uint> _glCheckFramebufferStatus;

    public static void GenTextures(int n, out uint textures)
    {
        if (OperatingSystem.IsWindows()) { Win32Gl.glGenTextures(n, out textures); return; }
        if (OperatingSystem.IsLinux()) { X11Gl.glGenTextures(n, out textures); return; }
        throw new PlatformNotSupportedException();
    }

    public static void DeleteTextures(int n, ref uint textures)
    {
        if (OperatingSystem.IsWindows()) { Win32Gl.glDeleteTextures(n, ref textures); return; }
        if (OperatingSystem.IsLinux()) { X11Gl.glDeleteTextures(n, ref textures); return; }
        throw new PlatformNotSupportedException();
    }

    public static void BindTexture(uint target, uint texture)
    {
        if (OperatingSystem.IsWindows()) { Win32Gl.glBindTexture(target, texture); return; }
        if (OperatingSystem.IsLinux()) { X11Gl.glBindTexture(target, texture); return; }
        throw new PlatformNotSupportedException();
    }

    public static void TexParameteri(uint target, uint pname, int param)
    {
        if (OperatingSystem.IsWindows()) { Win32Gl.glTexParameteri(target, pname, param); return; }
        if (OperatingSystem.IsLinux()) { X11Gl.glTexParameteri(target, pname, param); return; }
        throw new PlatformNotSupportedException();
    }

    public static void TexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels)
    {
        if (OperatingSystem.IsWindows()) { Win32Gl.glTexImage2D(target, level, internalformat, width, height, border, format, type, pixels); return; }
        if (OperatingSystem.IsLinux()) { X11Gl.glTexImage2D(target, level, internalformat, width, height, border, format, type, pixels); return; }
        throw new PlatformNotSupportedException();
    }

    public static void GenFramebuffers(int n, uint* framebuffers) { EnsureExtensions(); _glGenFramebuffers(n, framebuffers); }
    public static void DeleteFramebuffers(int n, uint* framebuffers) { EnsureExtensions(); _glDeleteFramebuffers(n, framebuffers); }
    public static void BindFramebuffer(uint target, uint framebuffer) { EnsureExtensions(); _glBindFramebuffer(target, framebuffer); }
    public static void FramebufferTexture2D(uint target, uint attachment, uint textarget, uint texture, int level)
    { EnsureExtensions(); _glFramebufferTexture2D(target, attachment, textarget, texture, level); }
    public static void GenRenderbuffers(int n, uint* renderbuffers) { EnsureExtensions(); _glGenRenderbuffers(n, renderbuffers); }
    public static void DeleteRenderbuffers(int n, uint* renderbuffers) { EnsureExtensions(); _glDeleteRenderbuffers(n, renderbuffers); }
    public static void BindRenderbuffer(uint target, uint renderbuffer) { EnsureExtensions(); _glBindRenderbuffer(target, renderbuffer); }
    public static void RenderbufferStorage(uint target, uint internalformat, int width, int height)
    { EnsureExtensions(); _glRenderbufferStorage(target, internalformat, width, height); }
    public static void FramebufferRenderbuffer(uint target, uint attachment, uint renderbuffertarget, uint renderbuffer)
    { EnsureExtensions(); _glFramebufferRenderbuffer(target, attachment, renderbuffertarget, renderbuffer); }
    public static uint CheckFramebufferStatus(uint target) { EnsureExtensions(); return _glCheckFramebufferStatus(target); }

    private static void EnsureExtensions()
    {
        if (_loaded) return;
        lock (_lock)
        {
            if (_loaded) return;
            if (OperatingSystem.IsWindows())
            {
                _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)Win32Gl.wglGetProcAddress("glGenFramebuffers");
                _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)Win32Gl.wglGetProcAddress("glDeleteFramebuffers");
                _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)Win32Gl.wglGetProcAddress("glBindFramebuffer");
                _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)Win32Gl.wglGetProcAddress("glFramebufferTexture2D");
                _glGenRenderbuffers = (delegate* unmanaged<int, uint*, void>)Win32Gl.wglGetProcAddress("glGenRenderbuffers");
                _glDeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)Win32Gl.wglGetProcAddress("glDeleteRenderbuffers");
                _glBindRenderbuffer = (delegate* unmanaged<uint, uint, void>)Win32Gl.wglGetProcAddress("glBindRenderbuffer");
                _glRenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)Win32Gl.wglGetProcAddress("glRenderbufferStorage");
                _glFramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)Win32Gl.wglGetProcAddress("glFramebufferRenderbuffer");
                _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)Win32Gl.wglGetProcAddress("glCheckFramebufferStatus");
            }
            else if (OperatingSystem.IsLinux())
            {
                _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)X11Gl.glXGetProcAddress("glGenFramebuffers");
                _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)X11Gl.glXGetProcAddress("glDeleteFramebuffers");
                _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)X11Gl.glXGetProcAddress("glBindFramebuffer");
                _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)X11Gl.glXGetProcAddress("glFramebufferTexture2D");
                _glGenRenderbuffers = (delegate* unmanaged<int, uint*, void>)X11Gl.glXGetProcAddress("glGenRenderbuffers");
                _glDeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)X11Gl.glXGetProcAddress("glDeleteRenderbuffers");
                _glBindRenderbuffer = (delegate* unmanaged<uint, uint, void>)X11Gl.glXGetProcAddress("glBindRenderbuffer");
                _glRenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)X11Gl.glXGetProcAddress("glRenderbufferStorage");
                _glFramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)X11Gl.glXGetProcAddress("glFramebufferRenderbuffer");
                _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)X11Gl.glXGetProcAddress("glCheckFramebufferStatus");
            }
            else
            {
                throw new PlatformNotSupportedException("Skia GL hosting is only supported on Win32 and X11.");
            }

            if (_glGenFramebuffers == null || _glDeleteFramebuffers == null || _glBindFramebuffer == null ||
                _glFramebufferTexture2D == null || _glGenRenderbuffers == null || _glDeleteRenderbuffers == null ||
                _glBindRenderbuffer == null || _glRenderbufferStorage == null || _glFramebufferRenderbuffer == null ||
                _glCheckFramebufferStatus == null)
            {
                throw new PlatformNotSupportedException("Required OpenGL FBO entry points unavailable.");
            }

            _loaded = true;
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
