namespace Aprillz.MewUI.Resources;

/// <summary>
/// Marker interface for pixel sources whose pixels live primarily on the GPU. Sits one
/// tier above <see cref="IPixelBufferSource"/> and below the per-backend marker
/// interfaces (<c>ID2DTextureSource</c>, <c>IGLTextureSource</c>, <c>IMetalTextureSource</c>)
/// defined in their respective backend assemblies.
/// </summary>
/// <remarks>
/// <para>
/// Carries only backend-agnostic metadata. Backend identity and the typed native handles
/// (e.g. <c>uint TextureId</c> for GL, <c>nint MtlTexture</c> for Metal) are exposed by
/// the per-backend marker interfaces in their own assemblies, so:
/// </para>
/// <list type="bullet">
///   <item>Core has zero knowledge of any specific GPU backend.</item>
///   <item>A consumer that wants zero-copy interop with a particular backend casts to
///         that backend's marker (<c>source is IGLTextureSource gl</c>); cross-backend
///         sources fall through to the generic <see cref="IPixelBufferSource.Lock"/>
///         path automatically.</item>
///   <item>Adding a new backend (Vulkan, WebGPU) defines its own marker in its own
///         assembly without modifying core.</item>
/// </list>
/// <para>
/// All implementers should also implement <see cref="IPixelBufferSource"/>; CPU-side
/// readback through <c>Lock</c> is the universal fallback.
/// </para>
/// </remarks>
public interface IGpuTextureSource : IPixelBufferSource
{
    /// <summary>
    /// Width of the GPU texture in texels. Usually equal to
    /// <see cref="IPixelBufferSource.PixelWidth"/>; declared separately so future
    /// implementations can expose a sub-region without reshaping the CPU mirror.
    /// </summary>
    int GpuPixelWidth => PixelWidth;

    /// <summary>Height of the GPU texture in texels. See <see cref="GpuPixelWidth"/>.</summary>
    int GpuPixelHeight => PixelHeight;

    /// <summary>
    /// True when texel row 0 corresponds to the bottom of the image (GL FBO convention).
    /// False for top-down storage (D3D / Metal). Consumers that mix sources from different
    /// backends use this to flip the V coordinate at sample time when needed.
    /// </summary>
    bool YFlipped => false;

    /// <summary>
    /// Returns the native GPU texture handle this source owns, or <c>0</c> when the
    /// resource hasn't been allocated yet (e.g. CPU-only consumer never triggered GPU
    /// init). Interpretation depends on the consuming backend: GL texture id for
    /// <see cref="System.IntPtr"/> from a GL source, <c>MTLTexture*</c> from a Metal
    /// source. Backend-typed handles are exposed by per-backend marker interfaces
    /// (<c>IGLTextureSource.TextureId</c>, <c>IMetalTextureSource.MtlTexture</c>); this
    /// method is the backend-agnostic accessor used when the consumer doesn't need to
    /// disambiguate.
    /// </summary>
    /// <remarks>
    /// The handle remains owned by the source — wrapping consumers must use
    /// NoDelete-style flags (e.g. <c>NVGimageFlags.NoDelete</c>) to avoid double-free.
    /// </remarks>
    nint GetTextureHandle() => 0;

    /// <summary>
    /// Adds a reference to the underlying GPU handle so its lifetime extends past this
    /// source's <see cref="System.IDisposable.Dispose"/>. Required for safe NoDelete-style
    /// zero-copy wrapping when the source's lifetime is tighter than the consumer's
    /// (e.g. a scratch surface rented per filter pass while the
    /// consumer's command buffer hasn't yet committed). Must be paired with
    /// <see cref="ReleaseGpuHandle"/>. Returns false when the source doesn't support
    /// retain (default).
    /// </summary>
    bool RetainGpuHandle(nint handle) => false;

    /// <summary>
    /// Releases a reference previously taken via <see cref="RetainGpuHandle"/>. No-op when
    /// the source doesn't support retain. Must be invoked exactly once per successful retain.
    /// </summary>
    void ReleaseGpuHandle(nint handle) { }

    /// <summary>
    /// Configures the underlying GPU texture's wrap mode for tiled / clamped sampling.
    /// Called by zero-copy image consumers (e.g. MewVG image-brush wrapping an FBO color
    /// attachment) before binding the texture for drawing — without this, tile-mode brush
    /// requests silently clamp because the source originally created the texture with
    /// <c>GL_CLAMP_TO_EDGE</c> (right for filter sampling, wrong for tiled fills) and
    /// NVG's <c>NoDelete</c> wrapping doesn't touch external texture state.
    /// </summary>
    /// <remarks>
    /// Default no-op — only sources whose backing GPU texture's wrap mode actually matters
    /// (currently <c>OpenGLPixelRenderSurface</c>) override. Future cleanup may move this
    /// to <c>IGLTextureSource.ConfigureWrap</c> since wrap mode is GL-specific; kept here
    /// for now so backend-agnostic consumers (MewVGImage) can call without GL marker cast.
    /// </remarks>
    void ConfigureGpuTextureWrap(nint handle, bool repeatX, bool repeatY) { }
}
