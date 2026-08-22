namespace Aprillz.MewUI.Controls;

/// <summary>
/// Controls how <see cref="Image"/> renders a vector source.
/// </summary>
public enum ImageVectorCacheMode
{
    /// <summary>Rasterizes into a cached bitmap before the frame that needs it is drawn, so no frame shows content older than the source. A size change is the exception: it rasterizes on a background thread while the previous bitmap is stretched into the new region.</summary>
    Cached,

    /// <summary>Rasterizes into a cached bitmap on a background thread. A frame may show the previous bitmap, or nothing at all before the first one is ready.</summary>
    CachedDeferred,

    /// <summary>Draws the vector into the target every frame and holds no bitmap.</summary>
    Direct,
}
