namespace Aprillz.MewUI.Resources;

/// <summary>
/// Common metadata for a raster-backed source, independent of whether its pixels are
/// exposed through CPU memory, GPU textures, or an external native resource.
/// </summary>
public interface IRasterSource
{
    /// <summary>Gets the raster width in pixels.</summary>
    int PixelWidth { get; }

    /// <summary>Gets the raster height in pixels.</summary>
    int PixelHeight { get; }

    /// <summary>Gets the monotonically increasing version that backends compare to detect changes.</summary>
    int Version { get; }
}
