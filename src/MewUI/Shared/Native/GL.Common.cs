using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

/// <summary>
/// Minimal OpenGL facade used by the OpenGL backend.
/// Platform-specific entrypoints are provided by <c>GLNative</c> in <c>GL.*.cs</c>.
/// </summary>
internal static class GL
{
    internal const uint GL_PROJECTION = 0x1701;
    internal const uint GL_MODELVIEW = 0x1700;

    internal const uint GL_COLOR_BUFFER_BIT = 0x00004000;

    internal const uint GL_BLEND = 0x0BE2;
    internal const uint GL_SCISSOR_TEST = 0x0C11;
    internal const uint GL_STENCIL_TEST = 0x0B90;
    internal const uint GL_TEXTURE_2D = 0x0DE1;
    internal const uint GL_LINE_SMOOTH = 0x0B20;
    internal const uint GL_MULTISAMPLE = 0x809D;

    internal const uint GL_SRC_ALPHA = 0x0302;
    internal const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;
    internal const uint GL_ONE = 0x0001;
    internal const uint GL_ZERO = 0x0000;

    internal const uint GL_QUADS = 0x0007;
    internal const uint GL_LINE_LOOP = 0x0002;
    internal const uint GL_LINE_STRIP = 0x0003;
    internal const uint GL_TRIANGLE_FAN = 0x0006;
    internal const uint GL_TRIANGLES = 0x0004;

    internal const uint GL_RGBA = 0x1908;
    internal const uint GL_ALPHA = 0x1906;
    internal const uint GL_UNSIGNED_BYTE = 0x1401;
    internal const uint GL_BGRA_EXT = 0x80E1;

    internal const uint GL_VENDOR = 0x1F00;
    internal const uint GL_RENDERER = 0x1F01;
    internal const uint GL_VERSION = 0x1F02;
    internal const uint GL_EXTENSIONS = 0x1F03;

    internal const uint GL_SAMPLE_BUFFERS = 0x80A8;
    internal const uint GL_SAMPLES = 0x80A9;
    internal const uint GL_STENCIL_BITS = 0x0D57;

    internal const uint GL_TEXTURE_MIN_FILTER = 0x2801;
    internal const uint GL_TEXTURE_MAG_FILTER = 0x2800;
    internal const uint GL_NEAREST = 0x2600;
    internal const uint GL_LINEAR = 0x2601;
    internal const uint GL_LINEAR_MIPMAP_LINEAR = 0x2703;
    internal const uint GL_TEXTURE_WRAP_S = 0x2802;
    internal const uint GL_TEXTURE_WRAP_T = 0x2803;
    internal const uint GL_CLAMP = 0x2900;
    internal const uint GL_CLAMP_TO_EDGE = 0x812F;

    internal const uint GL_UNPACK_ALIGNMENT = 0x0CF5;
    internal const uint GL_STENCIL_BUFFER_BIT = 0x00000400;
    internal const uint GL_KEEP = 0x1E00;
    internal const uint GL_REPLACE = 0x1E01;
    internal const uint GL_INCR = 0x1E02;
    internal const uint GL_DECR = 0x1E03;
    internal const uint GL_INVERT = 0x150A;
    internal const uint GL_NOTEQUAL = 0x0205;
    internal const uint GL_EQUAL = 0x0202;
    internal const uint GL_ALWAYS = 0x0207;

    internal const uint GL_LINE_SMOOTH_HINT = 0x0C52;
    internal const uint GL_NICEST = 0x1102;

    internal const uint GL_NO_ERROR = 0;

    public static void Viewport(int x, int y, int width, int height) => GLNative.Viewport(x, y, width, height);
    public static void MatrixMode(uint mode) => GLNative.MatrixMode(mode);
    public static void LoadIdentity() => GLNative.LoadIdentity();
    public static void Ortho(double left, double right, double bottom, double top, double zNear, double zFar) => GLNative.Ortho(left, right, bottom, top, zNear, zFar);
    public static void Scissor(int x, int y, int width, int height) => GLNative.Scissor(x, y, width, height);
    public static void Enable(uint cap) => GLNative.Enable(cap);
    public static void Disable(uint cap) => GLNative.Disable(cap);
    public static void BlendFunc(uint sfactor, uint dfactor) => GLNative.BlendFunc(sfactor, dfactor);
    public static void BlendFuncSeparate(uint srcRgb, uint dstRgb, uint srcAlpha, uint dstAlpha)
        => GLNative.BlendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);
    public static void StencilFunc(uint func, int @ref, uint mask) => GLNative.StencilFunc(func, @ref, mask);
    public static void StencilOp(uint sfail, uint dpfail, uint dppass) => GLNative.StencilOp(sfail, dpfail, dppass);
    public static void StencilMask(uint mask) => GLNative.StencilMask(mask);
    public static void ColorMask(bool red, bool green, bool blue, bool alpha) => GLNative.ColorMask(red, green, blue, alpha);
    public static void ClearStencil(int s) => GLNative.ClearStencil(s);
    public static void Hint(uint target, uint mode) => GLNative.Hint(target, mode);
    public static void ClearColor(float red, float green, float blue, float alpha) => GLNative.ClearColor(red, green, blue, alpha);
    public static void Clear(uint mask) => GLNative.Clear(mask);
    public static void LineWidth(float width) => GLNative.LineWidth(width);
    public static void Begin(uint mode) => GLNative.Begin(mode);
    public static void End() => GLNative.End();
    public static void Vertex2f(float x, float y) => GLNative.Vertex2f(x, y);
    public static void TexCoord2f(float s, float t) => GLNative.TexCoord2f(s, t);
    public static void Color4ub(byte red, byte green, byte blue, byte alpha) => GLNative.Color4ub(red, green, blue, alpha);
    public static void BindTexture(uint target, uint texture) => GLNative.BindTexture(target, texture);
    public static void GenTextures(int n, out uint textures) => GLNative.GenTextures(n, out textures);
    public static void DeleteTextures(int n, ref uint textures) => GLNative.DeleteTextures(n, ref textures);
    public static void TexParameteri(uint target, uint pname, int param) => GLNative.TexParameteri(target, pname, param);
    public static void TexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels)
        => GLNative.TexImage2D(target, level, internalformat, width, height, border, format, type, pixels);
    public static void ReadPixels(int x, int y, int width, int height, uint format, uint type, nint pixels)
        => GLNative.ReadPixels(x, y, width, height, format, type, pixels);
    public static nint GetString(uint name) => GLNative.GetString(name);
    public static uint GetError() => GLNative.GetError();

    public static int GetInteger(uint pname)
    {
        GLNative.GetIntegerv(pname, out int value);
        return value;
    }

    public static string? GetVersionString()
    {
        nint p = GetString(GL_VERSION);
        return p == 0 ? null : Marshal.PtrToStringAnsi(p);
    }

    public static string? GetVendorString()
    {
        nint p = GetString(GL_VENDOR);
        return p == 0 ? null : Marshal.PtrToStringAnsi(p);
    }

    public static string? GetRendererString()
    {
        nint p = GetString(GL_RENDERER);
        return p == 0 ? null : Marshal.PtrToStringAnsi(p);
    }

    public static string? GetExtensions()
    {
        nint p = GetString(GL_EXTENSIONS);
        return p == 0 ? null : Marshal.PtrToStringAnsi(p);
    }
}
