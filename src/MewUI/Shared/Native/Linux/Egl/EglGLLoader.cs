using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

/// <summary>
/// Resolves GL entry points for an EGL context without a window system: eglGetProcAddress first (Mesa
/// returns core functions too), then the exports of the desktop GL or GLES library.
/// </summary>
internal static class EglGLLoader
{
    private static readonly object _gate = new();
    private static nint _desktopLibrary;
    private static nint _glesLibrary;
    private static bool _librariesProbed;

    public static nint Resolve(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return 0;
        }

        nint proc = LibEgl.eglGetProcAddress(name);
        if (proc != 0)
        {
            return proc;
        }

        EnsureLibraries();
        if (_desktopLibrary != 0 && NativeLibrary.TryGetExport(_desktopLibrary, name, out proc))
        {
            return proc;
        }

        return _glesLibrary != 0 && NativeLibrary.TryGetExport(_glesLibrary, name, out proc) ? proc : 0;
    }

    private static void EnsureLibraries()
    {
        if (Volatile.Read(ref _librariesProbed))
        {
            return;
        }

        lock (_gate)
        {
            if (_librariesProbed)
            {
                return;
            }

            if (!NativeLibrary.TryLoad("libOpenGL.so.0", out _desktopLibrary))
            {
                NativeLibrary.TryLoad("libGL.so.1", out _desktopLibrary);
            }

            NativeLibrary.TryLoad("libGLESv2.so.2", out _glesLibrary);
            Volatile.Write(ref _librariesProbed, true);
        }
    }
}
