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
public readonly record struct DecodedBitmap(
    int WidthPx,
    int HeightPx,
    BitmapPixelFormat PixelFormat,
    byte[] Data)
{
    /// <summary>
    /// Gets the stride in bytes per row.
    /// </summary>
    public int StrideBytes => WidthPx * 4;
}
