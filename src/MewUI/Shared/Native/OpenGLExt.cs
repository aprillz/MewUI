namespace Aprillz.MewUI.Native;

/// <summary>
/// OpenGL extension constants and function pointer loader for FBO support.
/// </summary>
internal static unsafe partial class OpenGLExt
{
    // FBO constants
    public const uint GL_FRAMEBUFFER = 0x8D40;
    public const uint GL_RENDERBUFFER = 0x8D41;
    public const uint GL_COLOR_ATTACHMENT0 = 0x8CE0;
    public const uint GL_DEPTH_ATTACHMENT = 0x8D00;
    public const uint GL_STENCIL_ATTACHMENT = 0x8D20;
    public const uint GL_DEPTH_STENCIL_ATTACHMENT = 0x821A;
    public const uint GL_FRAMEBUFFER_COMPLETE = 0x8CD5;
    public const uint GL_DRAW_FRAMEBUFFER = 0x8CA9;
    public const uint GL_READ_FRAMEBUFFER = 0x8CA8;
    public const uint GL_DEPTH_STENCIL = 0x84F9;
    public const uint GL_DEPTH24_STENCIL8 = 0x88F0;

    // Function pointers
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
                LoadFunctionPointersWin32();
            }
            else if (OperatingSystem.IsLinux())
            {
                LoadFunctionPointersX11();
            }
            else
            {
                throw new PlatformNotSupportedException();
            }

            _supported = _glGenFramebuffers != null &&
                         _glDeleteFramebuffers != null &&
                         _glBindFramebuffer != null &&
                         _glFramebufferTexture2D != null &&
                         _glGenRenderbuffers != null &&
                         _glDeleteRenderbuffers != null &&
                         _glBindRenderbuffer != null &&
                         _glRenderbufferStorage != null &&
                         _glFramebufferRenderbuffer != null &&
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

    public static void GenRenderbuffers(int n, uint* renderbuffers)
    {
        EnsureInitialized();
        if (_glGenRenderbuffers != null)
        {
            _glGenRenderbuffers(n, renderbuffers);
        }
    }

    public static void DeleteRenderbuffers(int n, uint* renderbuffers)
    {
        EnsureInitialized();
        if (_glDeleteRenderbuffers != null)
        {
            _glDeleteRenderbuffers(n, renderbuffers);
        }
    }

    public static void BindRenderbuffer(uint target, uint renderbuffer)
    {
        EnsureInitialized();
        if (_glBindRenderbuffer != null)
        {
            _glBindRenderbuffer(target, renderbuffer);
        }
    }

    public static void RenderbufferStorage(uint target, uint internalformat, int width, int height)
    {
        EnsureInitialized();
        if (_glRenderbufferStorage != null)
        {
            _glRenderbufferStorage(target, internalformat, width, height);
        }
    }

    public static void FramebufferRenderbuffer(uint target, uint attachment, uint renderbuffertarget, uint renderbuffer)
    {
        EnsureInitialized();
        if (_glFramebufferRenderbuffer != null)
        {
            _glFramebufferRenderbuffer(target, attachment, renderbuffertarget, renderbuffer);
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
