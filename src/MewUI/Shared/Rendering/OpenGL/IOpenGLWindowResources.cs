namespace Aprillz.MewUI.Rendering.OpenGL;

internal interface IOpenGLWindowResources : IDisposable
{
    bool SupportsBgra { get; }

    bool SupportsNpotTextures { get; }

    /// <summary>Native GL context handle (GLXContext or EGLContext) for share-group identity.</summary>
    nint NativeContext { get; }

    void TrackTexture(uint textureId);

    void MakeCurrent(nint deviceOrDisplay);

    void ReleaseCurrent();

    void SwapBuffers(nint deviceOrDisplay, nint nativeWindow);

    void SetSwapInterval(int interval);
}
