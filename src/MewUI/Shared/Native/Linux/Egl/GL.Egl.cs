namespace Aprillz.MewUI.Native;

/// <summary>
/// OpenGL entrypoints for EGL contexts without a window system, resolved through <see cref="EglGLLoader"/> so
/// the same code runs on desktop GL and GLES.
/// </summary>
internal static unsafe class GLNative
{
    private static readonly delegate* unmanaged<int, int, int, int, void> _glViewport = (delegate* unmanaged<int, int, int, int, void>)EglGLLoader.Resolve("glViewport");
    private static readonly delegate* unmanaged<int, int, int, int, void> _glScissor = (delegate* unmanaged<int, int, int, int, void>)EglGLLoader.Resolve("glScissor");
    private static readonly delegate* unmanaged<uint, void> _glEnable = (delegate* unmanaged<uint, void>)EglGLLoader.Resolve("glEnable");
    private static readonly delegate* unmanaged<uint, void> _glDisable = (delegate* unmanaged<uint, void>)EglGLLoader.Resolve("glDisable");
    private static readonly delegate* unmanaged<uint, uint, void> _glBlendFunc = (delegate* unmanaged<uint, uint, void>)EglGLLoader.Resolve("glBlendFunc");
    private static readonly delegate* unmanaged<uint, uint, uint, uint, void> _glBlendFuncSeparate = (delegate* unmanaged<uint, uint, uint, uint, void>)EglGLLoader.Resolve("glBlendFuncSeparate");
    private static readonly delegate* unmanaged<uint, int, uint, void> _glStencilFunc = (delegate* unmanaged<uint, int, uint, void>)EglGLLoader.Resolve("glStencilFunc");
    private static readonly delegate* unmanaged<uint, uint, uint, void> _glStencilOp = (delegate* unmanaged<uint, uint, uint, void>)EglGLLoader.Resolve("glStencilOp");
    private static readonly delegate* unmanaged<uint, void> _glStencilMask = (delegate* unmanaged<uint, void>)EglGLLoader.Resolve("glStencilMask");
    private static readonly delegate* unmanaged<byte, byte, byte, byte, void> _glColorMask = (delegate* unmanaged<byte, byte, byte, byte, void>)EglGLLoader.Resolve("glColorMask");
    private static readonly delegate* unmanaged<int, void> _glClearStencil = (delegate* unmanaged<int, void>)EglGLLoader.Resolve("glClearStencil");
    private static readonly delegate* unmanaged<uint, uint, void> _glHint = (delegate* unmanaged<uint, uint, void>)EglGLLoader.Resolve("glHint");
    private static readonly delegate* unmanaged<float, float, float, float, void> _glClearColor = (delegate* unmanaged<float, float, float, float, void>)EglGLLoader.Resolve("glClearColor");
    private static readonly delegate* unmanaged<uint, void> _glClear = (delegate* unmanaged<uint, void>)EglGLLoader.Resolve("glClear");
    private static readonly delegate* unmanaged<void> _glFlush = (delegate* unmanaged<void>)EglGLLoader.Resolve("glFlush");
    private static readonly delegate* unmanaged<void> _glFinish = (delegate* unmanaged<void>)EglGLLoader.Resolve("glFinish");
    private static readonly delegate* unmanaged<float, void> _glLineWidth = (delegate* unmanaged<float, void>)EglGLLoader.Resolve("glLineWidth");
    private static readonly delegate* unmanaged<uint, uint, void> _glBindTexture = (delegate* unmanaged<uint, uint, void>)EglGLLoader.Resolve("glBindTexture");
    private static readonly delegate* unmanaged<int, uint*, void> _glGenTextures = (delegate* unmanaged<int, uint*, void>)EglGLLoader.Resolve("glGenTextures");
    private static readonly delegate* unmanaged<int, uint*, void> _glDeleteTextures = (delegate* unmanaged<int, uint*, void>)EglGLLoader.Resolve("glDeleteTextures");
    private static readonly delegate* unmanaged<uint, uint, int, void> _glTexParameteri = (delegate* unmanaged<uint, uint, int, void>)EglGLLoader.Resolve("glTexParameteri");
    private static readonly delegate* unmanaged<uint, int, int, int, int, int, uint, uint, nint, void> _glTexImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, nint, void>)EglGLLoader.Resolve("glTexImage2D");
    private static readonly delegate* unmanaged<int, int, int, int, uint, uint, nint, void> _glReadPixels = (delegate* unmanaged<int, int, int, int, uint, uint, nint, void>)EglGLLoader.Resolve("glReadPixels");
    private static readonly delegate* unmanaged<uint, nint> _glGetString = (delegate* unmanaged<uint, nint>)EglGLLoader.Resolve("glGetString");
    private static readonly delegate* unmanaged<uint, int*, void> _glGetIntegerv = (delegate* unmanaged<uint, int*, void>)EglGLLoader.Resolve("glGetIntegerv");
    private static readonly delegate* unmanaged<uint> _glGetError = (delegate* unmanaged<uint>)EglGLLoader.Resolve("glGetError");
    private static readonly delegate* unmanaged<uint, int, void> _glPixelStorei = (delegate* unmanaged<uint, int, void>)EglGLLoader.Resolve("glPixelStorei");

    public static void Viewport(int x, int y, int width, int height) => _glViewport(x, y, width, height);

    // EGL contexts here are core desktop GL or GLES, neither of which has the fixed-function pipeline.
    // The MewVG backend never draws through it; these keep the shared facade compiling.
    public static void MatrixMode(uint mode) { }

    public static void LoadIdentity() { }

    public static void Ortho(double left, double right, double bottom, double top, double zNear, double zFar) { }

    public static void Begin(uint mode) { }

    public static void End() { }

    public static void Vertex2f(float x, float y) { }

    public static void TexCoord2f(float s, float t) { }

    public static void Color4ub(byte red, byte green, byte blue, byte alpha) { }

    public static void Scissor(int x, int y, int width, int height) => _glScissor(x, y, width, height);

    public static void Enable(uint cap) => _glEnable(cap);

    public static void Disable(uint cap) => _glDisable(cap);

    public static void BlendFunc(uint sfactor, uint dfactor) => _glBlendFunc(sfactor, dfactor);

    public static void BlendFuncSeparate(uint srcRgb, uint dstRgb, uint srcAlpha, uint dstAlpha)
        => _glBlendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);

    public static void StencilFunc(uint func, int @ref, uint mask) => _glStencilFunc(func, @ref, mask);

    public static void StencilOp(uint sfail, uint dpfail, uint dppass) => _glStencilOp(sfail, dpfail, dppass);

    public static void StencilMask(uint mask) => _glStencilMask(mask);

    public static void ColorMask(bool red, bool green, bool blue, bool alpha)
        => _glColorMask(red ? (byte)1 : (byte)0, green ? (byte)1 : (byte)0, blue ? (byte)1 : (byte)0, alpha ? (byte)1 : (byte)0);

    public static void ClearStencil(int s) => _glClearStencil(s);

    public static void Hint(uint target, uint mode) => _glHint(target, mode);

    public static void ClearColor(float red, float green, float blue, float alpha) => _glClearColor(red, green, blue, alpha);

    public static void Clear(uint mask) => _glClear(mask);

    public static void Flush() => _glFlush();

    public static void Finish() => _glFinish();

    public static void LineWidth(float width) => _glLineWidth(width);

    public static void BindTexture(uint target, uint texture) => _glBindTexture(target, texture);

    public static void GenTextures(int n, out uint textures)
    {
        uint id = 0;
        _glGenTextures(n, &id);
        textures = id;
    }

    public static void DeleteTextures(int n, ref uint textures)
    {
        fixed (uint* ids = &textures)
        {
            _glDeleteTextures(n, ids);
        }
    }

    public static void TexParameteri(uint target, uint pname, int param) => _glTexParameteri(target, pname, param);

    public static void TexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels)
        => _glTexImage2D(target, level, internalformat, width, height, border, format, type, pixels);

    public static void ReadPixels(int x, int y, int width, int height, uint format, uint type, nint pixels)
        => _glReadPixels(x, y, width, height, format, type, pixels);

    public static void PixelStorei(uint pname, int param) => _glPixelStorei(pname, param);

    public static nint GetString(uint name) => _glGetString(name);

    public static void GetIntegerv(uint pname, out int data)
    {
        int value = 0;
        _glGetIntegerv(pname, &value);
        data = value;
    }

    public static void GetIntegerv(uint pname, int* data) => _glGetIntegerv(pname, data);

    public static uint GetError() => _glGetError();
}
