namespace Aprillz.MewUI.Native;

internal static unsafe partial class OpenGLExt
{
    private static partial void LoadFunctionPointers()
    {
        _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)EglGLLoader.Resolve("glGenFramebuffers");
        _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)EglGLLoader.Resolve("glDeleteFramebuffers");
        _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)EglGLLoader.Resolve("glBindFramebuffer");
        _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)EglGLLoader.Resolve("glFramebufferTexture2D");
        _glGenRenderbuffers = (delegate* unmanaged<int, uint*, void>)EglGLLoader.Resolve("glGenRenderbuffers");
        _glDeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)EglGLLoader.Resolve("glDeleteRenderbuffers");
        _glBindRenderbuffer = (delegate* unmanaged<uint, uint, void>)EglGLLoader.Resolve("glBindRenderbuffer");
        _glRenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)EglGLLoader.Resolve("glRenderbufferStorage");
        _glFramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)EglGLLoader.Resolve("glFramebufferRenderbuffer");
        _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)EglGLLoader.Resolve("glCheckFramebufferStatus");
        _glBlitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void>)EglGLLoader.Resolve("glBlitFramebuffer");

        // Shader / program / VAO / buffer entrypoints (GL 2.0+ / 3.0+) - required by
        // OpenGLGaussianBlur and any other GPU effect pass. Without these, IsShaderPipelineSupported
        // returns false and every blur silently falls back to the CPU executor (slow + visible
        // pipeline divergence vs Win32/Mac).
        _glCreateShader = (delegate* unmanaged<uint, uint>)EglGLLoader.Resolve("glCreateShader");
        _glDeleteShader = (delegate* unmanaged<uint, void>)EglGLLoader.Resolve("glDeleteShader");
        _glShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)EglGLLoader.Resolve("glShaderSource");
        _glCompileShader = (delegate* unmanaged<uint, void>)EglGLLoader.Resolve("glCompileShader");
        _glGetShaderiv = (delegate* unmanaged<uint, uint, int*, void>)EglGLLoader.Resolve("glGetShaderiv");
        _glGetShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)EglGLLoader.Resolve("glGetShaderInfoLog");
        _glCreateProgram = (delegate* unmanaged<uint>)EglGLLoader.Resolve("glCreateProgram");
        _glDeleteProgram = (delegate* unmanaged<uint, void>)EglGLLoader.Resolve("glDeleteProgram");
        _glAttachShader = (delegate* unmanaged<uint, uint, void>)EglGLLoader.Resolve("glAttachShader");
        _glLinkProgram = (delegate* unmanaged<uint, void>)EglGLLoader.Resolve("glLinkProgram");
        _glGetProgramiv = (delegate* unmanaged<uint, uint, int*, void>)EglGLLoader.Resolve("glGetProgramiv");
        _glGetProgramInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)EglGLLoader.Resolve("glGetProgramInfoLog");
        _glUseProgram = (delegate* unmanaged<uint, void>)EglGLLoader.Resolve("glUseProgram");
        _glGetUniformLocation = (delegate* unmanaged<uint, byte*, int>)EglGLLoader.Resolve("glGetUniformLocation");
        _glUniform1i = (delegate* unmanaged<int, int, void>)EglGLLoader.Resolve("glUniform1i");
        _glUniform2f = (delegate* unmanaged<int, float, float, void>)EglGLLoader.Resolve("glUniform2f");
        _glUniform1fv = (delegate* unmanaged<int, int, float*, void>)EglGLLoader.Resolve("glUniform1fv");
        _glGenBuffers = (delegate* unmanaged<int, uint*, void>)EglGLLoader.Resolve("glGenBuffers");
        _glDeleteBuffers = (delegate* unmanaged<int, uint*, void>)EglGLLoader.Resolve("glDeleteBuffers");
        _glBindBuffer = (delegate* unmanaged<uint, uint, void>)EglGLLoader.Resolve("glBindBuffer");
        _glBufferData = (delegate* unmanaged<uint, nint, void*, uint, void>)EglGLLoader.Resolve("glBufferData");
        _glGenVertexArrays = (delegate* unmanaged<int, uint*, void>)EglGLLoader.Resolve("glGenVertexArrays");
        _glDeleteVertexArrays = (delegate* unmanaged<int, uint*, void>)EglGLLoader.Resolve("glDeleteVertexArrays");
        _glBindVertexArray = (delegate* unmanaged<uint, void>)EglGLLoader.Resolve("glBindVertexArray");
        _glVertexAttribPointer = (delegate* unmanaged<uint, int, uint, byte, int, void*, void>)EglGLLoader.Resolve("glVertexAttribPointer");
        _glEnableVertexAttribArray = (delegate* unmanaged<uint, void>)EglGLLoader.Resolve("glEnableVertexAttribArray");
        _glActiveTexture = (delegate* unmanaged<uint, void>)EglGLLoader.Resolve("glActiveTexture");
        _glDrawArrays = (delegate* unmanaged<uint, int, int, void>)EglGLLoader.Resolve("glDrawArrays");
    }
}
