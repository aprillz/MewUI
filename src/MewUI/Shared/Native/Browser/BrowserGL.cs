using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

/// <summary>
/// GLES3/WebGL2 entrypoints resolved through the Emscripten context shim. Mirrors the role
/// <c>LibGL</c> plays for GLX: everything is a function pointer because the browser has no
/// import library to link against.
/// </summary>
internal static unsafe partial class BrowserGL
{
    private const string LibraryName = "mewui_webgl_shim";

    private static bool _loaded;

    [LibraryImport(LibraryName, EntryPoint = "mewui_webgl_get_proc", StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint GetProcCore(string name);

    internal static nint GetProcAddress(string name) => GetProcCore(name);

    private static delegate* unmanaged<int, int, int, int, void> _glViewport;
    private static delegate* unmanaged<int, int, int, int, void> _glScissor;
    private static delegate* unmanaged<uint, void> _glEnable;
    private static delegate* unmanaged<uint, void> _glDisable;
    private static delegate* unmanaged<uint, uint, void> _glBlendFunc;
    private static delegate* unmanaged<uint, uint, uint, uint, void> _glBlendFuncSeparate;
    private static delegate* unmanaged<uint, int, uint, void> _glStencilFunc;
    private static delegate* unmanaged<uint, uint, uint, void> _glStencilOp;
    private static delegate* unmanaged<uint, void> _glStencilMask;
    private static delegate* unmanaged<byte, byte, byte, byte, void> _glColorMask;
    private static delegate* unmanaged<int, void> _glClearStencil;
    private static delegate* unmanaged<uint, uint, void> _glHint;
    private static delegate* unmanaged<float, float, float, float, void> _glClearColor;
    private static delegate* unmanaged<uint, void> _glClear;
    private static delegate* unmanaged<void> _glFlush;
    private static delegate* unmanaged<void> _glFinish;
    private static delegate* unmanaged<float, void> _glLineWidth;
    private static delegate* unmanaged<uint, uint, void> _glBindTexture;
    private static delegate* unmanaged<int, uint*, void> _glGenTextures;
    private static delegate* unmanaged<int, uint*, void> _glDeleteTextures;
    private static delegate* unmanaged<uint, uint, int, void> _glTexParameteri;
    private static delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void> _glTexImage2D;
    private static delegate* unmanaged<int, int, int, int, uint, uint, void*, void> _glReadPixels;
    private static delegate* unmanaged<uint, nint> _glGetString;
    private static delegate* unmanaged<uint, int*, void> _glGetIntegerv;
    private static delegate* unmanaged<uint> _glGetError;

    internal static void EnsureLoaded()
    {
        if (_loaded)
        {
            return;
        }

        _loaded = true;
        _glViewport = (delegate* unmanaged<int, int, int, int, void>)GetProcCore("glViewport");
        _glScissor = (delegate* unmanaged<int, int, int, int, void>)GetProcCore("glScissor");
        _glEnable = (delegate* unmanaged<uint, void>)GetProcCore("glEnable");
        _glDisable = (delegate* unmanaged<uint, void>)GetProcCore("glDisable");
        _glBlendFunc = (delegate* unmanaged<uint, uint, void>)GetProcCore("glBlendFunc");
        _glBlendFuncSeparate = (delegate* unmanaged<uint, uint, uint, uint, void>)GetProcCore("glBlendFuncSeparate");
        _glStencilFunc = (delegate* unmanaged<uint, int, uint, void>)GetProcCore("glStencilFunc");
        _glStencilOp = (delegate* unmanaged<uint, uint, uint, void>)GetProcCore("glStencilOp");
        _glStencilMask = (delegate* unmanaged<uint, void>)GetProcCore("glStencilMask");
        _glColorMask = (delegate* unmanaged<byte, byte, byte, byte, void>)GetProcCore("glColorMask");
        _glClearStencil = (delegate* unmanaged<int, void>)GetProcCore("glClearStencil");
        _glHint = (delegate* unmanaged<uint, uint, void>)GetProcCore("glHint");
        _glClearColor = (delegate* unmanaged<float, float, float, float, void>)GetProcCore("glClearColor");
        _glClear = (delegate* unmanaged<uint, void>)GetProcCore("glClear");
        _glFlush = (delegate* unmanaged<void>)GetProcCore("glFlush");
        _glFinish = (delegate* unmanaged<void>)GetProcCore("glFinish");
        _glLineWidth = (delegate* unmanaged<float, void>)GetProcCore("glLineWidth");
        _glBindTexture = (delegate* unmanaged<uint, uint, void>)GetProcCore("glBindTexture");
        _glGenTextures = (delegate* unmanaged<int, uint*, void>)GetProcCore("glGenTextures");
        _glDeleteTextures = (delegate* unmanaged<int, uint*, void>)GetProcCore("glDeleteTextures");
        _glTexParameteri = (delegate* unmanaged<uint, uint, int, void>)GetProcCore("glTexParameteri");
        _glTexImage2D = (delegate* unmanaged<uint, int, int, int, int, int, uint, uint, void*, void>)GetProcCore("glTexImage2D");
        _glReadPixels = (delegate* unmanaged<int, int, int, int, uint, uint, void*, void>)GetProcCore("glReadPixels");
        _glGetString = (delegate* unmanaged<uint, nint>)GetProcCore("glGetString");
        _glGetIntegerv = (delegate* unmanaged<uint, int*, void>)GetProcCore("glGetIntegerv");
        _glGetError = (delegate* unmanaged<uint>)GetProcCore("glGetError");
    }

    internal static void Viewport(int x, int y, int width, int height) { EnsureLoaded(); _glViewport(x, y, width, height); }
    internal static void Scissor(int x, int y, int width, int height) { EnsureLoaded(); _glScissor(x, y, width, height); }
    internal static void Enable(uint cap) { EnsureLoaded(); _glEnable(cap); }
    internal static void Disable(uint cap) { EnsureLoaded(); _glDisable(cap); }
    internal static void BlendFunc(uint sfactor, uint dfactor) { EnsureLoaded(); _glBlendFunc(sfactor, dfactor); }

    internal static void BlendFuncSeparate(uint srcRgb, uint dstRgb, uint srcAlpha, uint dstAlpha)
    {
        EnsureLoaded();
        _glBlendFuncSeparate(srcRgb, dstRgb, srcAlpha, dstAlpha);
    }

    internal static void StencilFunc(uint func, int reference, uint mask) { EnsureLoaded(); _glStencilFunc(func, reference, mask); }
    internal static void StencilOp(uint sfail, uint dpfail, uint dppass) { EnsureLoaded(); _glStencilOp(sfail, dpfail, dppass); }
    internal static void StencilMask(uint mask) { EnsureLoaded(); _glStencilMask(mask); }

    internal static void ColorMask(bool red, bool green, bool blue, bool alpha)
    {
        EnsureLoaded();
        _glColorMask((byte)(red ? 1 : 0), (byte)(green ? 1 : 0), (byte)(blue ? 1 : 0), (byte)(alpha ? 1 : 0));
    }

    internal static void ClearStencil(int s) { EnsureLoaded(); _glClearStencil(s); }
    internal static void Hint(uint target, uint mode) { EnsureLoaded(); _glHint(target, mode); }

    internal static void ClearColor(float red, float green, float blue, float alpha)
    {
        EnsureLoaded();
        _glClearColor(red, green, blue, alpha);
    }

    internal static void Clear(uint mask) { EnsureLoaded(); _glClear(mask); }
    internal static void Flush() { EnsureLoaded(); _glFlush(); }
    internal static void Finish() { EnsureLoaded(); _glFinish(); }
    internal static void LineWidth(float width) { EnsureLoaded(); _glLineWidth(width); }
    internal static void BindTexture(uint target, uint texture) { EnsureLoaded(); _glBindTexture(target, texture); }

    internal static void GenTextures(int n, out uint textures)
    {
        EnsureLoaded();
        uint value;
        _glGenTextures(n, &value);
        textures = value;
    }

    internal static void DeleteTextures(int n, ref uint textures)
    {
        EnsureLoaded();
        fixed (uint* p = &textures)
        {
            _glDeleteTextures(n, p);
        }
    }

    internal static void TexParameteri(uint target, uint pname, int param) { EnsureLoaded(); _glTexParameteri(target, pname, param); }

    internal static void TexImage2D(uint target, int level, int internalformat, int width, int height, int border, uint format, uint type, nint pixels)
    {
        EnsureLoaded();
        _glTexImage2D(target, level, internalformat, width, height, border, format, type, (void*)pixels);
    }

    internal static void ReadPixels(int x, int y, int width, int height, uint format, uint type, nint pixels)
    {
        EnsureLoaded();
        _glReadPixels(x, y, width, height, format, type, (void*)pixels);
    }

    internal static nint GetString(uint name) { EnsureLoaded(); return _glGetString(name); }

    internal static void GetIntegerv(uint pname, out int data)
    {
        EnsureLoaded();
        int value;
        _glGetIntegerv(pname, &value);
        data = value;
    }

    internal static void GetIntegerv(uint pname, int* data) { EnsureLoaded(); _glGetIntegerv(pname, data); }

    internal static uint GetError() { EnsureLoaded(); return _glGetError(); }
}
