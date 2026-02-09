namespace BitMiracle.LibJpeg.Classic;

/// <summary>
/// Dithering options for decompression.
/// </summary>
/// <seealso cref="jpeg_decompress_struct.Dither_mode"/>
internal enum J_DITHER_MODE
{
    /// <summary>
    /// No dithering: fast, very low quality
    /// </summary>
    JDITHER_NONE,

    /// <summary>
    /// Ordered dither: moderate speed and quality
    /// </summary>
    JDITHER_ORDERED,

    /// <summary>
    /// Floyd-Steinberg dither: slow, high quality
    /// </summary>
    JDITHER_FS
}
