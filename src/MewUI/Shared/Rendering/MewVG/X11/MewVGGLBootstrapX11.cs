using System.Runtime.InteropServices;

using Aprillz.MewUI.Native;
using Aprillz.MewVG;

namespace Aprillz.MewUI.Rendering.MewVG;

internal static class MewVGGLBootstrapX11
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

        nint proc = LibGL.glXGetProcAddress(name);
        if (proc != 0)
        {
            return proc;
        }

        if (_openglLib == 0)
        {
            if (!NativeLibrary.TryLoad("libGL.so.1", out _openglLib))
            {
                NativeLibrary.TryLoad("libGL.so", out _openglLib);
            }
        }

        return _openglLib != 0 && NativeLibrary.TryGetExport(_openglLib, name, out proc) ? proc : 0;
    }
}
