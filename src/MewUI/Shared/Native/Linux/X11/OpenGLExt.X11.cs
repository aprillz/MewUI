namespace Aprillz.MewUI.Native;

internal static unsafe partial class OpenGLExt
{
    private static void LoadFunctionPointersX11()
    {
        _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)LibGL.glXGetProcAddress("glGenFramebuffers");
        _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)LibGL.glXGetProcAddress("glDeleteFramebuffers");
        _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)LibGL.glXGetProcAddress("glBindFramebuffer");
        _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)LibGL.glXGetProcAddress("glFramebufferTexture2D");
        _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)LibGL.glXGetProcAddress("glCheckFramebufferStatus");
    }
}

