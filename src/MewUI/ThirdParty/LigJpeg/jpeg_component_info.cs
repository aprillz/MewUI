namespace BitMiracle.LibJpeg.Classic;

/// <summary>
/// Basic info about one component (color channel).
/// </summary>
internal class jpeg_component_info
{
    /* These values are fixed over the whole image. */
    /* For compression, they must be supplied by parameter setup; */
    /* for decompression, they are read from the SOF marker. */


    /* These values may vary between scans. */
    /* For compression, they must be supplied by parameter setup; */
    /* for decompression, they are read from the SOS marker. */
    /* The decompressor output side may not use these variables. */

    /* Remaining fields should be treated as private by applications. */

    /* These values are computed during compression or decompression startup: */
    /* Component's size in DCT blocks.
     * Any dummy blocks added to complete an MCU are not counted; therefore
     * these values do not depend on whether a scan is interleaved or not.
     */
    internal int height_in_blocks;

    /* Size of a DCT block in samples,
     * reflecting any scaling we choose to apply during the DCT step.
     * Values from 1 to 16 are supported.
     * Note that different components may receive different DCT scalings.
     */
    internal int DCT_h_scaled_size;
    internal int DCT_v_scaled_size;

    /* The downsampled dimensions are the component's actual, unpadded number
     * of samples at the main buffer (preprocessing/compression interface);
     * DCT scaling is included, so
     * downsampled_width =
     *   ceil(image_width * Hi/Hmax * DCT_h_scaled_size/block_size)
     * and similarly for height.
     */
    internal int downsampled_width;    /* actual width in samples */
    internal int downsampled_height; /* actual height in samples */

    /* For decompression, in cases where some of the components will be
     * ignored (eg grayscale output from YCbCr image), we can skip most
     * computations for the unused components.
     * For compression, some of the components will need further quantization
     * scale by factor of 2 after DCT (eg BG_YCC output from normal RGB input).
     * The field is first set TRUE for decompression, FALSE for compression
     * in initial_setup, and then adapted in color conversion setup.
     */
    internal bool component_needed;

    /* These values are computed before starting a scan of the component. */
    /* The decompressor output side may not use these variables. */
    internal int MCU_width;      /* number of blocks per MCU, horizontally */
    internal int MCU_height;     /* number of blocks per MCU, vertically */
    internal int MCU_blocks;     /* MCU_width * MCU_height */
    internal int MCU_sample_width;       /* MCU width in samples: MCU_width * DCT_h_scaled_size */
    internal int last_col_width;     /* # of non-dummy blocks across in last MCU */
    internal int last_row_height;        /* # of non-dummy blocks down in last MCU */

    /* Saved quantization table for component; null if none yet saved.
     * See jpeg_input_controller comments about the need for this information.
     * This field is currently used only for decompression.
     */
    internal JQUANT_TBL? quant_table;

    internal jpeg_component_info()
    {
    }

    internal void Assign(jpeg_component_info ci)
    {
        Component_id = ci.Component_id;
        Component_index = ci.Component_index;
        H_samp_factor = ci.H_samp_factor;
        V_samp_factor = ci.V_samp_factor;
        Quant_tbl_no = ci.Quant_tbl_no;
        Dc_tbl_no = ci.Dc_tbl_no;
        Ac_tbl_no = ci.Ac_tbl_no;
        Width_in_blocks = ci.Width_in_blocks;
        height_in_blocks = ci.height_in_blocks;
        DCT_h_scaled_size = ci.DCT_h_scaled_size;
        DCT_v_scaled_size = ci.DCT_v_scaled_size;
        downsampled_width = ci.downsampled_width;
        downsampled_height = ci.downsampled_height;
        component_needed = ci.component_needed;
        MCU_width = ci.MCU_width;
        MCU_height = ci.MCU_height;
        MCU_blocks = ci.MCU_blocks;
        MCU_sample_width = ci.MCU_sample_width;
        last_col_width = ci.last_col_width;
        last_row_height = ci.last_row_height;
        quant_table = ci.quant_table;
    }

    /// <summary>
    /// Identifier for this component (0..255)
    /// </summary>
    /// <value>The component ID.</value>
    public int Component_id { get; set; }

    /// <summary>
    /// Its index in SOF or <see cref="jpeg_decompress_struct.Comp_info"/>.
    /// </summary>
    /// <value>The component index.</value>
    public int Component_index { get; set; }

    /// <summary>
    /// Horizontal sampling factor (1..4)
    /// </summary>
    /// <value>The horizontal sampling factor.</value>
    public int H_samp_factor { get; set; }

    /// <summary>
    /// Vertical sampling factor (1..4)
    /// </summary>
    /// <value>The vertical sampling factor.</value>
    public int V_samp_factor { get; set; }

    /// <summary>
    /// Quantization table selector (0..3)
    /// </summary>
    /// <value>The quantization table selector.</value>
    public int Quant_tbl_no { get; set; }

    /// <summary>
    /// DC entropy table selector (0..3)
    /// </summary>
    /// <value>The DC entropy table selector.</value>
    public int Dc_tbl_no { get; set; }

    /// <summary>
    /// AC entropy table selector (0..3)
    /// </summary>
    /// <value>The AC entropy table selector.</value>
    public int Ac_tbl_no { get; set; }

    /// <summary>
    /// Gets or sets the width in blocks.
    /// </summary>
    /// <value>The width in blocks.</value>
    public int Width_in_blocks { get; set; }

    /// <summary>
    /// Gets the downsampled width.
    /// </summary>
    /// <value>The downsampled width.</value>
    public int Downsampled_width
    {
        get { return downsampled_width; }
    }

    internal static jpeg_component_info[] createArrayOfComponents(int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        jpeg_component_info[] result = new jpeg_component_info[length];
        for (int i = 0; i < result.Length; ++i)
        {
            result[i] = new jpeg_component_info();
        }

        return result;
    }
}
