namespace Aprillz.MewUI.Native;

internal static unsafe partial class OpenGLExt
{
    private static partial void LoadFunctionPointers()
    {
        _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)LibGL.glXGetProcAddress("glGenFramebuffers");
        _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)LibGL.glXGetProcAddress("glDeleteFramebuffers");
        _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)LibGL.glXGetProcAddress("glBindFramebuffer");
        _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)LibGL.glXGetProcAddress("glFramebufferTexture2D");
        _glGenRenderbuffers = (delegate* unmanaged<int, uint*, void>)LibGL.glXGetProcAddress("glGenRenderbuffers");
        _glDeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)LibGL.glXGetProcAddress("glDeleteRenderbuffers");
        _glBindRenderbuffer = (delegate* unmanaged<uint, uint, void>)LibGL.glXGetProcAddress("glBindRenderbuffer");
        _glRenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)LibGL.glXGetProcAddress("glRenderbufferStorage");
        _glFramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)LibGL.glXGetProcAddress("glFramebufferRenderbuffer");
        _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)LibGL.glXGetProcAddress("glCheckFramebufferStatus");

        // Shader / program / VAO / buffer entrypoints (GL 2.0+ / 3.0+) - required by
        // OpenGLGaussianBlur and any other GPU effect pass. Without these, IsShaderPipelineSupported
        // returns false and every blur silently falls back to the CPU executor (slow + visible
        // pipeline divergence vs Win32/Mac).
        _glCreateShader = (delegate* unmanaged<uint, uint>)LibGL.glXGetProcAddress("glCreateShader");
        _glDeleteShader = (delegate* unmanaged<uint, void>)LibGL.glXGetProcAddress("glDeleteShader");
        _glShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)LibGL.glXGetProcAddress("glShaderSource");
        _glCompileShader = (delegate* unmanaged<uint, void>)LibGL.glXGetProcAddress("glCompileShader");
        _glGetShaderiv = (delegate* unmanaged<uint, uint, int*, void>)LibGL.glXGetProcAddress("glGetShaderiv");
        _glGetShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)LibGL.glXGetProcAddress("glGetShaderInfoLog");
        _glCreateProgram = (delegate* unmanaged<uint>)LibGL.glXGetProcAddress("glCreateProgram");
        _glDeleteProgram = (delegate* unmanaged<uint, void>)LibGL.glXGetProcAddress("glDeleteProgram");
        _glAttachShader = (delegate* unmanaged<uint, uint, void>)LibGL.glXGetProcAddress("glAttachShader");
        _glLinkProgram = (delegate* unmanaged<uint, void>)LibGL.glXGetProcAddress("glLinkProgram");
        _glGetProgramiv = (delegate* unmanaged<uint, uint, int*, void>)LibGL.glXGetProcAddress("glGetProgramiv");
        _glGetProgramInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)LibGL.glXGetProcAddress("glGetProgramInfoLog");
        _glUseProgram = (delegate* unmanaged<uint, void>)LibGL.glXGetProcAddress("glUseProgram");
        _glGetUniformLocation = (delegate* unmanaged<uint, byte*, int>)LibGL.glXGetProcAddress("glGetUniformLocation");
        _glUniform1i = (delegate* unmanaged<int, int, void>)LibGL.glXGetProcAddress("glUniform1i");
        _glUniform2f = (delegate* unmanaged<int, float, float, void>)LibGL.glXGetProcAddress("glUniform2f");
        _glUniform1fv = (delegate* unmanaged<int, int, float*, void>)LibGL.glXGetProcAddress("glUniform1fv");
        _glGenBuffers = (delegate* unmanaged<int, uint*, void>)LibGL.glXGetProcAddress("glGenBuffers");
        _glDeleteBuffers = (delegate* unmanaged<int, uint*, void>)LibGL.glXGetProcAddress("glDeleteBuffers");
        _glBindBuffer = (delegate* unmanaged<uint, uint, void>)LibGL.glXGetProcAddress("glBindBuffer");
        _glBufferData = (delegate* unmanaged<uint, nint, void*, uint, void>)LibGL.glXGetProcAddress("glBufferData");
        _glGenVertexArrays = (delegate* unmanaged<int, uint*, void>)LibGL.glXGetProcAddress("glGenVertexArrays");
        _glDeleteVertexArrays = (delegate* unmanaged<int, uint*, void>)LibGL.glXGetProcAddress("glDeleteVertexArrays");
        _glBindVertexArray = (delegate* unmanaged<uint, void>)LibGL.glXGetProcAddress("glBindVertexArray");
        _glVertexAttribPointer = (delegate* unmanaged<uint, int, uint, byte, int, void*, void>)LibGL.glXGetProcAddress("glVertexAttribPointer");
        _glEnableVertexAttribArray = (delegate* unmanaged<uint, void>)LibGL.glXGetProcAddress("glEnableVertexAttribArray");
        _glActiveTexture = (delegate* unmanaged<uint, void>)LibGL.glXGetProcAddress("glActiveTexture");
        _glDrawArrays = (delegate* unmanaged<uint, int, int, void>)LibGL.glXGetProcAddress("glDrawArrays");
    }
}
