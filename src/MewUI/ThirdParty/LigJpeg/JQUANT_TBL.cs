namespace BitMiracle.LibJpeg.Classic;

/// <summary>
/// DCT coefficient quantization tables.
/// </summary>
internal class JQUANT_TBL
{
    /* This field is used only during compression.  It's initialized false when
     * the table is created, and set true when it's been output to the file.
     * You could suppress output of a table by setting this to true.
     * (See jpeg_suppress_tables for an example.)
     */

    /* This array gives the coefficient quantizers in natural array order
     * (not the zigzag order in which they are stored in a JPEG DQT marker).
     * CAUTION: IJG versions prior to v6a kept this array in zigzag order.
     */
    internal readonly short[] quantval = new short[JpegConstants.DCTSIZE2];  /* quantization step for each coefficient */

    internal JQUANT_TBL()
    {
    }

    /// <summary>
    /// Gets or sets a value indicating whether the table has been output to file.
    /// </summary>
    /// <value>It's initialized <c>false</c> when the table is created, and set 
    /// <c>true</c> when it's been output to the file. You could suppress output of a table by setting this to <c>true</c>.
    /// </value>
    /// <remarks>This property is used only during compression.</remarks>
    public bool Sent_table { get; set; }
}
