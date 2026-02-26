using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal static class MewVGGLBootstrap
{
    private static int _initialized;
    private static nint _openglLib;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
        {
            return;
        }

        NanoVGGL.Initialize(GetProcAddress);
    }

    private static nint GetProcAddress(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return 0;
        }

        nint proc = OpenGL32.wglGetProcAddress(name);
        if (!IsInvalidProc(proc))
        {
            return proc;
        }

        if (_openglLib == 0)
        {
            if (!NativeLibrary.TryLoad("opengl32.dll", out _openglLib))
            {
                _openglLib = 0;
                return 0;
            }
        }

        return NativeLibrary.TryGetExport(_openglLib, name, out proc) ? proc : 0;
    }

    private static bool IsInvalidProc(nint proc)
        => proc == 0 || proc == (nint)1 || proc == (nint)2 || proc == (nint)3 || proc == unchecked((nint)(-1));
}
