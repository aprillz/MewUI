using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

internal static unsafe partial class OpenGLExt
{
    // opengl32.dll, loaded the first time the driver lookup misses.
    private static nint _openglLibrary;

    private static partial void LoadFunctionPointers()
    {
        _glGenFramebuffers = (delegate* unmanaged<int, uint*, void>)Resolve("glGenFramebuffers");
        _glDeleteFramebuffers = (delegate* unmanaged<int, uint*, void>)Resolve("glDeleteFramebuffers");
        _glBindFramebuffer = (delegate* unmanaged<uint, uint, void>)Resolve("glBindFramebuffer");
        _glFramebufferTexture2D = (delegate* unmanaged<uint, uint, uint, uint, int, void>)Resolve("glFramebufferTexture2D");
        _glGenRenderbuffers = (delegate* unmanaged<int, uint*, void>)Resolve("glGenRenderbuffers");
        _glDeleteRenderbuffers = (delegate* unmanaged<int, uint*, void>)Resolve("glDeleteRenderbuffers");
        _glBindRenderbuffer = (delegate* unmanaged<uint, uint, void>)Resolve("glBindRenderbuffer");
        _glRenderbufferStorage = (delegate* unmanaged<uint, uint, int, int, void>)Resolve("glRenderbufferStorage");
        _glFramebufferRenderbuffer = (delegate* unmanaged<uint, uint, uint, uint, void>)Resolve("glFramebufferRenderbuffer");
        _glCheckFramebufferStatus = (delegate* unmanaged<uint, uint>)Resolve("glCheckFramebufferStatus");
        _glBlitFramebuffer = (delegate* unmanaged<int, int, int, int, int, int, int, int, uint, uint, void>)Resolve("glBlitFramebuffer");

        // Shader / program / VAO / buffer entrypoints (GL 2.0+ / 3.0+).
        _glCreateShader = (delegate* unmanaged<uint, uint>)Resolve("glCreateShader");
        _glDeleteShader = (delegate* unmanaged<uint, void>)Resolve("glDeleteShader");
        _glShaderSource = (delegate* unmanaged<uint, int, byte**, int*, void>)Resolve("glShaderSource");
        _glCompileShader = (delegate* unmanaged<uint, void>)Resolve("glCompileShader");
        _glGetShaderiv = (delegate* unmanaged<uint, uint, int*, void>)Resolve("glGetShaderiv");
        _glGetShaderInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)Resolve("glGetShaderInfoLog");
        _glCreateProgram = (delegate* unmanaged<uint>)Resolve("glCreateProgram");
        _glDeleteProgram = (delegate* unmanaged<uint, void>)Resolve("glDeleteProgram");
        _glAttachShader = (delegate* unmanaged<uint, uint, void>)Resolve("glAttachShader");
        _glLinkProgram = (delegate* unmanaged<uint, void>)Resolve("glLinkProgram");
        _glGetProgramiv = (delegate* unmanaged<uint, uint, int*, void>)Resolve("glGetProgramiv");
        _glGetProgramInfoLog = (delegate* unmanaged<uint, int, int*, byte*, void>)Resolve("glGetProgramInfoLog");
        _glUseProgram = (delegate* unmanaged<uint, void>)Resolve("glUseProgram");
        _glGetUniformLocation = (delegate* unmanaged<uint, byte*, int>)Resolve("glGetUniformLocation");
        _glUniform1i = (delegate* unmanaged<int, int, void>)Resolve("glUniform1i");
        _glUniform2f = (delegate* unmanaged<int, float, float, void>)Resolve("glUniform2f");
        _glUniform1fv = (delegate* unmanaged<int, int, float*, void>)Resolve("glUniform1fv");
        _glGenBuffers = (delegate* unmanaged<int, uint*, void>)Resolve("glGenBuffers");
        _glDeleteBuffers = (delegate* unmanaged<int, uint*, void>)Resolve("glDeleteBuffers");
        _glBindBuffer = (delegate* unmanaged<uint, uint, void>)Resolve("glBindBuffer");
        _glBufferData = (delegate* unmanaged<uint, nint, void*, uint, void>)Resolve("glBufferData");
        _glGenVertexArrays = (delegate* unmanaged<int, uint*, void>)Resolve("glGenVertexArrays");
        _glDeleteVertexArrays = (delegate* unmanaged<int, uint*, void>)Resolve("glDeleteVertexArrays");
        _glBindVertexArray = (delegate* unmanaged<uint, void>)Resolve("glBindVertexArray");
        _glVertexAttribPointer = (delegate* unmanaged<uint, int, uint, byte, int, void*, void>)Resolve("glVertexAttribPointer");
        _glEnableVertexAttribArray = (delegate* unmanaged<uint, void>)Resolve("glEnableVertexAttribArray");
        _glActiveTexture = (delegate* unmanaged<uint, void>)Resolve("glActiveTexture");
        _glDrawArrays = (delegate* unmanaged<uint, int, int, void>)Resolve("glDrawArrays");
    }

    /// <summary>Finds an entry point through the driver, then among those opengl32.dll exports itself.</summary>
    private static nint Resolve(string name)
    {
        nint proc = OpenGL32.wglGetProcAddress(name);
        // A miss comes back as one of a few small values or -1, not always as zero.
        if (proc != 0 && proc != 1 && proc != 2 && proc != 3 && proc != -1)
        {
            return proc;
        }

        // Some drivers leave what opengl32.dll exports itself (glDrawArrays here) out of their own lookup.
        if (_openglLibrary == 0 && !NativeLibrary.TryLoad("opengl32.dll", out _openglLibrary))
        {
            return 0;
        }

        return NativeLibrary.TryGetExport(_openglLibrary, name, out proc) ? proc : 0;
    }
}
