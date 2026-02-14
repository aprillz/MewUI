namespace Aprillz.MewUI.Rendering.Gdi.Core;

/// <summary>
/// Constants used throughout the GDI rendering system.
/// </summary>
internal static class GdiRenderingConstants
{
    #region Buffer Sizes

    /// <summary>
    /// Maximum width/height for anti-aliased surfaces to prevent excessive memory usage.
    /// </summary>
    public const int MaxAaSurfaceSize = 4096;

    /// <summary>
    /// Threshold for using stackalloc vs ArrayPool for alpha row buffers.
    /// </summary>
    public const int StackAllocAlphaRowThreshold = 2048;

    /// <summary>
    /// Small buffer threshold for direct pooling (kept in small buffer cache).
    /// </summary>
    public const int SmallBufferThreshold = 256;

    /// <summary>
    /// Maximum number of surfaces to keep in the large buffer pool.
    /// </summary>
    public const int MaxLargeBufferPoolSize = 8;

    /// <summary>
    /// Maximum number of surfaces to keep in the small buffer cache.
    /// </summary>
    public const int MaxSmallBufferCacheSize = 4;

    #endregion

    #region Resource Cache

    /// <summary>
    /// Maximum number of cached pens in the resource cache.
    /// </summary>
    public const int MaxCachedPens = 64;

    /// <summary>
    /// Maximum number of cached brushes in the resource cache.
    /// </summary>
    public const int MaxCachedBrushes = 64;

    #endregion

    #region Anti-Aliasing

    /// <summary>
    /// Default anti-aliasing width in pixels for SDF-based rendering.
    /// </summary>
    public const float DefaultAaWidth = 1.0f;

    /// <summary>
    /// Epsilon for floating-point comparisons.
    /// </summary>
    public const double Epsilon = 0.0001;

    /// <summary>
    /// Threshold for determining if a line is axis-aligned.
    /// </summary>
    public const double AxisAlignedThreshold = 0.0001;

    /// <summary>
    /// Maximum thickness (in device pixels) to use Wu line AA instead of SSAA.
    /// </summary>
    public const double WuLineMaxThickness = 1.25;

    #endregion

    #region Supersample Factors

    /// <summary>
    /// Supersample factor for GdiCurveQuality.Good (2x2 = 4 samples per pixel).
    /// </summary>
    public const int SupersampleFactor2x = 2;

    /// <summary>
    /// Supersample factor for GdiCurveQuality.Best (3x3 = 9 samples per pixel).
    /// </summary>
    public const int SupersampleFactor3x = 3;

    #endregion

    #region SDF Thresholds

    /// <summary>
    /// SDF distance threshold for fully inside a shape (negative = inside).
    /// </summary>
    public const float SdfInsideThreshold = -0.5f;

    /// <summary>
    /// SDF distance threshold for fully outside a shape (positive = outside).
    /// </summary>
    public const float SdfOutsideThreshold = 0.5f;

    /// <summary>
    /// Threshold for switching from SDF to SSAA at complex corners/edges.
    /// </summary>
    public const float SdfCornerComplexityThreshold = 0.3f;

    #endregion
}
