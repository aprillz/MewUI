namespace Aprillz.MewUI.Native;

internal static unsafe partial class OpenGLExt
{
    private static partial void LoadFunctionPointers()
    {
        _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)OpenGL32.wglGetProcAddress("glGenFramebuffers");
        _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)OpenGL32.wglGetProcAddress("glDeleteFramebuffers");
        _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)OpenGL32.wglGetProcAddress("glBindFramebuffer");
        _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)OpenGL32.wglGetProcAddress("glFramebufferTexture2D");
        _glGenRenderbuffers = (delegate* unmanaged<int, uint*, void>)OpenGL32.wglGetProcAddress("glGenRenderbuffers");
        _glDeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)OpenGL32.wglGetProcAddress("glDeleteRenderbuffers");
        _glBindRenderbuffer = (delegate* unmanaged<uint, uint, void>)OpenGL32.wglGetProcAddress("glBindRenderbuffer");
        _glRenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)OpenGL32.wglGetProcAddress("glRenderbufferStorage");
        _glFramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)OpenGL32.wglGetProcAddress("glFramebufferRenderbuffer");
        _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)OpenGL32.wglGetProcAddress("glCheckFramebufferStatus");

        // Shader / program / VAO / buffer entrypoints (GL 2.0+ / 3.0+).
        _glCreateShader = (delegate* unmanaged<uint, uint>)OpenGL32.wglGetProcAddress("glCreateShader");
        _glDeleteShader = (delegate* unmanaged<uint, void>)OpenGL32.wglGetProcAddress("glDeleteShader");
        _glShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)OpenGL32.wglGetProcAddress("glShaderSource");
        _glCompileShader = (delegate* unmanaged<uint, void>)OpenGL32.wglGetProcAddress("glCompileShader");
        _glGetShaderiv = (delegate* unmanaged<uint, uint, int*, void>)OpenGL32.wglGetProcAddress("glGetShaderiv");
        _glGetShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)OpenGL32.wglGetProcAddress("glGetShaderInfoLog");
        _glCreateProgram = (delegate* unmanaged<uint>)OpenGL32.wglGetProcAddress("glCreateProgram");
        _glDeleteProgram = (delegate* unmanaged<uint, void>)OpenGL32.wglGetProcAddress("glDeleteProgram");
        _glAttachShader = (delegate* unmanaged<uint, uint, void>)OpenGL32.wglGetProcAddress("glAttachShader");
        _glLinkProgram = (delegate* unmanaged<uint, void>)OpenGL32.wglGetProcAddress("glLinkProgram");
        _glGetProgramiv = (delegate* unmanaged<uint, uint, int*, void>)OpenGL32.wglGetProcAddress("glGetProgramiv");
        _glGetProgramInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)OpenGL32.wglGetProcAddress("glGetProgramInfoLog");
        _glUseProgram = (delegate* unmanaged<uint, void>)OpenGL32.wglGetProcAddress("glUseProgram");
        _glGetUniformLocation = (delegate* unmanaged<uint, byte*, int>)OpenGL32.wglGetProcAddress("glGetUniformLocation");
        _glUniform1i = (delegate* unmanaged<int, int, void>)OpenGL32.wglGetProcAddress("glUniform1i");
        _glUniform2f = (delegate* unmanaged<int, float, float, void>)OpenGL32.wglGetProcAddress("glUniform2f");
        _glUniform1fv = (delegate* unmanaged<int, int, float*, void>)OpenGL32.wglGetProcAddress("glUniform1fv");
        _glGenBuffers = (delegate* unmanaged<int, uint*, void>)OpenGL32.wglGetProcAddress("glGenBuffers");
        _glDeleteBuffers = (delegate* unmanaged<int, uint*, void>)OpenGL32.wglGetProcAddress("glDeleteBuffers");
        _glBindBuffer = (delegate* unmanaged<uint, uint, void>)OpenGL32.wglGetProcAddress("glBindBuffer");
        _glBufferData = (delegate* unmanaged<uint, nint, void*, uint, void>)OpenGL32.wglGetProcAddress("glBufferData");
        _glGenVertexArrays = (delegate* unmanaged<int, uint*, void>)OpenGL32.wglGetProcAddress("glGenVertexArrays");
        _glDeleteVertexArrays = (delegate* unmanaged<int, uint*, void>)OpenGL32.wglGetProcAddress("glDeleteVertexArrays");
        _glBindVertexArray = (delegate* unmanaged<uint, void>)OpenGL32.wglGetProcAddress("glBindVertexArray");
        _glVertexAttribPointer = (delegate* unmanaged<uint, int, uint, byte, int, void*, void>)OpenGL32.wglGetProcAddress("glVertexAttribPointer");
        _glEnableVertexAttribArray = (delegate* unmanaged<uint, void>)OpenGL32.wglGetProcAddress("glEnableVertexAttribArray");
        _glActiveTexture = (delegate* unmanaged<uint, void>)OpenGL32.wglGetProcAddress("glActiveTexture");
        _glDrawArrays = (delegate* unmanaged<uint, int, int, void>)OpenGL32.wglGetProcAddress("glDrawArrays");
    }
}
