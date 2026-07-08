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

    // Shader / program / VAO / VBO constants (GL 2.0+ / 3.0+)
    public const uint GL_VERTEX_SHADER = 0x8B31;
    public const uint GL_FRAGMENT_SHADER = 0x8B30;
    public const uint GL_COMPILE_STATUS = 0x8B81;
    public const uint GL_LINK_STATUS = 0x8B82;
    public const uint GL_INFO_LOG_LENGTH = 0x8B84;
    public const uint GL_ARRAY_BUFFER = 0x8892;
    public const uint GL_STATIC_DRAW = 0x88E4;
    public const uint GL_FLOAT = 0x1406;
    public const uint GL_TRIANGLE_STRIP = 0x0005;
    public const uint GL_TEXTURE0 = 0x84C0;
    public const uint GL_FRAMEBUFFER_BINDING = 0x8CA6;
    public const uint GL_VIEWPORT = 0x0BA2;
    public const uint GL_CURRENT_PROGRAM = 0x8B8D;
    public const uint GL_VERTEX_ARRAY_BINDING = 0x85B5;
    public const uint GL_ARRAY_BUFFER_BINDING = 0x8894;
    public const uint GL_TEXTURE_BINDING_2D = 0x8069;
    public const uint GL_ACTIVE_TEXTURE = 0x84E0;

    // FBO function pointers
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

    // Shader / program function pointers
    private static delegate* unmanaged<uint, uint> _glCreateShader;
    private static delegate* unmanaged<uint, void> _glDeleteShader;
    private static delegate* unmanaged<uint, int, byte**, int*, void> _glShaderSource;
    private static delegate* unmanaged<uint, void> _glCompileShader;
    private static delegate* unmanaged<uint, uint, int*, void> _glGetShaderiv;
    private static delegate* unmanaged<uint, int, int*, byte*, void> _glGetShaderInfoLog;
    private static delegate* unmanaged<uint> _glCreateProgram;
    private static delegate* unmanaged<uint, void> _glDeleteProgram;
    private static delegate* unmanaged<uint, uint, void> _glAttachShader;
    private static delegate* unmanaged<uint, void> _glLinkProgram;
    private static delegate* unmanaged<uint, uint, int*, void> _glGetProgramiv;
    private static delegate* unmanaged<uint, int, int*, byte*, void> _glGetProgramInfoLog;
    private static delegate* unmanaged<uint, void> _glUseProgram;
    private static delegate* unmanaged<uint, byte*, int> _glGetUniformLocation;
    private static delegate* unmanaged<int, int, void> _glUniform1i;
    private static delegate* unmanaged<int, float, float, void> _glUniform2f;
    private static delegate* unmanaged<int, int, float*, void> _glUniform1fv;

    // VBO / VAO function pointers
    private static delegate* unmanaged<int, uint*, void> _glGenBuffers;
    private static delegate* unmanaged<int, uint*, void> _glDeleteBuffers;
    private static delegate* unmanaged<uint, uint, void> _glBindBuffer;
    private static delegate* unmanaged<uint, nint, void*, uint, void> _glBufferData;
    private static delegate* unmanaged<int, uint*, void> _glGenVertexArrays;
    private static delegate* unmanaged<int, uint*, void> _glDeleteVertexArrays;
    private static delegate* unmanaged<uint, void> _glBindVertexArray;
    private static delegate* unmanaged<uint, int, uint, byte, int, void*, void> _glVertexAttribPointer;
    private static delegate* unmanaged<uint, void> _glEnableVertexAttribArray;
    private static delegate* unmanaged<uint, void> _glActiveTexture;
    private static delegate* unmanaged<uint, int, int, void> _glDrawArrays;

    private static bool _initialized;
    private static bool _supported;
    private static readonly object _lock = new();

    private static partial void LoadFunctionPointers();

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

            LoadFunctionPointers();

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

    /// <summary>
    /// True when the shader / VAO / buffer entrypoints are available - required
    /// for GPU effect passes (e.g. <c>OpenGLGaussianBlur</c>). FBO support
    /// (<see cref="IsSupported"/>) is a prerequisite, since shaders without an
    /// FBO render target are not useful.
    /// </summary>
    public static bool IsShaderPipelineSupported
    {
        get
        {
            EnsureInitialized();
            return _supported &&
                   _glCreateShader != null &&
                   _glShaderSource != null &&
                   _glCompileShader != null &&
                   _glCreateProgram != null &&
                   _glAttachShader != null &&
                   _glLinkProgram != null &&
                   _glUseProgram != null &&
                   _glGetUniformLocation != null &&
                   _glUniform1i != null &&
                   _glUniform2f != null &&
                   _glUniform1fv != null &&
                   _glGenBuffers != null &&
                   _glBindBuffer != null &&
                   _glBufferData != null &&
                   _glGenVertexArrays != null &&
                   _glBindVertexArray != null &&
                   _glVertexAttribPointer != null &&
                   _glEnableVertexAttribArray != null &&
                   _glActiveTexture != null &&
                   _glDrawArrays != null;
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


    public static uint CreateShader(uint shaderType) => _glCreateShader != null ? _glCreateShader(shaderType) : 0;
    public static void DeleteShader(uint shader) { if (_glDeleteShader != null) _glDeleteShader(shader); }
    public static void ShaderSource(uint shader, string source)
    {
        if (_glShaderSource == null) return;
        var bytes = System.Text.Encoding.ASCII.GetBytes(source);
        fixed (byte* p = bytes)
        {
            byte* str = p;
            int len = bytes.Length;
            _glShaderSource(shader, 1, &str, &len);
        }
    }
    public static void CompileShader(uint shader) { if (_glCompileShader != null) _glCompileShader(shader); }
    public static int GetShaderiv(uint shader, uint pname)
    {
        if (_glGetShaderiv == null) return 0;
        int v = 0;
        _glGetShaderiv(shader, pname, &v);
        return v;
    }
    public static string GetShaderInfoLog(uint shader)
    {
        if (_glGetShaderInfoLog == null) return string.Empty;
        int len = GetShaderiv(shader, GL_INFO_LOG_LENGTH);
        if (len <= 0) return string.Empty;
        var buf = new byte[len];
        int written = 0;
        fixed (byte* p = buf) _glGetShaderInfoLog(shader, len, &written, p);
        return System.Text.Encoding.ASCII.GetString(buf, 0, Math.Max(0, written));
    }

    public static uint CreateProgram() => _glCreateProgram != null ? _glCreateProgram() : 0;
    public static void DeleteProgram(uint program) { if (_glDeleteProgram != null) _glDeleteProgram(program); }
    public static void AttachShader(uint program, uint shader) { if (_glAttachShader != null) _glAttachShader(program, shader); }
    public static void LinkProgram(uint program) { if (_glLinkProgram != null) _glLinkProgram(program); }
    public static int GetProgramiv(uint program, uint pname)
    {
        if (_glGetProgramiv == null) return 0;
        int v = 0;
        _glGetProgramiv(program, pname, &v);
        return v;
    }
    public static string GetProgramInfoLog(uint program)
    {
        if (_glGetProgramInfoLog == null) return string.Empty;
        int len = GetProgramiv(program, GL_INFO_LOG_LENGTH);
        if (len <= 0) return string.Empty;
        var buf = new byte[len];
        int written = 0;
        fixed (byte* p = buf) _glGetProgramInfoLog(program, len, &written, p);
        return System.Text.Encoding.ASCII.GetString(buf, 0, Math.Max(0, written));
    }
    public static void UseProgram(uint program) { if (_glUseProgram != null) _glUseProgram(program); }
    public static int GetUniformLocation(uint program, string name)
    {
        if (_glGetUniformLocation == null) return -1;
        var bytes = System.Text.Encoding.ASCII.GetBytes(name + "\0");
        fixed (byte* p = bytes) return _glGetUniformLocation(program, p);
    }
    public static void Uniform1i(int location, int v0) { if (_glUniform1i != null) _glUniform1i(location, v0); }
    public static void Uniform2f(int location, float v0, float v1) { if (_glUniform2f != null) _glUniform2f(location, v0, v1); }
    public static void Uniform1fv(int location, ReadOnlySpan<float> values)
    {
        if (_glUniform1fv == null || values.Length == 0) return;
        fixed (float* p = values) _glUniform1fv(location, values.Length, p);
    }

    public static void GenBuffers(int n, uint* buffers) { if (_glGenBuffers != null) _glGenBuffers(n, buffers); }
    public static void DeleteBuffers(int n, uint* buffers) { if (_glDeleteBuffers != null) _glDeleteBuffers(n, buffers); }
    public static void BindBuffer(uint target, uint buffer) { if (_glBindBuffer != null) _glBindBuffer(target, buffer); }
    public static void BufferData(uint target, nint size, void* data, uint usage)
    {
        if (_glBufferData != null) _glBufferData(target, size, data, usage);
    }
    public static void GenVertexArrays(int n, uint* arrays) { if (_glGenVertexArrays != null) _glGenVertexArrays(n, arrays); }
    public static void DeleteVertexArrays(int n, uint* arrays) { if (_glDeleteVertexArrays != null) _glDeleteVertexArrays(n, arrays); }
    public static void BindVertexArray(uint array) { if (_glBindVertexArray != null) _glBindVertexArray(array); }
    public static void VertexAttribPointer(uint index, int size, uint type, bool normalized, int stride, void* pointer)
    {
        if (_glVertexAttribPointer != null) _glVertexAttribPointer(index, size, type, (byte)(normalized ? 1 : 0), stride, pointer);
    }
    public static void EnableVertexAttribArray(uint index) { if (_glEnableVertexAttribArray != null) _glEnableVertexAttribArray(index); }
    public static void ActiveTexture(uint texture) { if (_glActiveTexture != null) _glActiveTexture(texture); }
    public static void DrawArrays(uint mode, int first, int count) { if (_glDrawArrays != null) _glDrawArrays(mode, first, count); }

    /// <summary>
    /// Provides access to the shader/program function pointer table for callers
    /// that need to load entries via the platform's GL proc loader (X11/macOS).
    /// Win32 loads them via wglGetProcAddress in <see cref="LoadFunctionPointers"/>.
    /// </summary>
    internal static unsafe void SetShaderFunctionPointers(
        delegate* unmanaged<uint, uint> createShader,
        delegate* unmanaged<uint, void> deleteShader,
        delegate* unmanaged<uint, int, byte**, int*, void> shaderSource,
        delegate* unmanaged<uint, void> compileShader,
        delegate* unmanaged<uint, uint, int*, void> getShaderiv,
        delegate* unmanaged<uint, int, int*, byte*, void> getShaderInfoLog,
        delegate* unmanaged<uint> createProgram,
        delegate* unmanaged<uint, void> deleteProgram,
        delegate* unmanaged<uint, uint, void> attachShader,
        delegate* unmanaged<uint, void> linkProgram,
        delegate* unmanaged<uint, uint, int*, void> getProgramiv,
        delegate* unmanaged<uint, int, int*, byte*, void> getProgramInfoLog,
        delegate* unmanaged<uint, void> useProgram,
        delegate* unmanaged<uint, byte*, int> getUniformLocation,
        delegate* unmanaged<int, int, void> uniform1i,
        delegate* unmanaged<int, float, float, void> uniform2f,
        delegate* unmanaged<int, int, float*, void> uniform1fv,
        delegate* unmanaged<int, uint*, void> genBuffers,
        delegate* unmanaged<int, uint*, void> deleteBuffers,
        delegate* unmanaged<uint, uint, void> bindBuffer,
        delegate* unmanaged<uint, nint, void*, uint, void> bufferData,
        delegate* unmanaged<int, uint*, void> genVertexArrays,
        delegate* unmanaged<int, uint*, void> deleteVertexArrays,
        delegate* unmanaged<uint, void> bindVertexArray,
        delegate* unmanaged<uint, int, uint, byte, int, void*, void> vertexAttribPointer,
        delegate* unmanaged<uint, void> enableVertexAttribArray,
        delegate* unmanaged<uint, void> activeTexture,
        delegate* unmanaged<uint, int, int, void> drawArrays)
    {
        _glCreateShader = createShader;
        _glDeleteShader = deleteShader;
        _glShaderSource = shaderSource;
        _glCompileShader = compileShader;
        _glGetShaderiv = getShaderiv;
        _glGetShaderInfoLog = getShaderInfoLog;
        _glCreateProgram = createProgram;
        _glDeleteProgram = deleteProgram;
        _glAttachShader = attachShader;
        _glLinkProgram = linkProgram;
        _glGetProgramiv = getProgramiv;
        _glGetProgramInfoLog = getProgramInfoLog;
        _glUseProgram = useProgram;
        _glGetUniformLocation = getUniformLocation;
        _glUniform1i = uniform1i;
        _glUniform2f = uniform2f;
        _glUniform1fv = uniform1fv;
        _glGenBuffers = genBuffers;
        _glDeleteBuffers = deleteBuffers;
        _glBindBuffer = bindBuffer;
        _glBufferData = bufferData;
        _glGenVertexArrays = genVertexArrays;
        _glDeleteVertexArrays = deleteVertexArrays;
        _glBindVertexArray = bindVertexArray;
        _glVertexAttribPointer = vertexAttribPointer;
        _glEnableVertexAttribArray = enableVertexAttribArray;
        _glActiveTexture = activeTexture;
        _glDrawArrays = drawArrays;
    }
}
