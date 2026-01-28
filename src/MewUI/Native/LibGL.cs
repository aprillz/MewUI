using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

// Linux OpenGL + GLX entrypoints.
internal static partial class LibGL
{
    private const string LibraryName = "libGL.so.1";

    // GLX
    [LibraryImport(LibraryName)]
    public static partial nint glXChooseVisual(nint display, int screen, nint attribList);

    [LibraryImport(LibraryName)]
    public static partial nint glXCreateContext(nint display, nint visualInfo, nint shareList, int direct);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool glXMakeCurrent(nint display, nint drawable, nint ctx);

    [LibraryImport(LibraryName)]
    public static partial void glXDestroyContext(nint display, nint ctx);

    [LibraryImport(LibraryName)]
    public static partial void glXSwapBuffers(nint display, nint drawable);

    // GL core
    [LibraryImport(LibraryName)]
    public static partial void glViewport(int x, int y, int width, int height);

    [LibraryImport(LibraryName)]
    public static partial void glMatrixMode(uint mode);

    [LibraryImport(LibraryName)]
    public static partial void glLoadIdentity();

    [LibraryImport(LibraryName)]
    public static partial void glOrtho(double left, double right, double bottom, double top, double zNear, double zFar);

    [LibraryImport(LibraryName)]
    public static partial void glScissor(int x, int y, int width, int height);

    [LibraryImport(LibraryName)]
    public static partial void glEnable(uint cap);

    [LibraryImport(LibraryName)]
    public static partial void glDisable(uint cap);

    [LibraryImport(LibraryName)]
    public static partial void glBlendFunc(uint sfactor, uint dfactor);

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
    public static partial void glReadPixels(int x, int y, int width, int height, uint format, uint type, nint pixels);

    [LibraryImport(LibraryName)]
    public static partial nint glGetString(uint name);

    [LibraryImport(LibraryName)]
    public static partial uint glGetError();

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nint glXGetProcAddress(string procName);
}
