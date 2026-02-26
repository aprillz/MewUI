using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

internal static unsafe partial class OpenGLExt
{
    private const string OpenGLLib = "/System/Library/Frameworks/OpenGL.framework/OpenGL";
    private static nint _openGLLibHandle;

    private static partial void LoadFunctionPointers()
    {
        // macOS OpenGL.framework exports core symbols; for legacy contexts the EXT variants may be present.
        if (!NativeLibrary.TryLoad(OpenGLLib, out _openGLLibHandle))
        {
            return;
        }

        _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)GetExport(_openGLLibHandle, "glGenFramebuffers", "glGenFramebuffersEXT");
        _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)GetExport(_openGLLibHandle, "glDeleteFramebuffers", "glDeleteFramebuffersEXT");
        _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)GetExport(_openGLLibHandle, "glBindFramebuffer", "glBindFramebufferEXT");
        _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)GetExport(_openGLLibHandle, "glFramebufferTexture2D", "glFramebufferTexture2DEXT");
        _glGenRenderbuffers = (delegate* unmanaged<int, uint*, void>)GetExport(_openGLLibHandle, "glGenRenderbuffers", "glGenRenderbuffersEXT");
        _glDeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)GetExport(_openGLLibHandle, "glDeleteRenderbuffers", "glDeleteRenderbuffersEXT");
        _glBindRenderbuffer = (delegate* unmanaged<uint, uint, void>)GetExport(_openGLLibHandle, "glBindRenderbuffer", "glBindRenderbufferEXT");
        _glRenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)GetExport(_openGLLibHandle, "glRenderbufferStorage", "glRenderbufferStorageEXT");
        _glFramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)GetExport(_openGLLibHandle, "glFramebufferRenderbuffer", "glFramebufferRenderbufferEXT");
        _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)GetExport(_openGLLibHandle, "glCheckFramebufferStatus", "glCheckFramebufferStatusEXT");
    }

    private static nint GetExport(nint lib, string primary, string fallback)
    {
        if (NativeLibrary.TryGetExport(lib, primary, out nint p))
        {
            return p;
        }

        if (NativeLibrary.TryGetExport(lib, fallback, out p))
        {
            return p;
        }

        return 0;
    }
}
