namespace Aprillz.MewUI.Native;

/// <summary>
/// OpenGL entrypoints for X11/Linux (GLX / libGL).
/// </summary>
internal static class GLNativeX11
{
    public static void Viewport(int x, int y, int width, int height) => LibGL.glViewport(x, y, width, height);

    public static void MatrixMode(uint mode) => LibGL.glMatrixMode(mode);

    public static void LoadIdentity() => LibGL.glLoadIdentity();

    public static void Ortho(double left, double right, double bottom, double top, double zNear, double zFar) => LibGL.glOrtho(left, right, bottom, top, zNear, zFar);

    public static void Scissor(int x, int y, int width, int height) => LibGL.glScissor(x, y, width, height);

    public static void Enable(uint cap) => LibGL.glEnable(cap);

    public static void Disable(uint cap) => LibGL.glDisable(cap);

    public static void BlendFunc(uint sfactor, uint dfactor) => LibGL.glBlendFunc(sfactor, dfactor);

    public static void BlendFuncSeparate(uint srcRgb, uint dstRgb, uint srcAlpha, uint dstAlpha)
        => LibGL.glBlendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);

    public static void Hint(uint target, uint mode) => LibGL.glHint(target, mode);

    public static void ClearColor(float red, float green, float blue, float alpha) => LibGL.glClearColor(red, green, blue, alpha);

    public static void Clear(uint mask) => LibGL.glClear(mask);

    public static void LineWidth(float width) => LibGL.glLineWidth(width);

    public static void Begin(uint mode) => LibGL.glBegin(mode);

    public static void End() => LibGL.glEnd();

    public static void Vertex2f(float x, float y) => LibGL.glVertex2f(x, y);

    public static void TexCoord2f(float s, float t) => LibGL.glTexCoord2f(s, t);

    public static void Color4ub(byte red, byte green, byte blue, byte alpha) => LibGL.glColor4ub(red, green, blue, alpha);

    public static void BindTexture(uint target, uint texture) => LibGL.glBindTexture(target, texture);

    public static void GenTextures(int n, out uint textures) => LibGL.glGenTextures(n, out textures);

    public static void DeleteTextures(int n, ref uint textures) => LibGL.glDeleteTextures(n, ref textures);

    public static void TexParameteri(uint target, uint pname, int param) => LibGL.glTexParameteri(target, pname, param);

    public static void TexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels) => LibGL.glTexImage2D(target, level, internalformat, width, height, border, format, type, pixels);

    public static void ReadPixels(int x, int y, int width, int height, uint format, uint type, nint pixels) => LibGL.glReadPixels(x, y, width, height, format, type, pixels);

    public static nint GetString(uint name) => LibGL.glGetString(name);

    public static void GetIntegerv(uint pname, out int data) => LibGL.glGetIntegerv(pname, out data);

    public static uint GetError() => LibGL.glGetError();
}
