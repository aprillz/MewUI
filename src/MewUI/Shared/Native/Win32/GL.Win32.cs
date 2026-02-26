namespace Aprillz.MewUI.Native;

/// <summary>
/// OpenGL entrypoints for Windows (WGL / opengl32.dll).
/// </summary>
internal static class GLNative
{
    private static readonly unsafe delegate* unmanaged<uint, uint, uint, uint, void> s_blendFuncSeparate;

    static unsafe GLNative()
    {
        nint proc = OpenGL32.wglGetProcAddress("glBlendFuncSeparate");
        if (proc == 0)
        {
            proc = OpenGL32.wglGetProcAddress("glBlendFuncSeparateEXT");
        }

        if (proc != 0)
        {
            s_blendFuncSeparate = (delegate* unmanaged<uint, uint, uint, uint, void>)proc;
        }
    }

    public static void Viewport(int x, int y, int width, int height) => OpenGL32.glViewport(x, y, width, height);

    public static void MatrixMode(uint mode) => OpenGL32.glMatrixMode(mode);

    public static void LoadIdentity() => OpenGL32.glLoadIdentity();

    public static void Ortho(double left, double right, double bottom, double top, double zNear, double zFar) => OpenGL32.glOrtho(left, right, bottom, top, zNear, zFar);

    public static void Scissor(int x, int y, int width, int height) => OpenGL32.glScissor(x, y, width, height);

    public static void Enable(uint cap) => OpenGL32.glEnable(cap);

    public static void Disable(uint cap) => OpenGL32.glDisable(cap);

    public static void BlendFunc(uint sfactor, uint dfactor) => OpenGL32.glBlendFunc(sfactor, dfactor);

    public static void BlendFuncSeparate(uint srcRgb, uint dstRgb, uint srcAlpha, uint dstAlpha)
    {
        unsafe
        {
            if (s_blendFuncSeparate != null)
            {
                s_blendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);
                return;
            }
        }

        BlendFunc(srcRgb, dstRgb);
    }

    public static void StencilFunc(uint func, int @ref, uint mask) => OpenGL32.glStencilFunc(func, @ref, mask);

    public static void StencilOp(uint sfail, uint dpfail, uint dppass) => OpenGL32.glStencilOp(sfail, dpfail, dppass);

    public static void StencilMask(uint mask) => OpenGL32.glStencilMask(mask);

    public static void ColorMask(bool red, bool green, bool blue, bool alpha) => OpenGL32.glColorMask(red, green, blue, alpha);

    public static void ClearStencil(int s) => OpenGL32.glClearStencil(s);

    public static void Hint(uint target, uint mode) => OpenGL32.glHint(target, mode);

    public static void ClearColor(float red, float green, float blue, float alpha) => OpenGL32.glClearColor(red, green, blue, alpha);

    public static void Clear(uint mask) => OpenGL32.glClear(mask);

    public static void LineWidth(float width) => OpenGL32.glLineWidth(width);

    public static void Begin(uint mode) => OpenGL32.glBegin(mode);

    public static void End() => OpenGL32.glEnd();

    public static void Vertex2f(float x, float y) => OpenGL32.glVertex2f(x, y);

    public static void TexCoord2f(float s, float t) => OpenGL32.glTexCoord2f(s, t);

    public static void Color4ub(byte red, byte green, byte blue, byte alpha) => OpenGL32.glColor4ub(red, green, blue, alpha);

    public static void BindTexture(uint target, uint texture) => OpenGL32.glBindTexture(target, texture);

    public static void GenTextures(int n, out uint textures) => OpenGL32.glGenTextures(n, out textures);

    public static void DeleteTextures(int n, ref uint textures) => OpenGL32.glDeleteTextures(n, ref textures);

    public static void TexParameteri(uint target, uint pname, int param) => OpenGL32.glTexParameteri(target, pname, param);

    public static void TexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels) => OpenGL32.glTexImage2D(target, level, internalformat, width, height, border, format, type, pixels);

    public static void ReadPixels(int x, int y, int width, int height, uint format, uint type, nint pixels) => OpenGL32.glReadPixels(x, y, width, height, format, type, pixels);

    public static nint GetString(uint name) => OpenGL32.glGetString(name);

    public static void GetIntegerv(uint pname, out int data) => OpenGL32.glGetIntegerv(pname, out data);

    public static uint GetError() => OpenGL32.glGetError();
}
