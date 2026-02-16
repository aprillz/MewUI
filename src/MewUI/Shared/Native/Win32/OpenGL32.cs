using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

internal static partial class OpenGL32
{
    private const string LibraryName = "opengl32.dll";

    // WGL
    [LibraryImport(LibraryName)]
    public static partial nint wglCreateContext(nint hdc);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint wglGetProcAddress(string lpszProc);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool wglMakeCurrent(nint hdc, nint hglrc);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool wglDeleteContext(nint hglrc);

    // GL core (minimal subset; fixed-function pipeline)
    [LibraryImport(LibraryName)]
    public static partial void glViewport(int x, int y, int width, int height);

    [LibraryImport(LibraryName)]
    public static partial void glMatrixMode(uint mode);

    [LibraryImport(LibraryName)]
    public static partial void glLoadIdentity();

    [LibraryImport(LibraryName)]
    public static partial void glOrtho(double left, double right, double bottom, double top, double zNear, double zFar);

    [LibraryImport(LibraryName)]
    public static partial void glPushMatrix();

    [LibraryImport(LibraryName)]
    public static partial void glPopMatrix();

    [LibraryImport(LibraryName)]
    public static partial void glTranslatef(float x, float y, float z);

    [LibraryImport(LibraryName)]
    public static partial void glScissor(int x, int y, int width, int height);

    [LibraryImport(LibraryName)]
    public static partial void glEnable(uint cap);

    [LibraryImport(LibraryName)]
    public static partial void glDisable(uint cap);

    [LibraryImport(LibraryName)]
    public static partial void glBlendFunc(uint sfactor, uint dfactor);

    [LibraryImport(LibraryName)]
    public static partial void glBlendFuncSeparate(uint srcRgb, uint dstRgb, uint srcAlpha, uint dstAlpha);

    [LibraryImport(LibraryName)]
    public static partial void glStencilFunc(uint func, int @ref, uint mask);

    [LibraryImport(LibraryName)]
    public static partial void glStencilOp(uint sfail, uint dpfail, uint dppass);

    [LibraryImport(LibraryName)]
    public static partial void glStencilMask(uint mask);

    [LibraryImport(LibraryName)]
    public static partial void glColorMask([MarshalAs(UnmanagedType.Bool)] bool red, [MarshalAs(UnmanagedType.Bool)] bool green, [MarshalAs(UnmanagedType.Bool)] bool blue, [MarshalAs(UnmanagedType.Bool)] bool alpha);

    [LibraryImport(LibraryName)]
    public static partial void glClearStencil(int s);

    [LibraryImport(LibraryName)]
    public static partial void glHint(uint target, uint mode);

    [LibraryImport(LibraryName)]
    public static partial void glClearColor(float red, float green, float blue, float alpha);

    [LibraryImport(LibraryName)]
    public static partial void glClear(uint mask);

    [LibraryImport(LibraryName)]
    public static partial void glLineWidth(float width);

    [LibraryImport(LibraryName)]
    public static partial void glBegin(uint mode);

    [LibraryImport(LibraryName)]
    public static partial void glEnd();

    [LibraryImport(LibraryName)]
    public static partial void glVertex2f(float x, float y);

    [LibraryImport(LibraryName)]
    public static partial void glTexCoord2f(float s, float t);

    [LibraryImport(LibraryName)]
    public static partial void glColor4ub(byte red, byte green, byte blue, byte alpha);

    [LibraryImport(LibraryName)]
    public static partial void glBindTexture(uint target, uint texture);

    [LibraryImport(LibraryName)]
    public static partial void glGenTextures(int n, out uint textures);

    [LibraryImport(LibraryName)]
    public static partial void glDeleteTextures(int n, ref uint textures);

    [LibraryImport(LibraryName)]
    public static partial void glTexParameteri(uint target, uint pname, int param);

    [LibraryImport(LibraryName)]
    public static partial void glTexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels);

    [LibraryImport(LibraryName)]
    public static partial void glTexSubImage2D(uint target, int level, int xoffset, int yoffset, int width, int height, uint format, uint type, nint pixels);

    [LibraryImport(LibraryName)]
    public static partial void glPixelStorei(uint pname, int param);

    [LibraryImport(LibraryName)]
    public static partial void glReadPixels(int x, int y, int width, int height, uint format, uint type, nint pixels);

    [LibraryImport(LibraryName)]
    public static partial nint glGetString(uint name);

    [LibraryImport(LibraryName)]
    public static partial void glGetIntegerv(uint pname, out int data);

    [LibraryImport(LibraryName)]
    public static partial uint glGetError();
}
