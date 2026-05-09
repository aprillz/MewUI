using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.OpenGL;

/// <summary>
/// Backend marker interface for pixel sources whose underlying GPU resource is a native
/// OpenGL texture (typically an FBO color attachment). MewVG GL consumers cast to this
/// to enable zero-copy NoDelete-style wrapping when source and consumer share a GL
/// context's share-list.
/// </summary>
/// <remarks>
/// Sources outside the GL backend (D2D bitmap, Metal texture, etc.) do NOT implement
/// this interface; the cross-backend cast naturally fails and falls through to the CPU
/// <see cref="IPixelBufferSource.Lock"/> readback path.
/// </remarks>
public interface IGLTextureSource : IGpuTextureSource
{
    /// <summary>
    /// Native GL texture id. Lifetime is owned by the source — consumers MUST NOT call
    /// <c>glDeleteTextures</c> on this id directly. Returns 0 when the texture hasn't
    /// been initialised yet (e.g. CPU-only consumer never triggered FBO setup).
    /// </summary>
    uint TextureId { get; }

    /// <summary>
    /// Identifier for the GL context that owns this texture, used by consumers to verify
    /// share-list compatibility. Two contexts share textures only if their creation
    /// pulled from the same share root; consumers compare this by reference equality
    /// against their own context's share group.
    /// </summary>
    nint ShareGroup { get; }

    /// <summary>
    /// Configures the texture's wrap mode for the next sample. Required because tile-mode
    /// brushes need <c>GL_REPEAT</c> while filter sampling defaults to
    /// <c>GL_CLAMP_TO_EDGE</c>; without this hook the consumer's NoDelete wrapping
    /// silently inherits the wrong mode.
    /// </summary>
    void ConfigureWrap(bool repeatX, bool repeatY);
}
