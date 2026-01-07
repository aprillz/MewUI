namespace Aprillz.MewUI.Rendering.OpenGL;

internal interface IOpenGLWindowResources : IDisposable
{
    bool SupportsBgra { get; }

    OpenGLTextCache TextCache { get; }

    void MakeCurrent(nint deviceOrDisplay);

    void ReleaseCurrent();

    void SwapBuffers(nint deviceOrDisplay, nint nativeWindow);
}

