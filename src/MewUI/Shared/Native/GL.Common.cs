using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Xml.Linq;

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

    public static void Viewport(int x, int y, int width, int height)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.Viewport(x, y, width, height);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.Viewport(x, y, width, height);
        else throw new PlatformNotSupportedException();
    }

    public static void MatrixMode(uint mode)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.MatrixMode(mode);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.MatrixMode(mode);
        else throw new PlatformNotSupportedException();
    }

    public static void LoadIdentity()
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.LoadIdentity();
        else if (OperatingSystem.IsLinux())
            GLNativeX11.LoadIdentity();
        else throw new PlatformNotSupportedException();
    }

    public static void Ortho(double left, double right, double bottom, double top, double zNear, double zFar)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.Ortho(left, right, bottom, top, zNear, zFar);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.Ortho(left, right, bottom, top, zNear, zFar);
        else throw new PlatformNotSupportedException();
    }

    public static void Scissor(int x, int y, int width, int height)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.Scissor(x, y, width, height);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.Scissor(x, y, width, height);
        else throw new PlatformNotSupportedException();
    }

    public static void Enable(uint cap)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.Enable(cap);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.Enable(cap);
        else throw new PlatformNotSupportedException();
    }

    public static void Disable(uint cap)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.Disable(cap);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.Disable(cap);
        else throw new PlatformNotSupportedException();
    }

    public static void BlendFunc(uint sfactor, uint dfactor)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.BlendFunc(sfactor, dfactor);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.BlendFunc(sfactor, dfactor);
        else throw new PlatformNotSupportedException();
    }

    public static void BlendFuncSeparate(uint srcRgb, uint dstRgb, uint srcAlpha, uint dstAlpha)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.BlendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.BlendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);
        else throw new PlatformNotSupportedException();
    }

    public static void StencilFunc(uint func, int @ref, uint mask)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.StencilFunc(func, @ref, mask);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.StencilFunc(func, @ref, mask);
        else throw new PlatformNotSupportedException();
    }

    public static void StencilOp(uint sfail, uint dpfail, uint dppass)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.StencilOp(sfail, dpfail, dppass);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.StencilOp(sfail, dpfail, dppass);
        else throw new PlatformNotSupportedException();
    }

    public static void StencilMask(uint mask)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.StencilMask(mask);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.StencilMask(mask);
        else throw new PlatformNotSupportedException();
    }

    public static void ColorMask(bool red, bool green, bool blue, bool alpha)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.ColorMask(red, green, blue, alpha);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.ColorMask(red, green, blue, alpha);
        else throw new PlatformNotSupportedException();
    }

    public static void ClearStencil(int s)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.ClearStencil(s);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.ClearStencil(s);
        else throw new PlatformNotSupportedException();
    }

    public static void Hint(uint target, uint mode)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.Hint(target, mode);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.Hint(target, mode);
        else throw new PlatformNotSupportedException();
    }

    public static void ClearColor(float red, float green, float blue, float alpha)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.ClearColor(red, green, blue, alpha);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.ClearColor(red, green, blue, alpha);
        else throw new PlatformNotSupportedException();
    }

    public static void Clear(uint mask)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.Clear(mask);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.Clear(mask);
        else throw new PlatformNotSupportedException();
    }

    public static void LineWidth(float width)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.LineWidth(width);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.LineWidth(width);
        else throw new PlatformNotSupportedException();
    }

    public static void Begin(uint mode)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.Begin(mode);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.Begin(mode);
        else throw new PlatformNotSupportedException();
    }

    public static void End()
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.End();
        else if (OperatingSystem.IsLinux())
            GLNativeX11.End();
        else throw new PlatformNotSupportedException();
    }

    public static void Vertex2f(float x, float y)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.Vertex2f(x, y);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.Vertex2f(x, y);
        else throw new PlatformNotSupportedException();
    }

    public static void TexCoord2f(float s, float t)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.TexCoord2f(s, t);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.TexCoord2f(s, t);
        else throw new PlatformNotSupportedException();
    }

    public static void Color4ub(byte red, byte green, byte blue, byte alpha)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.Color4ub(red, green, blue, alpha);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.Color4ub(red, green, blue, alpha);
        else throw new PlatformNotSupportedException();
    }

    public static void BindTexture(uint target, uint texture)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.BindTexture(target, texture);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.BindTexture(target, texture);
        else throw new PlatformNotSupportedException();
    }

    public static void GenTextures(int n, out uint textures)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.GenTextures(n, out textures);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.GenTextures(n, out textures);
        else throw new PlatformNotSupportedException();
    }

    public static void DeleteTextures(int n, ref uint textures)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.DeleteTextures(n, ref textures);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.DeleteTextures(n, ref textures);
        else throw new PlatformNotSupportedException();
    }

    public static void TexParameteri(uint target, uint pname, int param)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.TexParameteri(target, pname, param);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.TexParameteri(target, pname, param);
        else throw new PlatformNotSupportedException();
    }

    public static void TexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.TexImage2D(target, level, internalformat, width, height, border, format, type, pixels);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.TexImage2D(target, level, internalformat, width, height, border, format, type, pixels);
        else throw new PlatformNotSupportedException();
    }

    public static void ReadPixels(int x, int y, int width, int height, uint format, uint type, nint pixels)
    {
        if (OperatingSystem.IsWindows())
            GLNativeWin32.ReadPixels(x, y, width, height, format, type, pixels);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.ReadPixels(x, y, width, height, format, type, pixels);
        else throw new PlatformNotSupportedException();
    }

    public static nint GetString(uint name)
    {
        if (OperatingSystem.IsWindows())
            return GLNativeWin32.GetString(name);
        else if (OperatingSystem.IsLinux())
            return GLNativeX11.GetString(name);
        else throw new PlatformNotSupportedException();
    }

    public static uint GetError()
    {
        if (OperatingSystem.IsWindows())
            return GLNativeWin32.GetError();
        else if (OperatingSystem.IsLinux())
            return GLNativeX11.GetError();
        else throw new PlatformNotSupportedException();
    }

    public static int GetInteger(uint pname)
    {
        int value;
        if (OperatingSystem.IsWindows())
            GLNativeWin32.GetIntegerv(pname, out value);
        else if (OperatingSystem.IsLinux())
            GLNativeX11.GetIntegerv(pname, out value);
        else throw new PlatformNotSupportedException();

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
