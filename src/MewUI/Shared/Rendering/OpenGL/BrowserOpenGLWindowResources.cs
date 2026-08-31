using Aprillz.MewUI.Native;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// GL resources for the single Emscripten WebGL2 context. The browser owns context currency and
/// presentation, so making current and swapping are no-ops here.
/// </summary>
internal sealed class BrowserOpenGLWindowResources : IOpenGLWindowResources
{
    private readonly List<uint> _textures = new();
    private bool _disposed;

    /// <summary>WebGL2 has no GL_BGRA upload path.</summary>
    public bool SupportsBgra => false;

    public bool SupportsNpotTextures => true;

    // One context for the process, so any non-zero value identifies the single share group.
    public nint NativeContext => 1;

    public void MakeCurrent(nint deviceOrDisplay) { }

    public void ReleaseCurrent() { }

    // The browser composites the canvas itself; there is no buffer to swap.
    public void SwapBuffers(nint deviceOrDisplay, nint nativeWindow) { }

    public void SetSwapInterval(int interval) { }

    public void TrackTexture(uint textureId)
    {
        if (textureId == 0 || _disposed)
        {
            return;
        }

        _textures.Add(textureId);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        foreach (var texture in _textures)
        {
            uint id = texture;
            GL.DeleteTextures(1, ref id);
        }

        _textures.Clear();
    }
}
