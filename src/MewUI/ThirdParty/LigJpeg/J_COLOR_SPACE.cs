namespace BitMiracle.LibJpeg.Classic;

/// <summary>
/// Known color spaces.
/// </summary>
/// <seealso href="c90654b9-f3f4-4319-80d1-979c73d84e76.htm" target="_self">Special color spaces</seealso>
internal enum J_COLOR_SPACE
{
    /// <summary>
    /// Unspecified color space.
    /// </summary>
    JCS_UNKNOWN,

    /// <summary>
    /// Monochrome
    /// </summary>
    JCS_GRAYSCALE,

    /// <summary>
    /// Red/Green/Blue, standard RGB (sRGB)
    /// </summary>
    JCS_RGB,

    /// <summary>
    /// Y/Cb/Cr (also known as YUV), standard YCC
    /// </summary>
    JCS_YCbCr,

    /// <summary>
    /// C/M/Y/K
    /// </summary>
    JCS_CMYK,

    /// <summary>
    ///  Y/Cb/Cr/K
    /// </summary>
    JCS_YCCK,

    /// <summary>
    /// big gamut red/green/blue, bg-sRGB
    /// </summary>
    JCS_BG_RGB,

    /// <summary>
    /// big gamut Y/Cb/Cr, bg-sYCC
    /// </summary>
    JCS_BG_YCC,

    /// <summary>
    /// N channels
    /// </summary>
    JCS_NCHANNEL,
}
