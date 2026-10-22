namespace Aprillz.MewUI.Resources;

/// <summary>
/// Marker interface for raster sources whose pixels live primarily on the GPU, one tier above
/// <see cref="IRasterSource"/> and below the per-backend marker interfaces defined in backend assemblies.
/// </summary>
/// <remarks>
/// <para>
/// Carries only backend-agnostic metadata. Backend identity and typed native handles are
/// exposed by per-backend marker interfaces in their own assemblies, so:
/// </para>
/// <list type="bullet">
///   <item>Core has zero knowledge of any specific GPU backend.</item>
///   <item>A consumer that wants zero-copy interop with a particular backend casts to
///         that backend's marker; cross-backend sources can choose a CPU readback path
///         separately when they also implement <see cref="IPixelBufferSource"/>.</item>
///   <item>Adding a new backend defines its own marker in its own assembly without
///         modifying core.</item>
/// </list>
/// <para>
/// CPU-side readback is intentionally not part of this contract. Implement
/// <see cref="IPixelBufferSource"/> separately when CPU access is supported.
/// </para>
/// </remarks>
public interface IGpuTextureSource : IRasterSource
{
    /// <summary>
    /// Gets the GPU texture width in texels, usually equal to <see cref="IRasterSource.PixelWidth"/>.
    /// </summary>
    /// <remarks>
    /// Declared separately so an implementation can expose a sub-region without reshaping the CPU mirror.
    /// </remarks>
    int GpuPixelWidth => PixelWidth;

    /// <summary>Gets the GPU texture height in texels, usually equal to <see cref="IRasterSource.PixelHeight"/>.</summary>
    int GpuPixelHeight => PixelHeight;

    /// <summary>
    /// Gets whether texel row 0 is the bottom of the image (bottom-up storage); false for top-down storage.
    /// </summary>
    /// <remarks>
    /// Consumers that mix sources from different backends use this to flip the V coordinate at sample time.
    /// </remarks>
    bool YFlipped => false;

    /// <summary>
    /// Returns the native GPU texture handle this source owns, or 0 when the resource has not been allocated yet.
    /// </summary>
    /// <remarks>
    /// Interpretation depends on the consuming backend; backend-typed handles are exposed by per-backend marker
    /// interfaces, and this method is the backend-agnostic accessor for consumers that do not need to
    /// disambiguate. The handle remains owned by the source, so wrapping consumers must wrap it as borrowed
    /// (non-owning) to avoid a double free.
    /// </remarks>
    nint GetTextureHandle() => 0;

    /// <summary>
    /// Adds a reference to the underlying GPU handle so its lifetime extends past this source's disposal;
    /// returns false when the source does not support retaining (the default).
    /// </summary>
    /// <remarks>
    /// Required for safe borrowed zero-copy wrapping when the source's lifetime is tighter than the consumer's
    /// (for example a scratch surface rented per filter pass while the consumer's command buffer has not yet
    /// committed). Must be paired with <see cref="ReleaseGpuHandle"/>.
    /// </remarks>
    bool RetainGpuHandle(nint handle) => false;

    /// <summary>
    /// Releases a reference taken via <see cref="RetainGpuHandle"/>, exactly once per successful retain; a
    /// no-op when the source does not support retaining.
    /// </summary>
    void ReleaseGpuHandle(nint handle) { }

    /// <summary>
    /// Configures the wrap mode of the underlying GPU texture for tiled or clamped sampling; a no-op by default.
    /// </summary>
    /// <remarks>
    /// Zero-copy image consumers call this before binding the texture for drawing; without it, tile-mode brush
    /// requests silently clamp because the source created the texture with edge clamping (right for filter
    /// sampling, wrong for tiled fills) and borrowed wrapping does not touch external texture state. Only
    /// sources whose texture wrap mode matters at the consumer's tile-mode draw site override it.
    /// </remarks>
    void ConfigureGpuTextureWrap(nint handle, bool repeatX, bool repeatY) { }
}
