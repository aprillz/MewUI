namespace Aprillz.MewUI.Resources;

/// <summary>
/// Pixel formats supported by <see cref="DecodedBitmap"/>.
/// </summary>
public enum BitmapPixelFormat
{
    /// <summary>32-bit BGRA (8 bits per channel) with straight alpha.</summary>
    Bgra32 = 0,
}

/// <summary>
/// Represents a decoded bitmap in CPU memory.
/// </summary>
/// <param name="WidthPx">Bitmap width in pixels.</param>
/// <param name="HeightPx">Bitmap height in pixels.</param>
/// <param name="PixelFormat">Pixel format of <paramref name="Data"/>.</param>
/// <param name="Data">Pixel data buffer.</param>
/// <param name="HasAlpha">
/// True when the source format carries a meaningful alpha channel (PNG with alpha, ICO,
/// 32-bit BMP, etc.). False for opaque-only formats (JPEG, RGB PNG, sub-32-bit BMP).
/// Backends use this to skip per-pixel alpha scans (the conservative "is everything 0xFF?"
/// premultiply check) and to select <c>D2D1_ALPHA_MODE.IGNORE</c> over <c>PREMULTIPLIED</c>,
/// which lets the GPU skip blend math entirely.
/// Default <c>true</c> preserves the previous straight-alpha-aware behavior for legacy
/// callers that don't set the flag.
/// </param>
public readonly record struct DecodedBitmap(
    int WidthPx,
    int HeightPx,
    BitmapPixelFormat PixelFormat,
    byte[] Data,
    bool HasAlpha = true)
{
    /// <summary>
    /// Gets the stride in bytes per row.
    /// </summary>
    public int StrideBytes => WidthPx * 4;
}
