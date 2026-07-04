namespace Aprillz.MewUI.Resources;

/// <summary>
/// BGRA32 pixel buffer (8 bits per channel, straight alpha, tightly packed).
/// Used as an API boundary container - decoder output and raw pixel input.
/// </summary>
/// <param name="WidthPx">Bitmap width in pixels.</param>
/// <param name="HeightPx">Bitmap height in pixels.</param>
/// <param name="Data">Pixel data buffer in BGRA byte order.</param>
/// <param name="HasAlpha">
/// True when the source carries a meaningful alpha channel (PNG with alpha, ICO,
/// 32-bit BMP, etc.). False for opaque-only formats (JPEG, RGB PNG, sub-32-bit BMP).
/// Lets consumers skip per-pixel alpha scans and pick the opaque blending path.
/// </param>
public readonly record struct Bgra32PixelBuffer(
    int WidthPx,
    int HeightPx,
    byte[] Data,
    bool HasAlpha = true)
{
    public int StrideBytes => WidthPx * 4;
}
