namespace Aprillz.MewUI.Native;

/// <summary>
/// OpenGL entrypoints for the browser (Emscripten GLES3 / WebGL2).
/// </summary>
internal static class GLNative
{
    public static void Viewport(int x, int y, int width, int height) => BrowserGL.Viewport(x, y, width, height);

    // GLES3 dropped the fixed-function pipeline. These stay as no-ops so the shared facade still
    // compiles; the browser backend draws through MewVG and never takes the legacy path.
    public static void MatrixMode(uint mode) { }

    public static void LoadIdentity() { }

    public static void Ortho(double left, double right, double bottom, double top, double zNear, double zFar) { }

    public static void Begin(uint mode) { }

    public static void End() { }

    public static void Vertex2f(float x, float y) { }

    public static void TexCoord2f(float s, float t) { }

    public static void Color4ub(byte red, byte green, byte blue, byte alpha) { }

    public static void Scissor(int x, int y, int width, int height) => BrowserGL.Scissor(x, y, width, height);

    public static void Enable(uint cap) => BrowserGL.Enable(cap);

    public static void Disable(uint cap) => BrowserGL.Disable(cap);

    public static void BlendFunc(uint sfactor, uint dfactor) => BrowserGL.BlendFunc(sfactor, dfactor);

    public static void BlendFuncSeparate(uint srcRgb, uint dstRgb, uint srcAlpha, uint dstAlpha)
        => BrowserGL.BlendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);

    public static void StencilFunc(uint func, int @ref, uint mask) => BrowserGL.StencilFunc(func, @ref, mask);

    public static void StencilOp(uint sfail, uint dpfail, uint dppass) => BrowserGL.StencilOp(sfail, dpfail, dppass);

    public static void StencilMask(uint mask) => BrowserGL.StencilMask(mask);

    public static void ColorMask(bool red, bool green, bool blue, bool alpha) => BrowserGL.ColorMask(red, green, blue, alpha);

    public static void ClearStencil(int s) => BrowserGL.ClearStencil(s);

    public static void Hint(uint target, uint mode) => BrowserGL.Hint(target, mode);

    public static void ClearColor(float red, float green, float blue, float alpha) => BrowserGL.ClearColor(red, green, blue, alpha);

    public static void Clear(uint mask) => BrowserGL.Clear(mask);

    public static void Flush() => BrowserGL.Flush();

    public static void Finish() => BrowserGL.Finish();

    public static void LineWidth(float width) => BrowserGL.LineWidth(width);

    public static void BindTexture(uint target, uint texture) => BrowserGL.BindTexture(target, texture);

    public static void GenTextures(int n, out uint textures) => BrowserGL.GenTextures(n, out textures);

    public static void DeleteTextures(int n, ref uint textures) => BrowserGL.DeleteTextures(n, ref textures);

    public static void TexParameteri(uint target, uint pname, int param) => BrowserGL.TexParameteri(target, pname, param);

    public static void TexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels)
        => BrowserGL.TexImage2D(target, level, internalformat, width, height, border, format, type, pixels);

    public static void ReadPixels(int x, int y, int width, int height, uint format, uint type, nint pixels)
        => BrowserGL.ReadPixels(x, y, width, height, format, type, pixels);

    public static nint GetString(uint name) => BrowserGL.GetString(name);

    public static void GetIntegerv(uint pname, out int data) => BrowserGL.GetIntegerv(pname, out data);

    public static unsafe void GetIntegerv(uint pname, int* data) => BrowserGL.GetIntegerv(pname, data);

    public static uint GetError() => BrowserGL.GetError();
}
