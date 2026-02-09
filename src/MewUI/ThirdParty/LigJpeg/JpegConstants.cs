namespace BitMiracle.LibJpeg.Classic;

/// <summary>
/// Defines some JPEG constants.
/// </summary>
internal static class JpegConstants
{
    //////////////////////////////////////////////////////////////////////////
    // All of these are specified by the JPEG standard, so don't change them
    // if you want to be compatible.
    //

    /// <summary>
    /// The basic DCT block is 8x8 coefficients
    /// </summary>
    public const int DCTSIZE = 8;

    /// <summary>
    /// DCTSIZE squared; the number of elements in a block. 
    /// </summary>
    public const int DCTSIZE2 = DCTSIZE * DCTSIZE;

    /// <summary>
    /// Quantization tables are numbered 0..3 
    /// </summary>
    public const int NUM_QUANT_TBLS = 4;

    /// <summary>
    /// Huffman tables are numbered 0..3
    /// </summary>
    public const int NUM_HUFF_TBLS = 4;

    /// <summary>
    /// Arith-coding tables are numbered 0..15
    /// </summary>
    public const int NUM_ARITH_TBLS = 16;

    /// <summary>
    /// JPEG limit on the number of components in one scan.
    /// </summary>
    public const int MAX_COMPS_IN_SCAN = 4;

    // compressor's limit on blocks per MCU
    //
    // Unfortunately, some bozo at Adobe saw no reason to be bound by the standard;
    // the PostScript DCT filter can emit files with many more than 10 blocks/MCU.
    // If you happen to run across such a file, you can up D_MAX_BLOCKS_IN_MCU
    // to handle it.  We even let you do this from the jconfig.h file. However,
    // we strongly discourage changing C_MAX_BLOCKS_IN_MCU; just because Adobe
    // sometimes emits noncompliant files doesn't mean you should too.

    /// <summary>
    /// Compressor's limit on blocks per MCU.
    /// </summary>
    public const int C_MAX_BLOCKS_IN_MCU = 10;

    /// <summary>
    /// Decompressor's limit on blocks per MCU.
    /// </summary>
    public const int D_MAX_BLOCKS_IN_MCU = 10;

    /// <summary>
    /// JPEG limit on sampling factors.
    /// </summary>
    public const int MAX_SAMP_FACTOR = 4;


    //////////////////////////////////////////////////////////////////////////
    // implementation-specific constants
    //

    // Maximum number of components (color channels) allowed in JPEG image.
    // To meet the letter of the JPEG spec, set this to 255.  However, darn
    // few applications need more than 4 channels (maybe 5 for CMYK + alpha
    // mask).  We recommend 10 as a reasonable compromise; use 4 if you are
    // really short on memory.  (Each allowed component costs a hundred or so
    // bytes of storage, whether actually used in an image or not.)

    /// <summary>
    /// Maximum number of color channels allowed in JPEG image.
    /// </summary>
    public const int MAX_COMPONENTS = 10;



    /// <summary>
    /// The size of sample.
    /// </summary>
    /// <remarks>Are either:
    /// 8 - for 8-bit sample values (the usual setting)<br/>
    /// 9 - for 9-bit sample values
    /// 10 - for 10-bit sample values
    /// 11 - for 11-bit sample values
    /// 12 - for 12-bit sample values (not supported by this version)<br/>
    /// Only 8, 9, 10, 11, and 12 bits sample data precision are supported for
    /// full-feature DCT processing.Further depths up to 16-bit may be added
    /// later for the lossless modes of operation.
    /// Run-time selection and conversion of data precision will be added later
    /// and are currently not supported, sorry.
    /// Exception:  The transcoding part(jpegtran) supports all settings in a
    /// single instance, since it operates on the level of DCT coefficients and
    /// not sample values.The DCT coefficients are of the same type(16 bits)
    /// in all cases(see below).
    /// </remarks>
    public const int BITS_IN_JSAMPLE = 8;

    /// <summary>
    /// DCT method used by default.
    /// </summary>
    public const J_DCT_METHOD JDCT_DEFAULT = J_DCT_METHOD.JDCT_ISLOW;

    /// <summary>
    /// Fastest DCT method.
    /// </summary>
    public const J_DCT_METHOD JDCT_FASTEST = J_DCT_METHOD.JDCT_IFAST;

    /// <summary>
    /// A tad under 64K to prevent overflows. 
    /// </summary>
    public const int JPEG_MAX_DIMENSION = 65500;

    /// <summary>
    /// The maximum sample value.
    /// </summary>
    public const int MAXJSAMPLE = 255;

    /// <summary>
    /// The medium sample value.
    /// </summary>
    public const int CENTERJSAMPLE = 128;

    // Ordering of RGB data in scanlines passed to or from the application.
    // RESTRICTIONS:
    // 1. The sample applications cjpeg,djpeg do NOT support modified RGB formats.
    // 2. The color quantizer modules will not behave desirably if RGB_PIXELSIZE
    // is not 3 (they don't understand about dummy color components!).  So you
    // can't use color quantization if you change that value.

    /// <summary>
    /// Offset of Red in an RGB scanline element. 
    /// </summary>
    public const int RGB_RED = 0;

    /// <summary>
    /// Offset of Green in an RGB scanline element. 
    /// </summary>
    public const int RGB_GREEN = 1;

    /// <summary>
    /// Offset of Blue in an RGB scanline element. 
    /// </summary>
    public const int RGB_BLUE = 2;

    /// <summary>
    /// Bytes per RGB scanline element.
    /// </summary>
    public const int RGB_PIXELSIZE = 3;

    /// <summary>
    /// The number of bits of lookahead.
    /// </summary>
    public const int HUFF_LOOKAHEAD = 8;
}
