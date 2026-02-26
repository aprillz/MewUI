using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

/// <summary>
/// OpenGL entrypoints for macOS (OpenGL.framework).
/// </summary>
internal static partial class GLNative
{
    private const string OpenGLLib = "/System/Library/Frameworks/OpenGL.framework/OpenGL";

    [LibraryImport(OpenGLLib, EntryPoint = "glViewport")] public static partial void Viewport(int x, int y, int width, int height);

    [LibraryImport(OpenGLLib, EntryPoint = "glMatrixMode")] public static partial void MatrixMode(uint mode);

    [LibraryImport(OpenGLLib, EntryPoint = "glLoadIdentity")] public static partial void LoadIdentity();

    [LibraryImport(OpenGLLib, EntryPoint = "glOrtho")] public static partial void Ortho(double left, double right, double bottom, double top, double zNear, double zFar);

    [LibraryImport(OpenGLLib, EntryPoint = "glScissor")] public static partial void Scissor(int x, int y, int width, int height);

    [LibraryImport(OpenGLLib, EntryPoint = "glEnable")] public static partial void Enable(uint cap);

    [LibraryImport(OpenGLLib, EntryPoint = "glDisable")] public static partial void Disable(uint cap);

    [LibraryImport(OpenGLLib, EntryPoint = "glBlendFunc")] public static partial void BlendFunc(uint sfactor, uint dfactor);

    [LibraryImport(OpenGLLib, EntryPoint = "glBlendFuncSeparate")] public static partial void BlendFuncSeparate(uint srcRgb, uint dstRgb, uint srcAlpha, uint dstAlpha);

    [LibraryImport(OpenGLLib, EntryPoint = "glStencilFunc")] public static partial void StencilFunc(uint func, int @ref, uint mask);

    [LibraryImport(OpenGLLib, EntryPoint = "glStencilOp")] public static partial void StencilOp(uint sfail, uint dpfail, uint dppass);

    [LibraryImport(OpenGLLib, EntryPoint = "glStencilMask")] public static partial void StencilMask(uint mask);

    [LibraryImport(OpenGLLib, EntryPoint = "glColorMask")] public static partial void ColorMask([MarshalAs(UnmanagedType.Bool)] bool red, [MarshalAs(UnmanagedType.Bool)] bool green, [MarshalAs(UnmanagedType.Bool)] bool blue, [MarshalAs(UnmanagedType.Bool)] bool alpha);

    [LibraryImport(OpenGLLib, EntryPoint = "glClearStencil")] public static partial void ClearStencil(int s);

    [LibraryImport(OpenGLLib, EntryPoint = "glHint")] public static partial void Hint(uint target, uint mode);

    [LibraryImport(OpenGLLib, EntryPoint = "glClearColor")] public static partial void ClearColor(float red, float green, float blue, float alpha);

    [LibraryImport(OpenGLLib, EntryPoint = "glClear")] public static partial void Clear(uint mask);

    [LibraryImport(OpenGLLib, EntryPoint = "glLineWidth")] public static partial void LineWidth(float width);

    [LibraryImport(OpenGLLib, EntryPoint = "glBegin")] public static partial void Begin(uint mode);

    [LibraryImport(OpenGLLib, EntryPoint = "glEnd")] public static partial void End();

    [LibraryImport(OpenGLLib, EntryPoint = "glVertex2f")] public static partial void Vertex2f(float x, float y);

    [LibraryImport(OpenGLLib, EntryPoint = "glTexCoord2f")] public static partial void TexCoord2f(float s, float t);

    [LibraryImport(OpenGLLib, EntryPoint = "glColor4ub")] public static partial void Color4ub(byte red, byte green, byte blue, byte alpha);

    [LibraryImport(OpenGLLib, EntryPoint = "glBindTexture")] public static partial void BindTexture(uint target, uint texture);

    [LibraryImport(OpenGLLib, EntryPoint = "glGenTextures")] public static partial void GenTextures(int n, out uint textures);

    [LibraryImport(OpenGLLib, EntryPoint = "glDeleteTextures")] public static partial void DeleteTextures(int n, ref uint textures);

    [LibraryImport(OpenGLLib, EntryPoint = "glTexParameteri")] public static partial void TexParameteri(uint target, uint pname, int param);

    [LibraryImport(OpenGLLib, EntryPoint = "glTexImage2D")] public static partial void TexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels);

    [LibraryImport(OpenGLLib, EntryPoint = "glReadPixels")] public static partial void ReadPixels(int x, int y, int width, int height, uint format, uint type, nint pixels);

    [LibraryImport(OpenGLLib, EntryPoint = "glGetString")] public static partial nint GetString(uint name);

    [LibraryImport(OpenGLLib, EntryPoint = "glGetIntegerv")] public static partial void GetIntegerv(uint pname, out int data);

    [LibraryImport(OpenGLLib, EntryPoint = "glGetError")] public static partial uint GetError();
}
