using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI.Rendering.MewVG;

/// <summary>
/// Backend marker interface for pixel sources whose underlying GPU resource is a native
/// Metal texture (<c>MTLTexture*</c>). MewVG Metal consumers cast to this to enable
/// zero-copy NoDelete-style wrapping when source and consumer share a Metal device.
/// </summary>
/// <remarks>
/// Sources outside the Metal backend (D2D bitmap, GL FBO, etc.) do NOT implement this
/// interface; the cross-backend cast naturally fails and falls through to the CPU
/// <see cref="IPixelBufferSource.Lock"/> readback path.
/// </remarks>
public interface IMetalTextureSource : IGpuTextureSource
{
    /// <summary>
    /// Native <c>MTLTexture*</c>. Lifetime is owned by the source — consumers MUST NOT
    /// release this pointer directly without taking a retain via
    /// <see cref="IGpuTextureSource.RetainGpuHandle"/> first. Returns 0 when the texture
    /// hasn't been allocated yet (e.g. CPU-only consumer never triggered GPU init).
    /// </summary>
    nint MtlTexture { get; }

    /// <summary>
    /// Native <c>MTLDevice*</c> the texture was created on. Cross-device texture sampling
    /// is not supported by Metal — consumers compare this by reference equality against
    /// their own device before issuing zero-copy.
    /// </summary>
    nint MtlDevice { get; }
}
