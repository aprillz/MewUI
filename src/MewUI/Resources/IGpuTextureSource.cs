namespace Aprillz.MewUI.Resources;

/// <summary>
/// Marker interface for raster sources whose pixels live primarily on the GPU. Sits one
/// tier above <see cref="IRasterSource"/> and below per-backend marker interfaces defined
/// in their respective backend assemblies.
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
    /// Width of the GPU texture in texels. Usually equal to
    /// <see cref="IRasterSource.PixelWidth"/>; declared separately so future
    /// implementations can expose a sub-region without reshaping the CPU mirror.
    /// </summary>
    int GpuPixelWidth => PixelWidth;

    /// <summary>Height of the GPU texture in texels. See <see cref="GpuPixelWidth"/>.</summary>
    int GpuPixelHeight => PixelHeight;

    /// <summary>
    /// True when texel row 0 corresponds to the bottom of the image (bottom-up storage).
    /// False for top-down storage. Consumers that mix sources from different backends use
    /// this to flip the V coordinate at sample time when needed.
    /// </summary>
    bool YFlipped => false;

    /// <summary>
    /// Returns the native GPU texture handle this source owns, or <c>0</c> when the
    /// resource hasn't been allocated yet (e.g. CPU-only consumer never triggered GPU
    /// init). Interpretation depends on the consuming backend; backend-typed handles are
    /// exposed by per-backend marker interfaces. This method is the backend-agnostic
    /// accessor used when the consumer doesn't need to disambiguate.
    /// </summary>
    /// <remarks>
    /// The handle remains owned by the source — wrapping consumers must use no-delete-style
    /// flags to avoid double-free.
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
    /// Called by zero-copy image consumers before binding the texture for drawing — without
    /// this, tile-mode brush requests silently clamp because the source originally created
    /// the texture with edge-clamp (right for filter sampling, wrong for tiled fills) and
    /// no-delete-style wrapping doesn't touch external texture state.
    /// </summary>
    /// <remarks>
    /// Default no-op — only sources whose backing GPU texture's wrap mode actually matters
    /// at the consumer's tile-mode draw site override.
    /// </remarks>
    void ConfigureGpuTextureWrap(nint handle, bool repeatX, bool repeatY) { }
}
