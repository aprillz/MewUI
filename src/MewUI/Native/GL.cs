using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

/// <summary>
/// Cross-platform OpenGL dispatch shim (WGL on Windows, GLX on Linux).
/// This avoids scattering OS checks across rendering code.
/// </summary>
internal static class GL
{
    internal const uint GL_PROJECTION = 0x1701;
    internal const uint GL_MODELVIEW = 0x1700;

    internal const uint GL_COLOR_BUFFER_BIT = 0x00004000;

    internal const uint GL_BLEND = 0x0BE2;
    internal const uint GL_SCISSOR_TEST = 0x0C11;
    internal const uint GL_TEXTURE_2D = 0x0DE1;
    internal const uint GL_LINE_SMOOTH = 0x0B20;

    internal const uint GL_SRC_ALPHA = 0x0302;
    internal const uint GL_ONE_MINUS_SRC_ALPHA = 0x0303;

    internal const uint GL_QUADS = 0x0007;
    internal const uint GL_LINE_LOOP = 0x0002;
    internal const uint GL_LINE_STRIP = 0x0003;
    internal const uint GL_TRIANGLE_FAN = 0x0006;

    internal const uint GL_RGBA = 0x1908;
    internal const uint GL_UNSIGNED_BYTE = 0x1401;
    internal const uint GL_BGRA_EXT = 0x80E1;

    internal const uint GL_EXTENSIONS = 0x1F03;

    internal const uint GL_TEXTURE_MIN_FILTER = 0x2801;
    internal const uint GL_TEXTURE_MAG_FILTER = 0x2800;
    internal const uint GL_NEAREST = 0x2600;
    internal const uint GL_LINEAR = 0x2601;
    internal const uint GL_TEXTURE_WRAP_S = 0x2802;
    internal const uint GL_TEXTURE_WRAP_T = 0x2803;
    internal const uint GL_CLAMP = 0x2900;

    internal const uint GL_LINE_SMOOTH_HINT = 0x0C52;
    internal const uint GL_NICEST = 0x1102;

    public static void Viewport(int x, int y, int width, int height)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glViewport(x, y, width, height);
        else LibGL.glViewport(x, y, width, height);
    }

    public static void MatrixMode(uint mode)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glMatrixMode(mode);
        else LibGL.glMatrixMode(mode);
    }

    public static void LoadIdentity()
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glLoadIdentity();
        else LibGL.glLoadIdentity();
    }

    public static void Ortho(double left, double right, double bottom, double top, double zNear, double zFar)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glOrtho(left, right, bottom, top, zNear, zFar);
        else LibGL.glOrtho(left, right, bottom, top, zNear, zFar);
    }

    public static void Scissor(int x, int y, int width, int height)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glScissor(x, y, width, height);
        else LibGL.glScissor(x, y, width, height);
    }

    public static void Enable(uint cap)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glEnable(cap);
        else LibGL.glEnable(cap);
    }

    public static void Disable(uint cap)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glDisable(cap);
        else LibGL.glDisable(cap);
    }

    public static void BlendFunc(uint sfactor, uint dfactor)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glBlendFunc(sfactor, dfactor);
        else LibGL.glBlendFunc(sfactor, dfactor);
    }

    public static void Hint(uint target, uint mode)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glHint(target, mode);
        else LibGL.glHint(target, mode);
    }

    public static void ClearColor(float red, float green, float blue, float alpha)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glClearColor(red, green, blue, alpha);
        else LibGL.glClearColor(red, green, blue, alpha);
    }

    public static void Clear(uint mask)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glClear(mask);
        else LibGL.glClear(mask);
    }

    public static void LineWidth(float width)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glLineWidth(width);
        else LibGL.glLineWidth(width);
    }

    public static void Begin(uint mode)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glBegin(mode);
        else LibGL.glBegin(mode);
    }

    public static void End()
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glEnd();
        else LibGL.glEnd();
    }

    public static void Vertex2f(float x, float y)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glVertex2f(x, y);
        else LibGL.glVertex2f(x, y);
    }

    public static void TexCoord2f(float s, float t)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glTexCoord2f(s, t);
        else LibGL.glTexCoord2f(s, t);
    }

    public static void Color4ub(byte red, byte green, byte blue, byte alpha)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glColor4ub(red, green, blue, alpha);
        else LibGL.glColor4ub(red, green, blue, alpha);
    }

    public static void BindTexture(uint target, uint texture)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glBindTexture(target, texture);
        else LibGL.glBindTexture(target, texture);
    }

    public static void GenTextures(int n, out uint textures)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glGenTextures(n, out textures);
        else LibGL.glGenTextures(n, out textures);
    }

    public static void DeleteTextures(int n, ref uint textures)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glDeleteTextures(n, ref textures);
        else LibGL.glDeleteTextures(n, ref textures);
    }

    public static void TexParameteri(uint target, uint pname, int param)
    {
        if (OperatingSystem.IsWindows()) OpenGL32.glTexParameteri(target, pname, param);
        else LibGL.glTexParameteri(target, pname, param);
    }

    public static void TexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels)
    {
        if (OperatingSystem.IsWindows())
            OpenGL32.glTexImage2D(target, level, internalformat, width, height, border, format, type, pixels);
        else
            LibGL.glTexImage2D(target, level, internalformat, width, height, border, format, type, pixels);
    }

    public static void ReadPixels(int x, int y, int width, int height, uint format, uint type, nint pixels)
    {
        if (OperatingSystem.IsWindows())
            OpenGL32.glReadPixels(x, y, width, height, format, type, pixels);
        else
            LibGL.glReadPixels(x, y, width, height, format, type, pixels);
    }

    public static nint GetString(uint name)
        => OperatingSystem.IsWindows() ? OpenGL32.glGetString(name) : LibGL.glGetString(name);

    public static string? GetExtensions()
    {
        nint p = GetString(GL_EXTENSIONS);
        return p == 0 ? null : Marshal.PtrToStringAnsi(p);
    }
}
