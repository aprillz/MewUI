namespace Aprillz.MewUI.Resources;

/// <summary>
/// BGRA32 pixel buffer (8 bits per channel, straight alpha, tightly packed) that carries decoder output and
/// raw pixel input across the API boundary.
/// </summary>
/// <param name="WidthPx">Bitmap width in pixels.</param>
/// <param name="HeightPx">Bitmap height in pixels.</param>
/// <param name="Data">Pixel data buffer in BGRA byte order.</param>
/// <param name="HasAlpha">
/// Whether the pixels carry a meaningful alpha channel; false for opaque-only formats (JPEG, RGB PNG,
/// sub-32-bit BMP) lets consumers skip per-pixel alpha scans and take the opaque blending path.
/// </param>
public readonly record struct Bgra32PixelBuffer(
    int WidthPx,
    int HeightPx,
    byte[] Data,
    bool HasAlpha = true)
{
    /// <summary>Gets the stride in bytes per row (<c>WidthPx * 4</c>, tightly packed).</summary>
    public int StrideBytes => WidthPx * 4;
}
