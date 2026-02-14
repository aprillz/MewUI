namespace Aprillz.MewUI.Rendering.OpenGL;

internal interface IOpenGLWindowResources : IDisposable
{
    bool SupportsBgra { get; }

    bool SupportsNpotTextures { get; }

    OpenGLTextCache TextCache { get; }

    void TrackTexture(uint textureId);

    void MakeCurrent(nint deviceOrDisplay);

    void ReleaseCurrent();

    void SwapBuffers(nint deviceOrDisplay, nint nativeWindow);

    void SetSwapInterval(int interval);
}
