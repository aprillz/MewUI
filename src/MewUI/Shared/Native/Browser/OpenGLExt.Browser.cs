namespace Aprillz.MewUI.Native;

internal static unsafe partial class OpenGLExt
{
    private static partial void LoadFunctionPointers()
    {
        _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)BrowserGL.GetProcAddress("glGenFramebuffers");
        _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)BrowserGL.GetProcAddress("glDeleteFramebuffers");
        _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)BrowserGL.GetProcAddress("glBindFramebuffer");
        _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)BrowserGL.GetProcAddress("glFramebufferTexture2D");
        _glGenRenderbuffers = (delegate* unmanaged<int, uint*, void>)BrowserGL.GetProcAddress("glGenRenderbuffers");
        _glDeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)BrowserGL.GetProcAddress("glDeleteRenderbuffers");
        _glBindRenderbuffer = (delegate* unmanaged<uint, uint, void>)BrowserGL.GetProcAddress("glBindRenderbuffer");
        _glRenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)BrowserGL.GetProcAddress("glRenderbufferStorage");
        _glFramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)BrowserGL.GetProcAddress("glFramebufferRenderbuffer");
        _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)BrowserGL.GetProcAddress("glCheckFramebufferStatus");

        // Shader / program / VAO / buffer entrypoints (GL 2.0+ / 3.0+) - required by
        // OpenGLGaussianBlur and any other GPU effect pass. Without these, IsShaderPipelineSupported
        // returns false and every blur silently falls back to the CPU executor (slow + visible
        // pipeline divergence vs Win32/Mac).
        _glCreateShader = (delegate* unmanaged<uint, uint>)BrowserGL.GetProcAddress("glCreateShader");
        _glDeleteShader = (delegate* unmanaged<uint, void>)BrowserGL.GetProcAddress("glDeleteShader");
        _glShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)BrowserGL.GetProcAddress("glShaderSource");
        _glCompileShader = (delegate* unmanaged<uint, void>)BrowserGL.GetProcAddress("glCompileShader");
        _glGetShaderiv = (delegate* unmanaged<uint, uint, int*, void>)BrowserGL.GetProcAddress("glGetShaderiv");
        _glGetShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)BrowserGL.GetProcAddress("glGetShaderInfoLog");
        _glCreateProgram = (delegate* unmanaged<uint>)BrowserGL.GetProcAddress("glCreateProgram");
        _glDeleteProgram = (delegate* unmanaged<uint, void>)BrowserGL.GetProcAddress("glDeleteProgram");
        _glAttachShader = (delegate* unmanaged<uint, uint, void>)BrowserGL.GetProcAddress("glAttachShader");
        _glLinkProgram = (delegate* unmanaged<uint, void>)BrowserGL.GetProcAddress("glLinkProgram");
        _glGetProgramiv = (delegate* unmanaged<uint, uint, int*, void>)BrowserGL.GetProcAddress("glGetProgramiv");
        _glGetProgramInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)BrowserGL.GetProcAddress("glGetProgramInfoLog");
        _glUseProgram = (delegate* unmanaged<uint, void>)BrowserGL.GetProcAddress("glUseProgram");
        _glGetUniformLocation = (delegate* unmanaged<uint, byte*, int>)BrowserGL.GetProcAddress("glGetUniformLocation");
        _glUniform1i = (delegate* unmanaged<int, int, void>)BrowserGL.GetProcAddress("glUniform1i");
        _glUniform2f = (delegate* unmanaged<int, float, float, void>)BrowserGL.GetProcAddress("glUniform2f");
        _glUniform1fv = (delegate* unmanaged<int, int, float*, void>)BrowserGL.GetProcAddress("glUniform1fv");
        _glGenBuffers = (delegate* unmanaged<int, uint*, void>)BrowserGL.GetProcAddress("glGenBuffers");
        _glDeleteBuffers = (delegate* unmanaged<int, uint*, void>)BrowserGL.GetProcAddress("glDeleteBuffers");
        _glBindBuffer = (delegate* unmanaged<uint, uint, void>)BrowserGL.GetProcAddress("glBindBuffer");
        _glBufferData = (delegate* unmanaged<uint, nint, void*, uint, void>)BrowserGL.GetProcAddress("glBufferData");
        _glGenVertexArrays = (delegate* unmanaged<int, uint*, void>)BrowserGL.GetProcAddress("glGenVertexArrays");
        _glDeleteVertexArrays = (delegate* unmanaged<int, uint*, void>)BrowserGL.GetProcAddress("glDeleteVertexArrays");
        _glBindVertexArray = (delegate* unmanaged<uint, void>)BrowserGL.GetProcAddress("glBindVertexArray");
        _glVertexAttribPointer = (delegate* unmanaged<uint, int, uint, byte, int, void*, void>)BrowserGL.GetProcAddress("glVertexAttribPointer");
        _glEnableVertexAttribArray = (delegate* unmanaged<uint, void>)BrowserGL.GetProcAddress("glEnableVertexAttribArray");
        _glActiveTexture = (delegate* unmanaged<uint, void>)BrowserGL.GetProcAddress("glActiveTexture");
        _glDrawArrays = (delegate* unmanaged<uint, int, int, void>)BrowserGL.GetProcAddress("glDrawArrays");
    }
}
