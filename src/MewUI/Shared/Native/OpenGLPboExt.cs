using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

/// <summary>
/// Cross-platform loader for the GL functions needed to do async pixel uploads via
/// Pixel Buffer Objects + fence sync - none of which are part of the GL 1.1 ABI
/// exported by <c>opengl32.dll</c> / <c>libGL.so.1</c>'s direct symbols on every
/// system, so we resolve them dynamically through the platform's procaddress.
/// </summary>
/// <remarks>
/// Functions: <c>glGenBuffers</c>, <c>glDeleteBuffers</c>, <c>glBindBuffer</c>,
/// <c>glBufferData</c>, <c>glBufferSubData</c>, <c>glFenceSync</c>,
/// <c>glClientWaitSync</c>, <c>glDeleteSync</c>. PBO is ARB_pixel_buffer_object
/// (core GL 2.1); fence sync is ARB_sync (core GL 3.2). Both are universally
/// available on desktop drivers.
/// </remarks>
internal static unsafe class OpenGLPboExt
{
    public const uint GL_PIXEL_UNPACK_BUFFER         = 0x88EC;
    public const uint GL_STREAM_DRAW                 = 0x88E0;
    public const uint GL_DYNAMIC_DRAW                = 0x88E8;
    public const uint GL_SYNC_GPU_COMMANDS_COMPLETE  = 0x9117;
    public const uint GL_SYNC_FLUSH_COMMANDS_BIT     = 0x00000001;
    public const uint GL_TIMEOUT_EXPIRED             = 0x911B;
    public const uint GL_CONDITION_SATISFIED         = 0x911C;
    public const uint GL_ALREADY_SIGNALED            = 0x911A;
    public const uint GL_WAIT_FAILED                 = 0x911D;

    public const uint GL_TEXTURE_2D                  = 0x0DE1;
    public const uint GL_RGBA                        = 0x1908;
    public const uint GL_RGBA8                       = 0x8058;
    public const uint GL_BGRA                        = 0x80E1;
    public const uint GL_UNSIGNED_BYTE               = 0x1401;
    public const uint GL_UNSIGNED_INT_8_8_8_8_REV    = 0x8367;
    public const uint GL_TEXTURE_MIN_FILTER          = 0x2800;
    public const uint GL_TEXTURE_MAG_FILTER          = 0x2801;
    public const uint GL_TEXTURE_WRAP_S              = 0x2802;
    public const uint GL_TEXTURE_WRAP_T              = 0x2803;
    public const uint GL_LINEAR                      = 0x2601;
    public const uint GL_CLAMP_TO_EDGE               = 0x812F;
    public const uint GL_UNPACK_ROW_LENGTH           = 0x0CF2;

    // Extension functions (GL 2.1+ / 3.2+) - must come from wglGetProcAddress on Win32
    // because opengl32.dll only exports the GL 1.1 ABI directly. On Linux libGL.so.1
    // dlsym works for all of these too.
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _glGenBuffers;
    private static delegate* unmanaged[Stdcall]<int, uint*, void> _glDeleteBuffers;
    private static delegate* unmanaged[Stdcall]<uint, uint, void> _glBindBuffer;
    private static delegate* unmanaged[Stdcall]<uint, nint, void*, uint, void> _glBufferData;
    private static delegate* unmanaged[Stdcall]<uint, nint, nint, void*, void> _glBufferSubData;
    private static delegate* unmanaged[Stdcall]<uint, uint, nint> _glFenceSync;
    private static delegate* unmanaged[Stdcall]<nint, uint, ulong, uint> _glClientWaitSync;
    private static delegate* unmanaged[Stdcall]<nint, void> _glDeleteSync;

    // GL 1.1 functions (texture management) are NOT loaded here - wglGetProcAddress
    // on Win32 explicitly returns 0 for the GL 1.1 ABI. They're routed through the
    // existing OpenGL32 / LibGL DllImport wrappers via the platform helpers below.

    private static bool _loaded;
    private static bool _available;

    public static bool IsAvailable
    {
        get
        {
            if (_loaded) return _available;
            _loaded = true;
            _available = TryLoad();
            return _available;
        }
    }

    private static bool TryLoad()
    {
        try
        {
            _glGenBuffers      = (delegate* unmanaged[Stdcall]<int, uint*, void>)Resolve("glGenBuffers");
            _glDeleteBuffers   = (delegate* unmanaged[Stdcall]<int, uint*, void>)Resolve("glDeleteBuffers");
            _glBindBuffer      = (delegate* unmanaged[Stdcall]<uint, uint, void>)Resolve("glBindBuffer");
            _glBufferData      = (delegate* unmanaged[Stdcall]<uint, nint, void*, uint, void>)Resolve("glBufferData");
            _glBufferSubData   = (delegate* unmanaged[Stdcall]<uint, nint, nint, void*, void>)Resolve("glBufferSubData");
            _glFenceSync       = (delegate* unmanaged[Stdcall]<uint, uint, nint>)Resolve("glFenceSync");
            _glClientWaitSync  = (delegate* unmanaged[Stdcall]<nint, uint, ulong, uint>)Resolve("glClientWaitSync");
            _glDeleteSync      = (delegate* unmanaged[Stdcall]<nint, void>)Resolve("glDeleteSync");

            return _glGenBuffers != null && _glDeleteBuffers != null
                && _glBindBuffer != null && _glBufferData != null
                && _glBufferSubData != null
                && _glFenceSync != null && _glClientWaitSync != null && _glDeleteSync != null;
        }
        catch
        {
            return false;
        }
    }

    private static nint Resolve(string name)
    {
#if MEWUI_OPENGL_WIN32
        // Win32 opengl32.dll exports only GL 1.1; everything else (including
        // PBO and fence sync) is ICD extension - wglGetProcAddress dispatches
        // to the active driver. Returns 0 if no GL context is current.
        return OpenGL32.wglGetProcAddress(name);
#elif MEWUI_OPENGL_X11
        // libGL.so.1 may export PBO/sync directly (Mesa) or only via
        // glXGetProcAddress (some proprietary stacks). Try both.
        try
        {
            if (NativeLibrary.TryLoad("libGL.so.1", out nint h) &&
                NativeLibrary.TryGetExport(h, name, out nint sym))
            {
                return sym;
            }
        }
        catch { /* swallow - fall back to glXGetProcAddress */ }

        return LibGL.glXGetProcAddress(name);
#else
        // No GL backend compiled in (Metal, GDI-only). PboFenceUploader.IsSupported
        // returns false and the caller falls back to the sync upload path.
        return 0;
#endif
    }

    public static void GenBuffers(int n, uint* buffers) => _glGenBuffers(n, buffers);
    public static void DeleteBuffers(int n, uint* buffers) => _glDeleteBuffers(n, buffers);
    public static void BindBuffer(uint target, uint buffer) => _glBindBuffer(target, buffer);
    public static void BufferData(uint target, nint size, void* data, uint usage) => _glBufferData(target, size, data, usage);
    public static void BufferSubData(uint target, nint offset, nint size, void* data) => _glBufferSubData(target, offset, size, data);
    public static nint FenceSync() => _glFenceSync(GL_SYNC_GPU_COMMANDS_COMPLETE, 0);
    public static uint ClientWaitSync(nint sync, uint flags, ulong timeoutNs) => _glClientWaitSync(sync, flags, timeoutNs);
    public static void DeleteSync(nint sync) => _glDeleteSync(sync);

    // GL 1.1 functions go through the platform's existing DllImport wrappers - these
    // are exported directly from opengl32.dll / libGL.so.1 and never returned by
    // wglGetProcAddress (per Win32 GL spec).
    public static void GenTextures(int n, uint* textures)
    {
#if MEWUI_OPENGL_WIN32
        OpenGL32.glGenTextures(n, out *textures);
#elif MEWUI_OPENGL_X11
        LibGL.glGenTextures(n, out *textures);
#endif
    }

    public static void DeleteTextures(int n, uint* textures)
    {
#if MEWUI_OPENGL_WIN32
        OpenGL32.glDeleteTextures(n, ref *textures);
#elif MEWUI_OPENGL_X11
        LibGL.glDeleteTextures(n, ref *textures);
#endif
    }

    public static void BindTexture(uint target, uint texture)
    {
#if MEWUI_OPENGL_WIN32
        OpenGL32.glBindTexture(target, texture);
#elif MEWUI_OPENGL_X11
        LibGL.glBindTexture(target, texture);
#endif
    }

    public static void TexImage2D(uint target, int level, int internalFormat, int width, int height, int border, uint format, uint type, void* pixels)
    {
#if MEWUI_OPENGL_WIN32
        OpenGL32.glTexImage2D(target, level, internalFormat, width, height, border, format, type, (nint)pixels);
#elif MEWUI_OPENGL_X11
        LibGL.glTexImage2D(target, level, internalFormat, width, height, border, format, type, (nint)pixels);
#endif
    }

    public static void TexParameteri(uint target, uint pname, int param)
    {
#if MEWUI_OPENGL_WIN32
        OpenGL32.glTexParameteri(target, pname, param);
#elif MEWUI_OPENGL_X11
        LibGL.glTexParameteri(target, pname, param);
#endif
    }

    public static void PixelStorei(uint pname, int param)
    {
#if MEWUI_OPENGL_WIN32
        OpenGL32.glPixelStorei(pname, param);
#elif MEWUI_OPENGL_X11
        LibGL.glPixelStorei(pname, param);
#endif
    }
}
