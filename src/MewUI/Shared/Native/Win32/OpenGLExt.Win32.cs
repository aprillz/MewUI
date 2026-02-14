namespace Aprillz.MewUI.Native;

internal static unsafe partial class OpenGLExt
{
    private static void LoadFunctionPointersWin32()
    {
        _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)OpenGL32.wglGetProcAddress("glGenFramebuffers");
        _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)OpenGL32.wglGetProcAddress("glDeleteFramebuffers");
        _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)OpenGL32.wglGetProcAddress("glBindFramebuffer");
        _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)OpenGL32.wglGetProcAddress("glFramebufferTexture2D");
        _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)OpenGL32.wglGetProcAddress("glCheckFramebufferStatus");
    }
}

