namespace Aprillz.MewUI.Native;

/// <summary>
/// OpenGL extension constants and function pointer loader for FBO support.
/// </summary>
internal static unsafe class OpenGLExt
{
    // FBO constants
    public const uint GL_FRAMEBUFFER = 0x8D40;

    public const uint GL_RENDERBUFFER = 0x8D41;
    public const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
    public const uint GL_DEPTH_ATTACHMENT = 0x8D00;
    public const uint GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const uint GL_DRAW_FRAMEBUFFER = 0x8CA9;
    public const uint GL_READ_FRAMEBUFFER = 0x8CA8;

    // Function pointers
    private static delegate* unmanaged<int, uint*, void> _glGenFramebuffers;

    private static delegate* unmanaged<int, uint*, void> _glDeleteFramebuffers;
    private static delegate* unmanaged<uint, uint, void> _glBindFramebuffer;
    private static delegate* unmanaged<uint, uint, uint, uint, int, void> _glFramebufferTexture2D;
    private static delegate* unmanaged<uint, uint> _glCheckFramebufferStatus;

    private static bool _initialized;
    private static bool _supported;
    private static readonly object _lock = new();

    public static bool IsSupported
    {
        get
        {
            EnsureInitialized();
            return _supported;
        }
    }

    public static void EnsureInitialized()
    {
        if (_initialized)
        {
            return;
        }

        lock (_lock)
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;

            if (OperatingSystem.IsWindows())
            {
                _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)OpenGL32.wglGetProcAddress("glGenFramebuffers");
                _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)OpenGL32.wglGetProcAddress("glDeleteFramebuffers");
                _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)OpenGL32.wglGetProcAddress("glBindFramebuffer");
                _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)OpenGL32.wglGetProcAddress("glFramebufferTexture2D");
                _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)OpenGL32.wglGetProcAddress("glCheckFramebufferStatus");
            }
            else
            {
                _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)LibGL.glXGetProcAddress("glGenFramebuffers");
                _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)LibGL.glXGetProcAddress("glDeleteFramebuffers");
                _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)LibGL.glXGetProcAddress("glBindFramebuffer");
                _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)LibGL.glXGetProcAddress("glFramebufferTexture2D");
                _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)LibGL.glXGetProcAddress("glCheckFramebufferStatus");
            }

            _supported = _glGenFramebuffers != null &&
                         _glDeleteFramebuffers != null &&
                         _glBindFramebuffer != null &&
                         _glFramebufferTexture2D != null &&
                         _glCheckFramebufferStatus != null;
        }
    }

    public static void GenFramebuffers(int n, uint* framebuffers)
    {
        EnsureInitialized();
        if (_glGenFramebuffers != null)
        {
            _glGenFramebuffers(n, framebuffers);
        }
    }

    public static void DeleteFramebuffers(int n, uint* framebuffers)
    {
        EnsureInitialized();
        if (_glDeleteFramebuffers != null)
        {
            _glDeleteFramebuffers(n, framebuffers);
        }
    }

    public static void BindFramebuffer(uint target, uint framebuffer)
    {
        EnsureInitialized();
        if (_glBindFramebuffer != null)
        {
            _glBindFramebuffer(target, framebuffer);
        }
    }

    public static void FramebufferTexture2D(uint target, uint attachment, uint textarget, uint texture, int level)
    {
        EnsureInitialized();
        if (_glFramebufferTexture2D != null)
        {
            _glFramebufferTexture2D(target, attachment, textarget, texture, level);
        }
    }

    public static uint CheckFramebufferStatus(uint target)
    {
        EnsureInitialized();
        if (_glCheckFramebufferStatus != null)
        {
            return _glCheckFramebufferStatus(target);
        }
        return 0;
    }
}
