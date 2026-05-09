/*
 * This file contains the inverse-DCT management logic.
 * This code selects a particular IDCT implementation to be used,
 * and it performs related housekeeping chores.  No code in this file
 * is executed per IDCT step, only during output pass setup.
 *
 * Note that the IDCT routines are responsible for performing coefficient
 * dequantization as well as the IDCT proper.  This module sets up the
 * dequantization multiplier table needed by the IDCT routine.
 */

namespace BitMiracle.LibJpeg.Classic.Internal;

/// <summary>
/// An inverse DCT routine is given a pointer to the input JBLOCK and a pointer
/// to an output sample array.  The routine must dequantize the input data as
/// well as perform the IDCT; for dequantization, it uses the multiplier table
/// pointed to by componentInfo.dct_table.  The output data is to be placed into the
/// sample array starting at a specified column. (Any row offset needed will
/// be applied to the array pointer before it is passed to the IDCT code)
/// Note that the number of samples emitted by the IDCT routine is
/// DCT_h_scaled_size * DCT_v_scaled_size.
/// 
/// Each IDCT routine has its own ideas about the best dct_table element type.
/// 
/// The decompressor input side saves away the appropriate
/// quantization table for each component at the start of the first scan
/// involving that component.  (This is necessary in order to correctly
/// decode files that reuse Q-table slots.)
/// When we are ready to make an output pass, the saved Q-table is converted
/// to a multiplier table that will actually be used by the IDCT routine.
/// The multiplier table contents are IDCT-method-dependent.  To support
/// application changes in IDCT method between scans, we can remake the
/// multiplier tables if necessary.
/// In buffered-image mode, the first output pass may occur before any data
/// has been seen for some components, and thus before their Q-tables have
/// been saved away.  To handle this case, multiplier tables are preset
/// to zeroes; the result of the IDCT will be a neutral gray level.
/// </summary>
internal sealed class jpeg_inverse_dct
{
    private const int IFAST_SCALE_BITS = 2; /* fractional bits in scale factors */

    /*
    * Each IDCT routine is responsible for range-limiting its results and
    * converting them to unsigned form (0..MAXJSAMPLE).  The raw outputs could
    * be quite far out of range if the input data is corrupt, so a bulletproof
    * range-limiting step is required.  We use a mask-and-table-lookup method
    * to do the combined operations quickly, assuming that MAXJSAMPLE+1
    * is a power of 2.  See the comments with prepare_range_limit_table for more info.
    */
    private const int RANGE_MASK = (JpegConstants.MAXJSAMPLE * 4 + 3); /* 2 bits wider than legal samples */
    private const int RANGE_CENTER = (JpegConstants.MAXJSAMPLE * 2 + 2);
    private const int RANGE_SUBSET = (RANGE_CENTER - JpegConstants.CENTERJSAMPLE);

    private const int SLOW_INTEGER_CONST_BITS = 13;
    private const int SLOW_INTEGER_PASS1_BITS = 2;
    private const int SLOW_INTEGER_PASS1_PLUS3_BITS = SLOW_INTEGER_CONST_BITS + SLOW_INTEGER_PASS1_BITS + 3;

    /* We use the following pre-calculated constants.
    * If you change SLOW_INTEGER_CONST_BITS you may want to add appropriate values.
    * 
    * Convert a positive real constant to an integer scaled by CONST_SCALE.
    * static int SLOW_INTEGER_FIX(double x)
    * {
    *  return ((int) ((x) * (((int) 1) << SLOW_INTEGER_CONST_BITS) + 0.5));
    * }
    */

    private const int SLOW_INTEGER_FIX_0_298631336 = 2446;   /* SLOW_INTEGER_FIX(0.298631336) */
    private const int SLOW_INTEGER_FIX_0_390180644 = 3196;   /* SLOW_INTEGER_FIX(0.390180644) */
    private const int SLOW_INTEGER_FIX_0_541196100 = 4433;   /* SLOW_INTEGER_FIX(0.541196100) */
    private const int SLOW_INTEGER_FIX_0_765366865 = 6270;   /* SLOW_INTEGER_FIX(0.765366865) */
    private const int SLOW_INTEGER_FIX_0_899976223 = 7373;   /* SLOW_INTEGER_FIX(0.899976223) */
    private const int SLOW_INTEGER_FIX_1_175875602 = 9633;   /* SLOW_INTEGER_FIX(1.175875602) */
    private const int SLOW_INTEGER_FIX_1_501321110 = 12299;  /* SLOW_INTEGER_FIX(1.501321110) */
    private const int SLOW_INTEGER_FIX_1_847759065 = 15137;  /* SLOW_INTEGER_FIX(1.847759065) */
    private const int SLOW_INTEGER_FIX_1_961570560 = 16069;  /* SLOW_INTEGER_FIX(1.961570560) */
    private const int SLOW_INTEGER_FIX_2_053119869 = 16819;  /* SLOW_INTEGER_FIX(2.053119869) */
    private const int SLOW_INTEGER_FIX_2_562915447 = 20995;  /* SLOW_INTEGER_FIX(2.562915447) */
    private const int SLOW_INTEGER_FIX_3_072711026 = 25172;  /* SLOW_INTEGER_FIX(3.072711026) */

    private const int FAST_INTEGER_CONST_BITS = 8;
    private const int FAST_INTEGER_PASS1_BITS = 2;

    /* We use the following pre-calculated constants.
    * If you change FAST_INTEGER_CONST_BITS you may want to add appropriate values.
    */
    private const int FAST_INTEGER_FIX_1_082392200 = 277;        /* FAST_INTEGER_FIX(1.082392200) */
    private const int FAST_INTEGER_FIX_1_414213562 = 362;        /* FAST_INTEGER_FIX(1.414213562) */
    private const int FAST_INTEGER_FIX_1_847759065 = 473;        /* FAST_INTEGER_FIX(1.847759065) */
    private const int FAST_INTEGER_FIX_2_613125930 = 669;        /* FAST_INTEGER_FIX(2.613125930) */

    private const int REDUCED_CONST_BITS = 13;
    private const int REDUCED_PASS1_BITS = 2;

    /* We use the following pre-calculated constants.
    * If you change REDUCED_CONST_BITS you may want to add appropriate values.
    * Convert a positive real constant to an integer scaled by CONST_SCALE.
    * static int REDUCED_FIX(double x)
    * {
    *   return ((int) ((x) * (((int) 1) << REDUCED_CONST_BITS) + 0.5));
    * }
    */

    private const int REDUCED_FIX_0_211164243 = 1730;    /* REDUCED_FIX(0.211164243) */
    private const int REDUCED_FIX_0_509795579 = 4176;    /* REDUCED_FIX(0.509795579) */
    private const int REDUCED_FIX_0_601344887 = 4926;    /* REDUCED_FIX(0.601344887) */
    private const int REDUCED_FIX_0_720959822 = 5906;    /* REDUCED_FIX(0.720959822) */
    private const int REDUCED_FIX_0_765366865 = 6270;    /* REDUCED_FIX(0.765366865) */
    private const int REDUCED_FIX_0_850430095 = 6967;    /* REDUCED_FIX(0.850430095) */
    private const int REDUCED_FIX_0_899976223 = 7373;    /* REDUCED_FIX(0.899976223) */
    private const int REDUCED_FIX_1_061594337 = 8697;    /* REDUCED_FIX(1.061594337) */
    private const int REDUCED_FIX_1_272758580 = 10426;   /* REDUCED_FIX(1.272758580) */
    private const int REDUCED_FIX_1_451774981 = 11893;   /* REDUCED_FIX(1.451774981) */
    private const int REDUCED_FIX_1_847759065 = 15137;   /* REDUCED_FIX(1.847759065) */
    private const int REDUCED_FIX_2_172734803 = 17799;   /* REDUCED_FIX(2.172734803) */
    private const int REDUCED_FIX_2_562915447 = 20995;   /* REDUCED_FIX(2.562915447) */
    private const int REDUCED_FIX_3_624509785 = 29692;   /* REDUCED_FIX(3.624509785) */

    /* precomputed values scaled up by 14 bits */
    private static readonly short[] aanscales =
    {
        16384, 22725, 21407, 19266, 16384, 12873, 8867, 4520, 22725, 31521, 29692, 26722, 22725, 17855,
        12299, 6270, 21407, 29692, 27969, 25172, 21407, 16819, 11585,
        5906, 19266, 26722, 25172, 22654, 19266, 15137, 10426, 5315,
        16384, 22725, 21407, 19266, 16384, 12873, 8867, 4520, 12873,
        17855, 16819, 15137, 12873, 10114, 6967, 3552, 8867, 12299,
        11585, 10426, 8867, 6967, 4799, 2446, 4520, 6270, 5906, 5315,
        4520, 3552, 2446, 1247
    };

    private const int CONST_BITS = 14;

    private static readonly double[] aanscalefactor =
    {
        1.0, 1.387039845, 1.306562965, 1.175875602, 1.0,
        0.785694958, 0.541196100, 0.275899379
    };

    private enum InverseMethodKind : byte
    {
        None = 0,
        IdctIslow = 1,
        IdctIfast = 2,
        IdctFloat = 3,
        Idct1x1 = 4,
        Idct2x2 = 5,
        Idct3x3 = 6,
        Idct4x4 = 7,
        Idct5x5 = 8,
        Idct6x6 = 9,
        Idct7x7 = 10,
        Idct9x9 = 11,
        Idct10x10 = 12,
        Idct11x11 = 13,
        Idct12x12 = 14,
        Idct13x13 = 15,
        Idct14x14 = 16,
        Idct15x15 = 17,
        Idct16x16 = 18,
        Idct16x8 = 19,
        Idct14x7 = 20,
        Idct12x6 = 21,
        Idct10x5 = 22,
        Idct8x4 = 23,
        Idct6x3 = 24,
        Idct4x2 = 25,
        Idct2x1 = 26,
        Idct8x16 = 27,
        Idct7x14 = 28,
        Idct6x12 = 29,
        Idct5x10 = 30,
        Idct4x8 = 31,
        Idct3x6 = 32,
        Idct2x4 = 33,
        Idct1x2 = 34,
    }

    /* It is useful to allow each component to have a separate IDCT method. */
    private InverseMethodKind[] m_inverse_DCT_method = new InverseMethodKind[JpegConstants.MAX_COMPONENTS];

    /* Allocated multiplier tables: big enough for any supported variant */
    private class multiplier_table
    {
        public int[] int_array = new int[JpegConstants.DCTSIZE2];
        public float[] float_array = new float[JpegConstants.DCTSIZE2];
    };

    private multiplier_table[] m_dctTables;

    private jpeg_decompress_struct m_cinfo;

    /* This array contains the IDCT method code that each multiplier table
    * is currently set up for, or -1 if it's not yet set up.
    * The actual multiplier tables are pointed to by dct_table in the
    * per-component comp_info structures.
    */
    private int[] m_cur_method = new int[JpegConstants.MAX_COMPONENTS];

    private ComponentBuffer m_componentBuffer;

    /* buffers data between passes */
    private int[] m_workspace = new int[Math.Max(JpegConstants.DCTSIZE2, 8 * 16)];
    private float[] m_fworkspace = new float[Math.Max(JpegConstants.DCTSIZE2, 8 * 16)];

    public jpeg_inverse_dct(jpeg_decompress_struct cinfo)
    {
        m_cinfo = cinfo;

        m_dctTables = new multiplier_table[cinfo.m_num_components];
        for (int ci = 0; ci < cinfo.m_num_components; ci++)
        {
            /* Allocate and pre-zero a multiplier table for each component */
            m_dctTables[ci] = new multiplier_table();

            /* Mark multiplier table not yet set up for any method */
            m_cur_method[ci] = -1;
        }
    }

    /// <summary>
    /// Prepare for an output pass.
    /// Here we select the proper IDCT routine for each component and build
    /// a matching multiplier table.
    /// </summary>
    public void start_pass()
    {
        for (int ci = 0; ci < m_cinfo.m_num_components; ci++)
        {
            jpeg_component_info componentInfo = m_cinfo.Comp_info![ci];

            InverseMethodKind im = InverseMethodKind.None;
            int method = 0;
            /* Select the proper IDCT routine for this component's scaling */
            switch ((componentInfo.DCT_h_scaled_size << 8) + componentInfo.DCT_v_scaled_size)
            {
                case ((1 << 8) + 1):
                    im = InverseMethodKind.Idct1x1;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((2 << 8) + 2):
                    im = InverseMethodKind.Idct2x2;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((3 << 8) + 3):
                    im = InverseMethodKind.Idct3x3;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((4 << 8) + 4):
                    im = InverseMethodKind.Idct4x4;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((5 << 8) + 5):
                    im = InverseMethodKind.Idct5x5;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((6 << 8) + 6):
                    im = InverseMethodKind.Idct6x6;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((7 << 8) + 7):
                    im = InverseMethodKind.Idct7x7;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((9 << 8) + 9):
                    im = InverseMethodKind.Idct9x9;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((10 << 8) + 10):
                    im = InverseMethodKind.Idct10x10;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((11 << 8) + 11):
                    im = InverseMethodKind.Idct11x11;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((12 << 8) + 12):
                    im = InverseMethodKind.Idct12x12;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((13 << 8) + 13):
                    im = InverseMethodKind.Idct13x13;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((14 << 8) + 14):
                    im = InverseMethodKind.Idct14x14;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((15 << 8) + 15):
                    im = InverseMethodKind.Idct15x15;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((16 << 8) + 16):
                    im = InverseMethodKind.Idct16x16;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((16 << 8) + 8):
                    im = InverseMethodKind.Idct16x8;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((14 << 8) + 7):
                    im = InverseMethodKind.Idct14x7;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((12 << 8) + 6):
                    im = InverseMethodKind.Idct12x6;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((10 << 8) + 5):
                    im = InverseMethodKind.Idct10x5;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((8 << 8) + 4):
                    im = InverseMethodKind.Idct8x4;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((6 << 8) + 3):
                    im = InverseMethodKind.Idct6x3;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((4 << 8) + 2):
                    im = InverseMethodKind.Idct4x2;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((2 << 8) + 1):
                    im = InverseMethodKind.Idct2x1;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((8 << 8) + 16):
                    im = InverseMethodKind.Idct8x16;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((7 << 8) + 14):
                    im = InverseMethodKind.Idct7x14;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((6 << 8) + 12):
                    im = InverseMethodKind.Idct6x12;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((5 << 8) + 10):
                    im = InverseMethodKind.Idct5x10;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((4 << 8) + 8):
                    im = InverseMethodKind.Idct4x8;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((3 << 8) + 6):
                    im = InverseMethodKind.Idct3x6;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((2 << 8) + 4):
                    im = InverseMethodKind.Idct2x4;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((1 << 8) + 2):
                    im = InverseMethodKind.Idct1x2;
                    method = (int)J_DCT_METHOD.JDCT_ISLOW;    /* jidctint uses islow-style table */
                    break;
                case ((JpegConstants.DCTSIZE << 8) + JpegConstants.DCTSIZE):
                    switch (m_cinfo.m_dct_method)
                    {
                        case J_DCT_METHOD.JDCT_ISLOW:
                            im = InverseMethodKind.IdctIslow;
                            method = (int)J_DCT_METHOD.JDCT_ISLOW;
                            break;
                        case J_DCT_METHOD.JDCT_IFAST:
                            im = InverseMethodKind.IdctIfast;
                            method = (int)J_DCT_METHOD.JDCT_IFAST;
                            break;
                        case J_DCT_METHOD.JDCT_FLOAT:
                            im = InverseMethodKind.IdctFloat;
                            method = (int)J_DCT_METHOD.JDCT_FLOAT;
                            break;
                        default:
                            m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOT_COMPILED);
                            break;
                    }
                    break;
                default:
                    m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_BAD_DCTSIZE, componentInfo.DCT_h_scaled_size, componentInfo.DCT_v_scaled_size);
                    break;
            }

            m_inverse_DCT_method[ci] = im;

            /* Create multiplier table from quant table.
             * However, we can skip this if the component is uninteresting
             * or if we already built the table.  Also, if no quant table
             * has yet been saved for the component, we leave the
             * multiplier table all-zero; we'll be reading zeroes from the
             * coefficient controller's buffer anyway.
             */
            if (!componentInfo.component_needed || m_cur_method[ci] == method)
            {
                continue;
            }

            JQUANT_TBL? qtbl = componentInfo.quant_table;
            if (qtbl == null)
            {
                /* happens if no data yet for component */
                continue;
            }

            m_cur_method[ci] = method;
            switch ((J_DCT_METHOD)method)
            {
                case J_DCT_METHOD.JDCT_ISLOW:
                    /* For LL&M IDCT method, multipliers are equal to raw quantization
                     * coefficients, but are stored as ints to ensure access efficiency.
                     */
                    int[] ismtbl = m_dctTables[ci].int_array;
                    for (int i = 0; i < JpegConstants.DCTSIZE2; i++)
                    {
                        ismtbl[i] = qtbl.quantval[i];
                    }

                    break;

                case J_DCT_METHOD.JDCT_IFAST:
                    /* For AA&N IDCT method, multipliers are equal to quantization
                     * coefficients scaled by scalefactor[row]*scalefactor[col], where
                     *   scalefactor[0] = 1
                     *   scalefactor[k] = cos(k*PI/16) * sqrt(2)    for k=1..7
                     * For integer operation, the multiplier table is to be scaled by
                     * IFAST_SCALE_BITS.
                     */
                    int[] ifmtbl = m_dctTables[ci].int_array;

                    for (int i = 0; i < JpegConstants.DCTSIZE2; i++)
                    {
                        ifmtbl[i] = JpegUtils.DESCALE(
                            (int)qtbl.quantval[i] * (int)aanscales[i],
                            CONST_BITS - IFAST_SCALE_BITS);
                    }
                    break;

                case J_DCT_METHOD.JDCT_FLOAT:
                    /* For float AA&N IDCT method, multipliers are equal to quantization
                     * coefficients scaled by scalefactor[row]*scalefactor[col], where
                     *   scalefactor[0] = 1
                     *   scalefactor[k] = cos(k*PI/16) * sqrt(2)    for k=1..7
                     * We apply a further scale factor of 1/8.
                     */
                    float[] fmtbl = m_dctTables[ci].float_array;
                    int ii = 0;
                    for (int row = 0; row < JpegConstants.DCTSIZE; row++)
                    {
                        for (int col = 0; col < JpegConstants.DCTSIZE; col++)
                        {
                            fmtbl[ii] = (float)((double)qtbl.quantval[ii] * aanscalefactor[row] * aanscalefactor[col] * 0.125);
                            ii++;
                        }
                    }
                    break;

                default:
                    m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOT_COMPILED);
                    break;
            }
        }
    }

    /* Inverse DCT (also performs dequantization) */
    public void inverse(int component_index, ReadOnlySpan<short> coef_block, ComponentBuffer output_buf, int output_row, int output_col)
    {
        m_componentBuffer = output_buf;

        switch (m_inverse_DCT_method[component_index])
        {
            case InverseMethodKind.IdctIslow:
                jpeg_idct_islow(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.IdctIfast:
                jpeg_idct_ifast(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.IdctFloat:
                jpeg_idct_float(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct1x1:
                jpeg_idct_1x1(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct2x2:
                jpeg_idct_2x2(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct3x3:
                jpeg_idct_3x3(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct4x4:
                jpeg_idct_4x4(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct5x5:
                jpeg_idct_5x5(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct6x6:
                jpeg_idct_6x6(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct7x7:
                jpeg_idct_7x7(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct9x9:
                jpeg_idct_9x9(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct10x10:
                jpeg_idct_10x10(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct11x11:
                jpeg_idct_11x11(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct12x12:
                jpeg_idct_12x12(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct13x13:
                jpeg_idct_13x13(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct14x14:
                jpeg_idct_14x14(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct15x15:
                jpeg_idct_15x15(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct16x16:
                jpeg_idct_16x16(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct16x8:
                jpeg_idct_16x8(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct14x7:
                jpeg_idct_14x7(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct12x6:
                jpeg_idct_12x6(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct10x5:
                jpeg_idct_10x5(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct8x4:
                jpeg_idct_8x4(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct6x3:
                jpeg_idct_6x3(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct4x2:
                jpeg_idct_4x2(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct2x1:
                jpeg_idct_2x1(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct8x16:
                jpeg_idct_8x16(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct7x14:
                jpeg_idct_7x14(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct6x12:
                jpeg_idct_6x12(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct5x10:
                jpeg_idct_5x10(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct4x8:
                jpeg_idct_4x8(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct3x6:
                jpeg_idct_3x6(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct2x4:
                jpeg_idct_2x4(component_index, coef_block, output_row, output_col);
                return;
            case InverseMethodKind.Idct1x2:
                jpeg_idct_1x2(component_index, coef_block, output_row, output_col);
                return;
            default:
                m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOT_COMPILED);
                return;
        }
    }

    /// <summary>
    /// Perform dequantization and inverse DCT on one block of coefficients.
    /// NOTE: this code only copes with 8x8 DCTs.
    /// A slow-but-accurate integer implementation of the
    /// inverse DCT (Discrete Cosine Transform).  In the IJG code, this routine
    /// must also perform dequantization of the input coefficients.
    /// 
    /// A 2-D IDCT can be done by 1-D IDCT on each column followed by 1-D IDCT
    /// on each row (or vice versa, but it's more convenient to emit a row at
    /// a time).  Direct algorithms are also available, but they are much more
    /// complex and seem not to be any faster when reduced to code.
    /// 
    /// This implementation is based on an algorithm described in
    /// C. Loeffler, A. Ligtenberg and G. Moschytz, "Practical Fast 1-D DCT
    /// Algorithms with 11 Multiplications", Proc. Int'l. Conf. on Acoustics,
    /// Speech, and Signal Processing 1989 (ICASSP '89), pp. 988-991.
    /// The primary algorithm described there uses 11 multiplies and 29 adds.
    /// We use their alternate method with 12 multiplies and 32 adds.
    /// The advantage of this method is that no data path contains more than one
    /// multiplication; this allows a very simple and accurate implementation in
    /// scaled fixed-point arithmetic, with a minimal number of shifts.
    /// 
    /// The poop on this scaling stuff is as follows:
    /// 
    /// Each 1-D IDCT step produces outputs which are a factor of sqrt(N)
    /// larger than the true IDCT outputs.  The final outputs are therefore
    /// a factor of N larger than desired; since N=8 this can be cured by
    /// a simple right shift at the end of the algorithm.  The advantage of
    /// this arrangement is that we save two multiplications per 1-D IDCT,
    /// because the y0 and y4 inputs need not be divided by sqrt(N).
    /// 
    /// We have to do addition and subtraction of the integer inputs, which
    /// is no problem, and multiplication by fractional constants, which is
    /// a problem to do in integer arithmetic.  We multiply all the constants
    /// by CONST_SCALE and convert them to integer constants (thus retaining
    /// SLOW_INTEGER_CONST_BITS bits of precision in the constants).  After doing a
    /// multiplication we have to divide the product by CONST_SCALE, with proper
    /// rounding, to produce the correct output.  This division can be done
    /// cheaply as a right shift of SLOW_INTEGER_CONST_BITS bits.  We postpone shifting
    /// as long as possible so that partial sums can be added together with
    /// full fractional precision.
    /// 
    /// The outputs of the first pass are scaled up by SLOW_INTEGER_PASS1_BITS bits so that
    /// they are represented to better-than-integral precision.  These outputs
    /// require BITS_IN_JSAMPLE + SLOW_INTEGER_PASS1_BITS + 3 bits; this fits in a 16-bit word
    /// with the recommended scaling.  (To scale up 12-bit sample data further, an
    /// intermediate int array would be needed.)
    /// 
    /// To avoid overflow of the 32-bit intermediate results in pass 2, we must
    /// have BITS_IN_JSAMPLE + SLOW_INTEGER_CONST_BITS + SLOW_INTEGER_PASS1_BITS &lt;= 26.  Error analysis
    /// shows that the values given below are the most effective.
    /// </summary>
    private void jpeg_idct_islow(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        /* Pass 1: process columns from input, store into work array. */
        /* Note results are scaled up by sqrt(8) compared to a true IDCT; */
        /* furthermore, we scale the results by 2**SLOW_INTEGER_PASS1_BITS. */

        int[] quantTable = m_dctTables[component_index].int_array;
        for (int ctr = JpegConstants.DCTSIZE, coefBlockIndex = 0, quantTableIndex = 0, workspaceIndex = 0;
            ctr > 0;
            ctr--, coefBlockIndex++, quantTableIndex++, workspaceIndex++)
        {
            /* Due to quantization, we will usually find that many of the input
            * coefficients are zero, especially the AC terms.  We can exploit this
            * by short-circuiting the IDCT calculation for any column in which all
            * the AC terms are zero.  In that case each output is equal to the
            * DC coefficient (with scale factor as needed).
            * With typical images and quantization tables, half or more of the
            * column DCT calculations can be simplified this way.
            */

            if (coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 2] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 4] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 6] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7] == 0)
            {
                /* AC terms all zero */
                int dcval = (coef_block[coefBlockIndex] * quantTable[quantTableIndex]) << SLOW_INTEGER_PASS1_BITS;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 0] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 1] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 2] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 3] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 4] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 5] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 6] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 7] = dcval;
                continue;
            }

            /* Even part: reverse the even part of the forward DCT. */
            /* The rotator is c(-6). */

            int z2 = coef_block[coefBlockIndex] * quantTable[quantTableIndex];
            int z3 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 4] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 4];

            z2 <<= SLOW_INTEGER_CONST_BITS;
            z3 <<= SLOW_INTEGER_CONST_BITS;
            /* Add fudge factor here for final descale. */
            z2 += 1 << (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS - 1);

            int tmp0 = z2 + z3;
            int tmp1 = z2 - z3;

            z2 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 2] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 2];
            z3 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 6] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 6];

            int z1 = (z2 + z3) * SLOW_INTEGER_FIX_0_541196100;
            int tmp2 = z1 + z2 * SLOW_INTEGER_FIX_0_765366865;
            int tmp3 = z1 - z3 * SLOW_INTEGER_FIX_1_847759065;

            int tmp10 = tmp0 + tmp2;
            int tmp13 = tmp0 - tmp2;
            int tmp11 = tmp1 + tmp3;
            int tmp12 = tmp1 - tmp3;

            /* Odd part per figure 8; the matrix is unitary and hence its
            * transpose is its inverse.  i0..i3 are y7,y5,y3,y1 respectively.
            */

            tmp0 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 7];
            tmp1 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 5];
            tmp2 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 3];
            tmp3 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 1];

            z2 = tmp0 + tmp2;
            z3 = tmp1 + tmp3;

            z1 = (z2 + z3) * SLOW_INTEGER_FIX_1_175875602;       /*  c3 */
            z2 *= (-SLOW_INTEGER_FIX_1_961570560);          /* -c3-c5 */
            z3 *= (-SLOW_INTEGER_FIX_0_390180644);          /* -c3+c5 */
            z2 += z1;
            z3 += z1;

            z1 = (tmp0 + tmp3) * (-SLOW_INTEGER_FIX_0_899976223); /* -c3+c7 */
            tmp0 *= SLOW_INTEGER_FIX_0_298631336;        /* -c1+c3+c5-c7 */
            tmp3 *= SLOW_INTEGER_FIX_1_501321110;        /*  c1+c3-c5-c7 */
            tmp0 += z1 + z2;
            tmp3 += z1 + z3;

            z1 = (tmp1 + tmp2) * (-SLOW_INTEGER_FIX_2_562915447); /* -c1-c3 */
            tmp1 *= SLOW_INTEGER_FIX_2_053119869;        /*  c1+c3-c5+c7 */
            tmp2 *= SLOW_INTEGER_FIX_3_072711026;        /*  c1+c3+c5-c7 */
            tmp1 += z1 + z3;
            tmp2 += z1 + z2;

            /* Final output stage: inputs are tmp10..tmp13, tmp0..tmp3 */

            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 0] = (tmp10 + tmp3) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 7] = (tmp10 - tmp3) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 1] = (tmp11 + tmp2) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 6] = (tmp11 - tmp2) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 2] = (tmp12 + tmp1) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 5] = (tmp12 - tmp1) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 3] = (tmp13 + tmp0) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 4] = (tmp13 - tmp0) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
        }

        /* Pass 2: process rows from work array, store into output array. */
        /* Note that we must descale the results by a factor of 8 == 2**3, */
        /* and also undo the SLOW_INTEGER_PASS1_BITS scaling. */

        byte[] limit = m_cinfo.m_sample_range_limit;
        int limitOffset = m_cinfo.m_sampleRangeLimitOffset - RANGE_SUBSET;

        for (int ctr = 0, workspaceIndex = 0;
            ctr < JpegConstants.DCTSIZE;
            ctr++, workspaceIndex += JpegConstants.DCTSIZE)
        {
            /* Add range center and fudge factor for final descale and range-limit. */
            int z2 = m_workspace[workspaceIndex] +
                (RANGE_CENTER << (SLOW_INTEGER_PASS1_BITS + 3)) +
                (1 << (SLOW_INTEGER_PASS1_BITS + 2));

            /* Rows of zeroes can be exploited in the same way as we did with columns.
            * However, the column calculation has created many nonzero AC terms, so
            * the simplification applies less often (typically 5% to 10% of the time).
            * On machines with very fast multiplication, it's possible that the
            * test takes more time than it's worth.  In that case this section
            * may be commented out.
            */
            int currentOutRow = output_row + ctr;
            var currentOutBuffer = m_componentBuffer[currentOutRow];
            if (m_workspace[workspaceIndex + 1] == 0 &&
                m_workspace[workspaceIndex + 2] == 0 &&
                m_workspace[workspaceIndex + 3] == 0 &&
                m_workspace[workspaceIndex + 4] == 0 &&
                m_workspace[workspaceIndex + 5] == 0 &&
                m_workspace[workspaceIndex + 6] == 0 &&
                m_workspace[workspaceIndex + 7] == 0)
            {
                /* AC terms all zero */
                byte dcval = limit[limitOffset + ((z2 >> (SLOW_INTEGER_PASS1_BITS + 3)) & RANGE_MASK)];
                currentOutBuffer[output_col + 0] = dcval;
                currentOutBuffer[output_col + 1] = dcval;
                currentOutBuffer[output_col + 2] = dcval;
                currentOutBuffer[output_col + 3] = dcval;
                currentOutBuffer[output_col + 4] = dcval;
                currentOutBuffer[output_col + 5] = dcval;
                currentOutBuffer[output_col + 6] = dcval;
                currentOutBuffer[output_col + 7] = dcval;
                continue;
            }

            /* Even part: reverse the even part of the forward DCT. */
            /* The rotator is c(-6). */

            int z3 = m_workspace[workspaceIndex + 4];

            int tmp0 = (z2 + z3) << SLOW_INTEGER_CONST_BITS;
            int tmp1 = (z2 - z3) << SLOW_INTEGER_CONST_BITS;

            z2 = m_workspace[workspaceIndex + 2];
            z3 = m_workspace[workspaceIndex + 6];

            int z1 = (z2 + z3) * SLOW_INTEGER_FIX_0_541196100; /* c6 */
            int tmp2 = z1 + z2 * SLOW_INTEGER_FIX_0_765366865; /* c2-c6 */
            int tmp3 = z1 - z3 * SLOW_INTEGER_FIX_1_847759065; /* c2+c6 */

            int tmp10 = tmp0 + tmp2;
            int tmp13 = tmp0 - tmp2;
            int tmp11 = tmp1 + tmp3;
            int tmp12 = tmp1 - tmp3;

            /* Odd part per figure 8; the matrix is unitary and hence its
            * transpose is its inverse.  i0..i3 are y7,y5,y3,y1 respectively.
            */

            tmp0 = m_workspace[workspaceIndex + 7];
            tmp1 = m_workspace[workspaceIndex + 5];
            tmp2 = m_workspace[workspaceIndex + 3];
            tmp3 = m_workspace[workspaceIndex + 1];

            z2 = tmp0 + tmp2;
            z3 = tmp1 + tmp3;

            z1 = (z2 + z3) * SLOW_INTEGER_FIX_1_175875602;       /*  c3 */
            z2 *= (-SLOW_INTEGER_FIX_1_961570560);          /* -c3-c5 */
            z3 *= (-SLOW_INTEGER_FIX_0_390180644);          /* -c3+c5 */
            z2 += z1;
            z3 += z1;

            z1 = (tmp0 + tmp3) * (-SLOW_INTEGER_FIX_0_899976223); /* -c3+c7 */
            tmp0 *= SLOW_INTEGER_FIX_0_298631336;        /* -c1+c3+c5-c7 */
            tmp3 *= SLOW_INTEGER_FIX_1_501321110;        /*  c1+c3-c5-c7 */
            tmp0 += z1 + z2;
            tmp3 += z1 + z3;

            z1 = (tmp1 + tmp2) * (-SLOW_INTEGER_FIX_2_562915447); /* -c1-c3 */
            tmp1 *= SLOW_INTEGER_FIX_2_053119869;        /*  c1+c3-c5+c7 */
            tmp2 *= SLOW_INTEGER_FIX_3_072711026;        /*  c1+c3+c5-c7 */
            tmp1 += z1 + z3;
            tmp2 += z1 + z2;

            /* Final output stage: inputs are tmp10..tmp13, tmp0..tmp3 */

            currentOutBuffer[output_col + 0] = limit[limitOffset + ((tmp10 + tmp3) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 7] = limit[limitOffset + ((tmp10 - tmp3) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 1] = limit[limitOffset + ((tmp11 + tmp2) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 6] = limit[limitOffset + ((tmp11 - tmp2) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 2] = limit[limitOffset + ((tmp12 + tmp1) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 5] = limit[limitOffset + ((tmp12 - tmp1) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 3] = limit[limitOffset + ((tmp13 + tmp0) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 4] = limit[limitOffset + ((tmp13 - tmp0) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
        }
    }

    private static int SLOW_INTEGER_FIX(double x)
    {
        return (int)(x * (1 << SLOW_INTEGER_CONST_BITS) + 0.5);
    }

    /// <summary>
    /// Perform dequantization and inverse DCT on one block of coefficients.
    /// NOTE: this code only copes with 8x8 DCTs.
    /// 
    /// A fast, not so accurate integer implementation of the
    /// inverse DCT (Discrete Cosine Transform).  In the IJG code, this routine
    /// must also perform dequantization of the input coefficients.
    /// 
    /// A 2-D IDCT can be done by 1-D IDCT on each column followed by 1-D IDCT
    /// on each row (or vice versa, but it's more convenient to emit a row at
    /// a time).  Direct algorithms are also available, but they are much more
    /// complex and seem not to be any faster when reduced to code.
    /// 
    /// This implementation is based on Arai, Agui, and Nakajima's algorithm for
    /// scaled DCT.  Their original paper (Trans. IEICE E-71(11):1095) is in
    /// Japanese, but the algorithm is described in the Pennebaker &amp; Mitchell
    /// JPEG textbook (see REFERENCES section in file README).  The following code
    /// is based directly on figure 4-8 in P&amp;M.
    /// While an 8-point DCT cannot be done in less than 11 multiplies, it is
    /// possible to arrange the computation so that many of the multiplies are
    /// simple scalings of the final outputs.  These multiplies can then be
    /// folded into the multiplications or divisions by the JPEG quantization
    /// table entries.  The AA&amp;N method leaves only 5 multiplies and 29 adds
    /// to be done in the DCT itself.
    /// The primary disadvantage of this method is that with fixed-point math,
    /// accuracy is lost due to imprecise representation of the scaled
    /// quantization values.  The smaller the quantization table entry, the less
    /// precise the scaled value, so this implementation does worse with high-
    /// quality-setting files than with low-quality ones.
    /// 
    /// Scaling decisions are generally the same as in the LL&amp;M algorithm;
    /// However, we choose to descale
    /// (right shift) multiplication products as soon as they are formed,
    /// rather than carrying additional fractional bits into subsequent additions.
    /// This compromises accuracy slightly, but it lets us save a few shifts.
    /// More importantly, 16-bit arithmetic is then adequate (for 8-bit samples)
    /// everywhere except in the multiplications proper; this saves a good deal
    /// of work on 16-bit-int machines.
    /// 
    /// The dequantized coefficients are not integers because the AA&amp;N scaling
    /// factors have been incorporated.  We represent them scaled up by FAST_INTEGER_PASS1_BITS,
    /// so that the first and second IDCT rounds have the same input scaling.
    /// For 8-bit JSAMPLEs, we choose IFAST_SCALE_BITS = FAST_INTEGER_PASS1_BITS so as to
    /// avoid a descaling shift; this compromises accuracy rather drastically
    /// for small quantization table entries, but it saves a lot of shifts.
    /// For 12-bit JSAMPLEs, there's no hope of using 16x16 multiplies anyway,
    /// so we use a much larger scaling factor to preserve accuracy.
    /// 
    /// A final compromise is to represent the multiplicative constants to only
    /// 8 fractional bits, rather than 13.  This saves some shifting work on some
    /// machines, and may also reduce the cost of multiplication (since there
    /// are fewer one-bits in the constants).
    /// </summary>
    private void jpeg_idct_ifast(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        /* Pass 1: process columns from input, store into work array. */

        int coefBlockIndex = 0;
        int workspaceIndex = 0;

        int[] quantTable = m_dctTables[component_index].int_array;
        int quantTableIndex = 0;

        for (int ctr = JpegConstants.DCTSIZE; ctr > 0; ctr--)
        {
            /* Due to quantization, we will usually find that many of the input
            * coefficients are zero, especially the AC terms.  We can exploit this
            * by short-circuiting the IDCT calculation for any column in which all
            * the AC terms are zero.  In that case each output is equal to the
            * DC coefficient (with scale factor as needed).
            * With typical images and quantization tables, half or more of the
            * column DCT calculations can be simplified this way.
            */

            if (coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 2] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 4] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 6] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7] == 0)
            {
                /* AC terms all zero */
                int dcval = FAST_INTEGER_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 0],
                    quantTable[quantTableIndex + JpegConstants.DCTSIZE * 0]);

                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 0] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 1] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 2] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 3] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 4] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 5] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 6] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 7] = dcval;

                /* advance pointers to next column */
                coefBlockIndex++;
                quantTableIndex++;
                workspaceIndex++;
                continue;
            }

            /* Even part */

            int tmp0 = FAST_INTEGER_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 0],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 0]);
            int tmp1 = FAST_INTEGER_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 2],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 2]);
            int tmp2 = FAST_INTEGER_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 4],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 4]);
            int tmp3 = FAST_INTEGER_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 6],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 6]);

            int tmp10 = tmp0 + tmp2;    /* phase 3 */
            int tmp11 = tmp0 - tmp2;

            int tmp13 = tmp1 + tmp3;    /* phases 5-3 */
            int tmp12 = FAST_INTEGER_MULTIPLY(tmp1 - tmp3, FAST_INTEGER_FIX_1_414213562) - tmp13; /* 2*c4 */

            tmp0 = tmp10 + tmp13;   /* phase 2 */
            tmp3 = tmp10 - tmp13;
            tmp1 = tmp11 + tmp12;
            tmp2 = tmp11 - tmp12;

            /* Odd part */

            int tmp4 = FAST_INTEGER_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 1]);
            int tmp5 = FAST_INTEGER_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 3]);
            int tmp6 = FAST_INTEGER_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 5]);
            int tmp7 = FAST_INTEGER_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 7]);

            int z13 = tmp6 + tmp5;      /* phase 6 */
            int z10 = tmp6 - tmp5;
            int z11 = tmp4 + tmp7;
            int z12 = tmp4 - tmp7;

            tmp7 = z11 + z13;       /* phase 5 */
            tmp11 = FAST_INTEGER_MULTIPLY(z11 - z13, FAST_INTEGER_FIX_1_414213562); /* 2*c4 */

            int z5 = FAST_INTEGER_MULTIPLY(z10 + z12, FAST_INTEGER_FIX_1_847759065); /* 2*c2 */
            tmp10 = z5 - FAST_INTEGER_MULTIPLY(z12, FAST_INTEGER_FIX_1_082392200); /* 2*(c2-c6) */
            tmp12 = z5 - FAST_INTEGER_MULTIPLY(z10, FAST_INTEGER_FIX_2_613125930); /* 2*(c2+c6) */

            tmp6 = tmp12 - tmp7;    /* phase 2 */
            tmp5 = tmp11 - tmp6;
            tmp4 = tmp10 - tmp5;

            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 0] = tmp0 + tmp7;
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 7] = tmp0 - tmp7;
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 1] = tmp1 + tmp6;
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 6] = tmp1 - tmp6;
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 2] = tmp2 + tmp5;
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 5] = tmp2 - tmp5;
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 3] = tmp3 + tmp4;
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 4] = tmp3 - tmp4;

            /* advance pointers to next column */
            coefBlockIndex++;
            quantTableIndex++;
            workspaceIndex++;
        }

        /* Pass 2: process rows from work array, store into output array. */
        /* Note that we must descale the results by a factor of 8 == 2**3, */
        /* and also undo the FAST_INTEGER_PASS1_BITS scaling. */

        workspaceIndex = 0;
        byte[] limit = m_cinfo.m_sample_range_limit;
        int limitOffset = m_cinfo.m_sampleRangeLimitOffset - RANGE_SUBSET;

        for (int ctr = 0; ctr < JpegConstants.DCTSIZE; ctr++)
        {
            int currentOutRow = output_row + ctr;

            /* Add range center and fudge factor for final descale and range-limit. */
            int z5 = m_workspace[workspaceIndex + 0] +
               ((RANGE_CENTER << (FAST_INTEGER_PASS1_BITS + 3)) +
                (1 << (FAST_INTEGER_PASS1_BITS + 2)));

            /* Rows of zeroes can be exploited in the same way as we did with columns.
            * However, the column calculation has created many nonzero AC terms, so
            * the simplification applies less often (typically 5% to 10% of the time).
            * On machines with very fast multiplication, it's possible that the
            * test takes more time than it's worth.  In that case this section
            * may be commented out.
            */
            var currentOutBuffer = m_componentBuffer[currentOutRow];
            if (m_workspace[workspaceIndex + 1] == 0 &&
                m_workspace[workspaceIndex + 2] == 0 &&
                m_workspace[workspaceIndex + 3] == 0 &&
                m_workspace[workspaceIndex + 4] == 0 &&
                m_workspace[workspaceIndex + 5] == 0 &&
                m_workspace[workspaceIndex + 6] == 0 &&
                m_workspace[workspaceIndex + 7] == 0)
            {
                /* AC terms all zero */
                byte dcval = limit[limitOffset + FAST_INTEGER_IRIGHT_SHIFT(z5, FAST_INTEGER_PASS1_BITS + 3) & RANGE_MASK];

                currentOutBuffer[output_col + 0] = dcval;
                currentOutBuffer[output_col + 1] = dcval;
                currentOutBuffer[output_col + 2] = dcval;
                currentOutBuffer[output_col + 3] = dcval;
                currentOutBuffer[output_col + 4] = dcval;
                currentOutBuffer[output_col + 5] = dcval;
                currentOutBuffer[output_col + 6] = dcval;
                currentOutBuffer[output_col + 7] = dcval;

                /* advance pointer to next row */
                workspaceIndex += JpegConstants.DCTSIZE;
                continue;
            }

            /* Even part */

            int tmp10 = z5 + m_workspace[workspaceIndex + 4];
            int tmp11 = z5 - m_workspace[workspaceIndex + 4];

            int tmp13 = m_workspace[workspaceIndex + 2] + m_workspace[workspaceIndex + 6];
            int tmp12 = FAST_INTEGER_MULTIPLY(m_workspace[workspaceIndex + 2] - m_workspace[workspaceIndex + 6],
                FAST_INTEGER_FIX_1_414213562) - tmp13; /* 2*c4 */

            int tmp0 = tmp10 + tmp13;
            int tmp3 = tmp10 - tmp13;
            int tmp1 = tmp11 + tmp12;
            int tmp2 = tmp11 - tmp12;

            /* Odd part */

            int z13 = m_workspace[workspaceIndex + 5] + m_workspace[workspaceIndex + 3];
            int z10 = m_workspace[workspaceIndex + 5] - m_workspace[workspaceIndex + 3];
            int z11 = m_workspace[workspaceIndex + 1] + m_workspace[workspaceIndex + 7];
            int z12 = m_workspace[workspaceIndex + 1] - m_workspace[workspaceIndex + 7];

            int tmp7 = z11 + z13;       /* phase 5 */
            tmp11 = FAST_INTEGER_MULTIPLY(z11 - z13, FAST_INTEGER_FIX_1_414213562); /* 2*c4 */

            z5 = FAST_INTEGER_MULTIPLY(z10 + z12, FAST_INTEGER_FIX_1_847759065); /* 2*c2 */
            tmp10 = z5 - FAST_INTEGER_MULTIPLY(z12, FAST_INTEGER_FIX_1_082392200); /* 2*(c2-c6) */
            tmp12 = z5 - FAST_INTEGER_MULTIPLY(z10, FAST_INTEGER_FIX_2_613125930); /* 2*(c2+c6) */

            int tmp6 = tmp12 - tmp7;    /* phase 2 */
            int tmp5 = tmp11 - tmp6;
            int tmp4 = tmp10 - tmp5;

            /* Final output stage: scale down by a factor of 8 and range-limit */

            currentOutBuffer[output_col + 0] = limit[limitOffset + FAST_INTEGER_IRIGHT_SHIFT(tmp0 + tmp7, FAST_INTEGER_PASS1_BITS + 3) & RANGE_MASK];
            currentOutBuffer[output_col + 7] = limit[limitOffset + FAST_INTEGER_IRIGHT_SHIFT(tmp0 - tmp7, FAST_INTEGER_PASS1_BITS + 3) & RANGE_MASK];
            currentOutBuffer[output_col + 1] = limit[limitOffset + FAST_INTEGER_IRIGHT_SHIFT(tmp1 + tmp6, FAST_INTEGER_PASS1_BITS + 3) & RANGE_MASK];
            currentOutBuffer[output_col + 6] = limit[limitOffset + FAST_INTEGER_IRIGHT_SHIFT(tmp1 - tmp6, FAST_INTEGER_PASS1_BITS + 3) & RANGE_MASK];
            currentOutBuffer[output_col + 2] = limit[limitOffset + FAST_INTEGER_IRIGHT_SHIFT(tmp2 + tmp5, FAST_INTEGER_PASS1_BITS + 3) & RANGE_MASK];
            currentOutBuffer[output_col + 5] = limit[limitOffset + FAST_INTEGER_IRIGHT_SHIFT(tmp2 - tmp5, FAST_INTEGER_PASS1_BITS + 3) & RANGE_MASK];
            currentOutBuffer[output_col + 3] = limit[limitOffset + FAST_INTEGER_IRIGHT_SHIFT(tmp3 + tmp4, FAST_INTEGER_PASS1_BITS + 3) & RANGE_MASK];
            currentOutBuffer[output_col + 4] = limit[limitOffset + FAST_INTEGER_IRIGHT_SHIFT(tmp3 - tmp4, FAST_INTEGER_PASS1_BITS + 3) & RANGE_MASK];

            /* advance pointer to next row */
            workspaceIndex += JpegConstants.DCTSIZE;
        }
    }

    /// <summary>
    /// Multiply a DCTELEM variable by an int constant, and immediately
    /// descale to yield a DCTELEM result.
    /// </summary>
    private static int FAST_INTEGER_MULTIPLY(int var, int c)
    {
#if !USE_ACCURATE_ROUNDING
        return (var * c) >> FAST_INTEGER_CONST_BITS;
#else
        return (JpegUtils.DESCALE(var * c, FAST_INTEGER_CONST_BITS));
#endif
    }

    /// <summary>
    /// Dequantize a coefficient by multiplying it by the multiplier-table
    /// entry; produce a DCTELEM result.  For 8-bit data a 16x16->16
    /// multiplication will do.  For 12-bit data, the multiplier table is
    /// declared int, so a 32-bit multiply will be used.
    /// </summary>
    private static int FAST_INTEGER_DEQUANTIZE(short coef, int quantval)
    {
        return ((int)coef * quantval);
    }

    /// <summary>
    /// Like DESCALE, but applies to a DCTELEM and produces an int.
    /// We assume that int right shift is unsigned if int right shift is.
    /// </summary>
    private static int FAST_INTEGER_IRIGHT_SHIFT(int x, int shft)
    {
        return (x >> shft);
    }

    /// <summary>
    /// Perform dequantization and inverse DCT on one block of coefficients.
    /// NOTE: this code only copes with 8x8 DCTs.
    /// 
    /// A floating-point implementation of the
    /// inverse DCT (Discrete Cosine Transform).  In the IJG code, this routine
    /// must also perform dequantization of the input coefficients.
    /// 
    /// This implementation should be more accurate than either of the integer
    /// IDCT implementations.  However, it may not give the same results on all
    /// machines because of differences in roundoff behavior.  Speed will depend
    /// on the hardware's floating point capacity.
    /// 
    /// A 2-D IDCT can be done by 1-D IDCT on each column followed by 1-D IDCT
    /// on each row (or vice versa, but it's more convenient to emit a row at
    /// a time).  Direct algorithms are also available, but they are much more
    /// complex and seem not to be any faster when reduced to code.
    /// 
    /// This implementation is based on Arai, Agui, and Nakajima's algorithm for
    /// scaled DCT.  Their original paper (Trans. IEICE E-71(11):1095) is in
    /// Japanese, but the algorithm is described in the Pennebaker &amp; Mitchell
    /// JPEG textbook (see REFERENCES section in file README).  The following code
    /// is based directly on figure 4-8 in P&amp;M.
    /// While an 8-point DCT cannot be done in less than 11 multiplies, it is
    /// possible to arrange the computation so that many of the multiplies are
    /// simple scalings of the final outputs.  These multiplies can then be
    /// folded into the multiplications or divisions by the JPEG quantization
    /// table entries.  The AA&amp;N method leaves only 5 multiplies and 29 adds
    /// to be done in the DCT itself.
    /// The primary disadvantage of this method is that with a fixed-point
    /// implementation, accuracy is lost due to imprecise representation of the
    /// scaled quantization values.  However, that problem does not arise if
    /// we use floating point arithmetic.
    /// </summary>
    private void jpeg_idct_float(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        /* Pass 1: process columns from input, store into work array. */

        int coefBlockIndex = 0;
        int workspaceIndex = 0;

        float[] quantTable = m_dctTables[component_index].float_array;
        int quantTableIndex = 0;

        for (int ctr = JpegConstants.DCTSIZE; ctr > 0; ctr--)
        {
            /* Due to quantization, we will usually find that many of the input
            * coefficients are zero, especially the AC terms.  We can exploit this
            * by short-circuiting the IDCT calculation for any column in which all
            * the AC terms are zero.  In that case each output is equal to the
            * DC coefficient (with scale factor as needed).
            * With typical images and quantization tables, half or more of the
            * column DCT calculations can be simplified this way.
            */

            if (coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 2] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 4] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 6] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7] == 0)
            {
                /* AC terms all zero */
                float dcval = FLOAT_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 0],
                    quantTable[quantTableIndex + JpegConstants.DCTSIZE * 0]);

                m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 0] = dcval;
                m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 1] = dcval;
                m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 2] = dcval;
                m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 3] = dcval;
                m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 4] = dcval;
                m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 5] = dcval;
                m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 6] = dcval;
                m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 7] = dcval;

                coefBlockIndex++;            /* advance pointers to next column */
                quantTableIndex++;
                workspaceIndex++;
                continue;
            }

            /* Even part */

            float tmp0 = FLOAT_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 0],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 0]);
            float tmp1 = FLOAT_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 2],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 2]);
            float tmp2 = FLOAT_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 4],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 4]);
            float tmp3 = FLOAT_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 6],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 6]);

            float tmp10 = tmp0 + tmp2;    /* phase 3 */
            float tmp11 = tmp0 - tmp2;

            float tmp13 = tmp1 + tmp3;    /* phases 5-3 */
            float tmp12 = (tmp1 - tmp3) * 1.414213562f - tmp13; /* 2*c4 */

            tmp0 = tmp10 + tmp13;   /* phase 2 */
            tmp3 = tmp10 - tmp13;
            tmp1 = tmp11 + tmp12;
            tmp2 = tmp11 - tmp12;

            /* Odd part */

            float tmp4 = FLOAT_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 1]);
            float tmp5 = FLOAT_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 3]);
            float tmp6 = FLOAT_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 5]);
            float tmp7 = FLOAT_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 7]);

            float z13 = tmp6 + tmp5;      /* phase 6 */
            float z10 = tmp6 - tmp5;
            float z11 = tmp4 + tmp7;
            float z12 = tmp4 - tmp7;

            tmp7 = z11 + z13;       /* phase 5 */
            tmp11 = (z11 - z13) * 1.414213562f; /* 2*c4 */

            float z5 = (z10 + z12) * 1.847759065f; /* 2*c2 */
            tmp10 = z5 - z12 * 1.082392200f; /* 2*(c2-c6) */
            tmp12 = z5 - z10 * 2.613125930f; /* 2*(c2+c6) */

            tmp6 = tmp12 - tmp7;    /* phase 2 */
            tmp5 = tmp11 - tmp6;
            tmp4 = tmp10 - tmp5;

            m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 0] = tmp0 + tmp7;
            m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 7] = tmp0 - tmp7;
            m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 1] = tmp1 + tmp6;
            m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 6] = tmp1 - tmp6;
            m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 2] = tmp2 + tmp5;
            m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 5] = tmp2 - tmp5;
            m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 3] = tmp3 + tmp4;
            m_fworkspace[workspaceIndex + JpegConstants.DCTSIZE * 4] = tmp3 - tmp4;

            coefBlockIndex++;            /* advance pointers to next column */
            quantTableIndex++;
            workspaceIndex++;
        }

        /* Pass 2: process rows from work array, store into output array. */
        workspaceIndex = 0;
        byte[] limit = m_cinfo.m_sample_range_limit;
        int limitOffset = m_cinfo.m_sampleRangeLimitOffset - RANGE_SUBSET;

        for (int ctr = 0; ctr < JpegConstants.DCTSIZE; ctr++)
        {
            /* Rows of zeroes can be exploited in the same way as we did with columns.
            * However, the column calculation has created many nonzero AC terms, so
            * the simplification applies less often (typically 5% to 10% of the time).
            * And testing floats for zero is relatively expensive, so we don't bother.
            */

            /* Even part */

            /* Prepare range-limit and float->int conversion */
            float z5 = m_fworkspace[workspaceIndex + 0] + (RANGE_CENTER + 0.5f);
            float tmp10 = z5 + m_fworkspace[workspaceIndex + 4];
            float tmp11 = z5 - m_fworkspace[workspaceIndex + 4];

            float tmp13 = m_fworkspace[workspaceIndex + 2] + m_fworkspace[workspaceIndex + 6];
            float tmp12 = (m_fworkspace[workspaceIndex + 2] - m_fworkspace[workspaceIndex + 6]) * 1.414213562f - tmp13; /* 2*c4 */

            float tmp0 = tmp10 + tmp13;
            float tmp3 = tmp10 - tmp13;
            float tmp1 = tmp11 + tmp12;
            float tmp2 = tmp11 - tmp12;

            /* Odd part */

            float z13 = m_fworkspace[workspaceIndex + 5] + m_fworkspace[workspaceIndex + 3];
            float z10 = m_fworkspace[workspaceIndex + 5] - m_fworkspace[workspaceIndex + 3];
            float z11 = m_fworkspace[workspaceIndex + 1] + m_fworkspace[workspaceIndex + 7];
            float z12 = m_fworkspace[workspaceIndex + 1] - m_fworkspace[workspaceIndex + 7];

            float tmp7 = z11 + z13;     /* phase 5 */
            tmp11 = (z11 - z13) * 1.414213562f; /* 2*c4 */

            z5 = (z10 + z12) * 1.847759065f; /* 2*c2 */
            tmp10 = z5 - z12 * 1.082392200f; /* 2*(c2-c6) */
            tmp12 = z5 - z10 * 2.613125930f; /* 2*(c2+c6) */

            float tmp6 = tmp12 - tmp7;      /* phase 2 */
            float tmp5 = tmp11 - tmp6;
            float tmp4 = tmp10 - tmp5;

            /* Final output stage: float->int conversion and range-limit */
            int currentOutRow = output_row + ctr;
            var currentOutBuffer = m_componentBuffer[currentOutRow];
            currentOutBuffer[output_col + 0] = limit[limitOffset + (int)(tmp0 + tmp7) & RANGE_MASK];
            currentOutBuffer[output_col + 7] = limit[limitOffset + (int)(tmp0 - tmp7) & RANGE_MASK];
            currentOutBuffer[output_col + 1] = limit[limitOffset + (int)(tmp1 + tmp6) & RANGE_MASK];
            currentOutBuffer[output_col + 6] = limit[limitOffset + (int)(tmp1 - tmp6) & RANGE_MASK];
            currentOutBuffer[output_col + 2] = limit[limitOffset + (int)(tmp2 + tmp5) & RANGE_MASK];
            currentOutBuffer[output_col + 5] = limit[limitOffset + (int)(tmp2 - tmp5) & RANGE_MASK];
            currentOutBuffer[output_col + 3] = limit[limitOffset + (int)(tmp3 + tmp4) & RANGE_MASK];
            currentOutBuffer[output_col + 4] = limit[limitOffset + (int)(tmp3 - tmp4) & RANGE_MASK];

            workspaceIndex += JpegConstants.DCTSIZE;       /* advance pointer to next row */
        }
    }

    /// <summary>
    /// Dequantize a coefficient by multiplying it by the multiplier-table
    /// entry; produce a float result.
    /// </summary>
    private static float FLOAT_DEQUANTIZE(short coef, float quantval)
    {
        return (((float)(coef)) * (quantval));
    }

    /// <summary>
    /// Inverse-DCT routines that produce reduced-size output:
    /// either 4x4, 2x2, or 1x1 pixels from an 8x8 DCT block.
    /// 
    /// NOTE: this code only copes with 8x8 DCTs.
    /// 
    /// The implementation is based on the Loeffler, Ligtenberg and Moschytz (LL&amp;M)
    /// algorithm. We simply replace each 8-to-8 1-D IDCT step
    /// with an 8-to-4 step that produces the four averages of two adjacent outputs
    /// (or an 8-to-2 step producing two averages of four outputs, for 2x2 output).
    /// These steps were derived by computing the corresponding values at the end
    /// of the normal LL&amp;M code, then simplifying as much as possible.
    /// 
    /// 1x1 is trivial: just take the DC coefficient divided by 8.
    /// 
    /// Perform dequantization and inverse DCT on one block of coefficients,
    /// producing a reduced-size 4x4 output block.
    /// </summary>
    private void jpeg_idct_4x4(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        /* Pass 1: process columns from input, store into work array. */
        int coefBlockIndex = 0;
        int workspaceIndex = 0;

        int[] quantTable = m_dctTables[component_index].int_array;
        int quantTableIndex = 0;

        for (int ctr = JpegConstants.DCTSIZE; ctr > 0; coefBlockIndex++, quantTableIndex++, workspaceIndex++, ctr--)
        {
            /* Don't bother to process column 4, because second pass won't use it */
            if (ctr == JpegConstants.DCTSIZE - 4)
            {
                continue;
            }

            if (coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 2] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 6] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7] == 0)
            {
                /* AC terms all zero; we need not examine term 4 for 4x4 output */
                int dcval = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 0],
                    quantTable[quantTableIndex + JpegConstants.DCTSIZE * 0]) << REDUCED_PASS1_BITS;

                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 0] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 1] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 2] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 3] = dcval;

                continue;
            }

            /* Even part */

            int tmp0 = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 0],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 0]);
            tmp0 <<= (REDUCED_CONST_BITS + 1);

            int z2 = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 2],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 2]);
            int z3 = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 6],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 6]);

            int tmp2 = z2 * REDUCED_FIX_1_847759065 + z3 * (-REDUCED_FIX_0_765366865);

            int tmp10 = tmp0 + tmp2;
            int tmp12 = tmp0 - tmp2;

            /* Odd part */

            int z1 = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 7]);
            z2 = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 5]);
            z3 = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 3]);
            int z4 = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 1]);

            tmp0 = z1 * (-REDUCED_FIX_0_211164243) /* sqrt(2) * (c3-c1) */ +
                   z2 * REDUCED_FIX_1_451774981 /* sqrt(2) * (c3+c7) */ +
                   z3 * (-REDUCED_FIX_2_172734803) /* sqrt(2) * (-c1-c5) */ +
                   z4 * REDUCED_FIX_1_061594337; /* sqrt(2) * (c5+c7) */

            tmp2 = z1 * (-REDUCED_FIX_0_509795579) /* sqrt(2) * (c7-c5) */ +
                   z2 * (-REDUCED_FIX_0_601344887) /* sqrt(2) * (c5-c1) */ +
                   z3 * REDUCED_FIX_0_899976223 /* sqrt(2) * (c3-c7) */ +
                   z4 * REDUCED_FIX_2_562915447; /* sqrt(2) * (c1+c3) */

            /* Final output stage */

            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 0] = JpegUtils.DESCALE(tmp10 + tmp2, REDUCED_CONST_BITS - REDUCED_PASS1_BITS + 1);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 3] = JpegUtils.DESCALE(tmp10 - tmp2, REDUCED_CONST_BITS - REDUCED_PASS1_BITS + 1);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 1] = JpegUtils.DESCALE(tmp12 + tmp0, REDUCED_CONST_BITS - REDUCED_PASS1_BITS + 1);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 2] = JpegUtils.DESCALE(tmp12 - tmp0, REDUCED_CONST_BITS - REDUCED_PASS1_BITS + 1);
        }

        /* Pass 2: process 4 rows from work array, store into output array. */
        byte[] limit = m_cinfo.m_sample_range_limit;
        int limitOffset = m_cinfo.m_sampleRangeLimitOffset - RANGE_SUBSET;

        workspaceIndex = 0;
        for (int ctr = 0; ctr < 4; ctr++)
        {
            int currentOutRow = output_row + ctr;
            /* It's not clear whether a zero row test is worthwhile here ... */
            var currentOutBuffer = m_componentBuffer[currentOutRow];
            if (m_workspace[workspaceIndex + 1] == 0 &&
                m_workspace[workspaceIndex + 2] == 0 &&
                m_workspace[workspaceIndex + 3] == 0 &&
                m_workspace[workspaceIndex + 5] == 0 &&
                m_workspace[workspaceIndex + 6] == 0 &&
                m_workspace[workspaceIndex + 7] == 0)
            {
                /* AC terms all zero */
                byte dcval = limit[limitOffset + JpegUtils.DESCALE(m_workspace[workspaceIndex + 0], REDUCED_PASS1_BITS + 3) & RANGE_MASK];

                currentOutBuffer[output_col + 0] = dcval;
                currentOutBuffer[output_col + 1] = dcval;
                currentOutBuffer[output_col + 2] = dcval;
                currentOutBuffer[output_col + 3] = dcval;

                workspaceIndex += JpegConstants.DCTSIZE;       /* advance pointer to next row */
                continue;
            }

            /* Even part */

            int tmp0 = (m_workspace[workspaceIndex + 0]) << (REDUCED_CONST_BITS + 1);

            int tmp2 = m_workspace[workspaceIndex + 2] * REDUCED_FIX_1_847759065 + m_workspace[workspaceIndex + 6] * (-REDUCED_FIX_0_765366865);

            int tmp10 = tmp0 + tmp2;
            int tmp12 = tmp0 - tmp2;

            /* Odd part */

            int z1 = m_workspace[workspaceIndex + 7];
            int z2 = m_workspace[workspaceIndex + 5];
            int z3 = m_workspace[workspaceIndex + 3];
            int z4 = m_workspace[workspaceIndex + 1];

            tmp0 = z1 * (-REDUCED_FIX_0_211164243) /* sqrt(2) * (c3-c1) */ +
                   z2 * REDUCED_FIX_1_451774981 /* sqrt(2) * (c3+c7) */ +
                   z3 * (-REDUCED_FIX_2_172734803) /* sqrt(2) * (-c1-c5) */ +
                   z4 * REDUCED_FIX_1_061594337; /* sqrt(2) * (c5+c7) */

            tmp2 = z1 * (-REDUCED_FIX_0_509795579) /* sqrt(2) * (c7-c5) */ +
                   z2 * (-REDUCED_FIX_0_601344887) /* sqrt(2) * (c5-c1) */ +
                   z3 * REDUCED_FIX_0_899976223 /* sqrt(2) * (c3-c7) */ +
                   z4 * REDUCED_FIX_2_562915447; /* sqrt(2) * (c1+c3) */

            /* Final output stage */

            currentOutBuffer[output_col + 0] = limit[limitOffset + JpegUtils.DESCALE(tmp10 + tmp2, REDUCED_CONST_BITS + REDUCED_PASS1_BITS + 3 + 1) & RANGE_MASK];
            currentOutBuffer[output_col + 3] = limit[limitOffset + JpegUtils.DESCALE(tmp10 - tmp2, REDUCED_CONST_BITS + REDUCED_PASS1_BITS + 3 + 1) & RANGE_MASK];
            currentOutBuffer[output_col + 1] = limit[limitOffset + JpegUtils.DESCALE(tmp12 + tmp0, REDUCED_CONST_BITS + REDUCED_PASS1_BITS + 3 + 1) & RANGE_MASK];
            currentOutBuffer[output_col + 2] = limit[limitOffset + JpegUtils.DESCALE(tmp12 - tmp0, REDUCED_CONST_BITS + REDUCED_PASS1_BITS + 3 + 1) & RANGE_MASK];

            workspaceIndex += JpegConstants.DCTSIZE;       /* advance pointer to next row */
        }
    }

    /// <summary>
    /// Perform dequantization and inverse DCT on one block of coefficients,
    /// producing a reduced-size 2x2 output block.
    /// </summary>
    private void jpeg_idct_2x2(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        /* Pass 1: process columns from input, store into work array. */
        int coefBlockIndex = 0;
        int workspaceIndex = 0;

        int[] quantTable = m_dctTables[component_index].int_array;
        int quantTableIndex = 0;

        for (int ctr = JpegConstants.DCTSIZE; ctr > 0; coefBlockIndex++, quantTableIndex++, workspaceIndex++, ctr--)
        {
            /* Don't bother to process columns 2,4,6 */
            if (ctr == JpegConstants.DCTSIZE - 2 || ctr == JpegConstants.DCTSIZE - 4 || ctr == JpegConstants.DCTSIZE - 6)
            {
                continue;
            }

            if (coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7] == 0)
            {
                /* AC terms all zero; we need not examine terms 2,4,6 for 2x2 output */
                int dcval = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 0],
                    quantTable[quantTableIndex + JpegConstants.DCTSIZE * 0]) << REDUCED_PASS1_BITS;

                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 0] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 1] = dcval;

                continue;
            }

            /* Even part */

            int z1 = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 0],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 0]);
            int tmp10 = z1 << (REDUCED_CONST_BITS + 2);

            /* Odd part */

            z1 = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 7]);
            int tmp0 = z1 * -REDUCED_FIX_0_720959822; /* sqrt(2) * (c7-c5+c3-c1) */
            z1 = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 5]);
            tmp0 += z1 * REDUCED_FIX_0_850430095; /* sqrt(2) * (-c1+c3+c5+c7) */
            z1 = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 3]);
            tmp0 += z1 * (-REDUCED_FIX_1_272758580); /* sqrt(2) * (-c1+c3-c5-c7) */
            z1 = REDUCED_DEQUANTIZE(coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1],
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 1]);
            tmp0 += z1 * REDUCED_FIX_3_624509785; /* sqrt(2) * (c1+c3+c5+c7) */

            /* Final output stage */

            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 0] = JpegUtils.DESCALE(tmp10 + tmp0, REDUCED_CONST_BITS - REDUCED_PASS1_BITS + 2);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 1] = JpegUtils.DESCALE(tmp10 - tmp0, REDUCED_CONST_BITS - REDUCED_PASS1_BITS + 2);
        }

        /* Pass 2: process 2 rows from work array, store into output array. */
        workspaceIndex = 0;
        byte[] limit = m_cinfo.m_sample_range_limit;
        int limitOffset = m_cinfo.m_sampleRangeLimitOffset - RANGE_SUBSET;

        for (int ctr = 0; ctr < 2; ctr++)
        {
            int currentOutRow = output_row + ctr;
            /* It's not clear whether a zero row test is worthwhile here ... */
            var currentOutBuffer = m_componentBuffer[currentOutRow];
            if (m_workspace[workspaceIndex + 1] == 0 &&
                m_workspace[workspaceIndex + 3] == 0 &&
                m_workspace[workspaceIndex + 5] == 0 &&
                m_workspace[workspaceIndex + 7] == 0)
            {
                /* AC terms all zero */
                byte dcval = limit[limitOffset + JpegUtils.DESCALE(m_workspace[workspaceIndex + 0], REDUCED_PASS1_BITS + 3) & RANGE_MASK];

                currentOutBuffer[output_col + 0] = dcval;
                currentOutBuffer[output_col + 1] = dcval;

                workspaceIndex += JpegConstants.DCTSIZE;       /* advance pointer to next row */
                continue;
            }

            /* Even part */

            int tmp10 = (m_workspace[workspaceIndex + 0]) << (REDUCED_CONST_BITS + 2);

            /* Odd part */

            int tmp0 = m_workspace[workspaceIndex + 7] * (-REDUCED_FIX_0_720959822) /* sqrt(2) * (c7-c5+c3-c1) */ +
                   m_workspace[workspaceIndex + 5] * REDUCED_FIX_0_850430095 /* sqrt(2) * (-c1+c3+c5+c7) */ +
                   m_workspace[workspaceIndex + 3] * (-REDUCED_FIX_1_272758580) /* sqrt(2) * (-c1+c3-c5-c7) */ +
                   m_workspace[workspaceIndex + 1] * REDUCED_FIX_3_624509785; /* sqrt(2) * (c1+c3+c5+c7) */

            /* Final output stage */

            currentOutBuffer[output_col + 0] = limit[limitOffset + JpegUtils.DESCALE(tmp10 + tmp0, REDUCED_CONST_BITS + REDUCED_PASS1_BITS + 3 + 2) & RANGE_MASK];
            currentOutBuffer[output_col + 1] = limit[limitOffset + JpegUtils.DESCALE(tmp10 - tmp0, REDUCED_CONST_BITS + REDUCED_PASS1_BITS + 3 + 2) & RANGE_MASK];

            workspaceIndex += JpegConstants.DCTSIZE;       /* advance pointer to next row */
        }
    }

    /// <summary>
    /// Perform dequantization and inverse DCT on one block of coefficients,
    /// producing a reduced-size 1x1 output block.
    /// </summary>
    private void jpeg_idct_1x1(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        /* We hardly need an inverse DCT routine for this: just take the
        * average pixel value, which is one-eighth of the DC coefficient.
        */
        int[] quantptr = m_dctTables[component_index].int_array;
        int dcval = REDUCED_DEQUANTIZE(coef_block[0], quantptr[0]);
        dcval = JpegUtils.DESCALE(dcval, 3);

        byte[] limit = m_cinfo.m_sample_range_limit;
        int limitOffset = m_cinfo.m_sampleRangeLimitOffset - RANGE_SUBSET;

        m_componentBuffer[output_row + 0][output_col] = limit[limitOffset + dcval & RANGE_MASK];
    }

    /// <summary>
    /// Dequantize a coefficient by multiplying it by the multiplier-table
    /// entry; produce an int result.  In this module, both inputs and result
    /// are 16 bits or less, so either int or short multiply will work.
    /// </summary>
    private static int REDUCED_DEQUANTIZE(short coef, int quantval)
    {
        return ((int)coef * quantval);
    }

    private void jpeg_idct_3x3(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_5x5(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_6x6(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_7x7(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_9x9(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_10x10(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_11x11(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_12x12(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_13x13(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_14x14(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_15x15(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_16x16(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        /* Pass 1: process columns from input, store into work array. */
        int coefBlockIndex = 0;

        int[] quantTable = m_dctTables[component_index].int_array;
        int quantTableIndex = 0;

        int workspaceIndex = 0;

        for (int ctr = 0; ctr < 8; ctr++)
        {
            /* Even part */
            int tmp0 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 0] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 0];
            tmp0 <<= SLOW_INTEGER_CONST_BITS;
            /* Add fudge factor here for final descale. */
            tmp0 += 1 << (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS - 1);

            int z1 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 4] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 4];
            int tmp1 = z1 * SLOW_INTEGER_FIX(1.306562965);      /* c4[16] = c2[8] */
            int tmp2 = z1 * SLOW_INTEGER_FIX_0_541196100;       /* c12[16] = c6[8] */

            int tmp10 = tmp0 + tmp1;
            int tmp11 = tmp0 - tmp1;
            int tmp12 = tmp0 + tmp2;
            int tmp13 = tmp0 - tmp2;

            z1 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 2] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 2];
            int z2 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 6] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 6];
            int z3 = z1 - z2;
            int z4 = z3 * SLOW_INTEGER_FIX(0.275899379);        /* c14[16] = c7[8] */
            z3 *= SLOW_INTEGER_FIX(1.387039845);        /* c2[16] = c1[8] */

            tmp0 = z3 + z2 * SLOW_INTEGER_FIX_2_562915447;  /* (c6+c2)[16] = (c3+c1)[8] */
            tmp1 = z4 + z1 * SLOW_INTEGER_FIX_0_899976223;  /* (c6-c14)[16] = (c3-c7)[8] */
            tmp2 = z3 - z1 * SLOW_INTEGER_FIX(0.601344887); /* (c2-c10)[16] = (c1-c5)[8] */
            int tmp3 = z4 - z2 * SLOW_INTEGER_FIX(0.509795579); /* (c10-c14)[16] = (c5-c7)[8] */

            int tmp20 = tmp10 + tmp0;
            int tmp27 = tmp10 - tmp0;
            int tmp21 = tmp12 + tmp1;
            int tmp26 = tmp12 - tmp1;
            int tmp22 = tmp13 + tmp2;
            int tmp25 = tmp13 - tmp2;
            int tmp23 = tmp11 + tmp3;
            int tmp24 = tmp11 - tmp3;

            /* Odd part */

            z1 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 1];
            z2 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 3];
            z3 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 5];
            z4 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 7];

            tmp11 = z1 + z3;

            tmp1 = (z1 + z2) * SLOW_INTEGER_FIX(1.353318001);   /* c3 */
            tmp2 = tmp11 * SLOW_INTEGER_FIX(1.247225013);   /* c5 */
            tmp3 = (z1 + z4) * SLOW_INTEGER_FIX(1.093201867);   /* c7 */
            tmp10 = (z1 - z4) * SLOW_INTEGER_FIX(0.897167586);   /* c9 */
            tmp11 *= SLOW_INTEGER_FIX(0.666655658);   /* c11 */
            tmp12 = (z1 - z2) * SLOW_INTEGER_FIX(0.410524528);   /* c13 */
            tmp0 = tmp1 + tmp2 + tmp3 - z1 * SLOW_INTEGER_FIX(2.286341144);        /* c7+c5+c3-c1 */
            tmp13 = tmp10 + tmp11 + tmp12 - z1 * SLOW_INTEGER_FIX(1.835730603);        /* c9+c11+c13-c15 */
            z1 = (z2 + z3) * SLOW_INTEGER_FIX(0.138617169);   /* c15 */
            tmp1 += z1 + z2 * SLOW_INTEGER_FIX(0.071888074);  /* c9+c11-c3-c15 */
            tmp2 += z1 - z3 * SLOW_INTEGER_FIX(1.125726048);  /* c5+c7+c15-c3 */
            z1 = (z3 - z2) * SLOW_INTEGER_FIX(1.407403738);   /* c1 */
            tmp11 += z1 - z3 * SLOW_INTEGER_FIX(0.766367282);  /* c1+c11-c9-c13 */
            tmp12 += z1 + z2 * SLOW_INTEGER_FIX(1.971951411);  /* c1+c5+c13-c7 */
            z2 += z4;
            z1 = z2 * (-SLOW_INTEGER_FIX(0.666655658));      /* -c11 */
            tmp1 += z1;
            tmp3 += z1 + z4 * SLOW_INTEGER_FIX(1.065388962);  /* c3+c11+c15-c7 */
            z2 *= (-SLOW_INTEGER_FIX(1.247225013));      /* -c5 */
            tmp10 += z2 + z4 * SLOW_INTEGER_FIX(3.141271809);  /* c1+c5+c9-c13 */
            tmp12 += z2;
            z2 = (z3 + z4) * (-SLOW_INTEGER_FIX(1.353318001)); /* -c3 */
            tmp2 += z2;
            tmp3 += z2;
            z2 = (z4 - z3) * SLOW_INTEGER_FIX(0.410524528);   /* c13 */
            tmp10 += z2;
            tmp11 += z2;

            /* Final output stage */

            m_workspace[workspaceIndex + 8 * 0] = (tmp20 + tmp0) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 15] = (tmp20 - tmp0) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 1] = (tmp21 + tmp1) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 14] = (tmp21 - tmp1) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 2] = (tmp22 + tmp2) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 13] = (tmp22 - tmp2) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 3] = (tmp23 + tmp3) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 12] = (tmp23 - tmp3) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 4] = (tmp24 + tmp10) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 11] = (tmp24 - tmp10) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 5] = (tmp25 + tmp11) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 10] = (tmp25 - tmp11) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 6] = (tmp26 + tmp12) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 9] = (tmp26 - tmp12) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 7] = (tmp27 + tmp13) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 8] = (tmp27 - tmp13) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);

            /* advance pointers to next column */
            coefBlockIndex++;
            quantTableIndex++;
            workspaceIndex++;
        }

        /* Pass 2: process 16 rows from work array, store into output array. */

        workspaceIndex = 0;
        byte[] limit = m_cinfo.m_sample_range_limit;
        int limitOffset = m_cinfo.m_sampleRangeLimitOffset - RANGE_SUBSET;

        for (int ctr = 0; ctr < 16; ctr++)
        {
            /* Even part */

            /* Add range center and fudge factor for final descale and range-limit. */
            int tmp0 = m_workspace[workspaceIndex + 0] +
                (RANGE_CENTER << (SLOW_INTEGER_PASS1_BITS + 3)) +
                (1 << (SLOW_INTEGER_PASS1_BITS + 2));
            tmp0 <<= SLOW_INTEGER_CONST_BITS;

            int z1 = m_workspace[workspaceIndex + 4];
            int tmp1 = z1 * SLOW_INTEGER_FIX(1.306562965);      /* c4[16] = c2[8] */
            int tmp2 = z1 * SLOW_INTEGER_FIX_0_541196100;       /* c12[16] = c6[8] */

            int tmp10 = tmp0 + tmp1;
            int tmp11 = tmp0 - tmp1;
            int tmp12 = tmp0 + tmp2;
            int tmp13 = tmp0 - tmp2;

            z1 = m_workspace[workspaceIndex + 2];
            int z2 = m_workspace[workspaceIndex + 6];
            int z3 = z1 - z2;
            int z4 = z3 * SLOW_INTEGER_FIX(0.275899379);        /* c14[16] = c7[8] */
            z3 *= SLOW_INTEGER_FIX(1.387039845);        /* c2[16] = c1[8] */

            tmp0 = z3 + z2 * SLOW_INTEGER_FIX_2_562915447;  /* (c6+c2)[16] = (c3+c1)[8] */
            tmp1 = z4 + z1 * SLOW_INTEGER_FIX_0_899976223;  /* (c6-c14)[16] = (c3-c7)[8] */
            tmp2 = z3 - z1 * SLOW_INTEGER_FIX(0.601344887); /* (c2-c10)[16] = (c1-c5)[8] */
            int tmp3 = z4 - z2 * SLOW_INTEGER_FIX(0.509795579); /* (c10-c14)[16] = (c5-c7)[8] */

            int tmp20 = tmp10 + tmp0;
            int tmp27 = tmp10 - tmp0;
            int tmp21 = tmp12 + tmp1;
            int tmp26 = tmp12 - tmp1;
            int tmp22 = tmp13 + tmp2;
            int tmp25 = tmp13 - tmp2;
            int tmp23 = tmp11 + tmp3;
            int tmp24 = tmp11 - tmp3;

            /* Odd part */

            z1 = m_workspace[workspaceIndex + 1];
            z2 = m_workspace[workspaceIndex + 3];
            z3 = m_workspace[workspaceIndex + 5];
            z4 = m_workspace[workspaceIndex + 7];

            tmp11 = z1 + z3;

            tmp1 = (z1 + z2) * SLOW_INTEGER_FIX(1.353318001);   /* c3 */
            tmp2 = tmp11 * SLOW_INTEGER_FIX(1.247225013);   /* c5 */
            tmp3 = (z1 + z4) * SLOW_INTEGER_FIX(1.093201867);   /* c7 */
            tmp10 = (z1 - z4) * SLOW_INTEGER_FIX(0.897167586);   /* c9 */
            tmp11 *= SLOW_INTEGER_FIX(0.666655658);   /* c11 */
            tmp12 = (z1 - z2) * SLOW_INTEGER_FIX(0.410524528);   /* c13 */
            tmp0 = tmp1 + tmp2 + tmp3 - z1 * SLOW_INTEGER_FIX(2.286341144);        /* c7+c5+c3-c1 */
            tmp13 = tmp10 + tmp11 + tmp12 - z1 * SLOW_INTEGER_FIX(1.835730603);        /* c9+c11+c13-c15 */
            z1 = (z2 + z3) * SLOW_INTEGER_FIX(0.138617169);   /* c15 */
            tmp1 += z1 + z2 * SLOW_INTEGER_FIX(0.071888074);  /* c9+c11-c3-c15 */
            tmp2 += z1 - z3 * SLOW_INTEGER_FIX(1.125726048);  /* c5+c7+c15-c3 */
            z1 = (z3 - z2) * SLOW_INTEGER_FIX(1.407403738);   /* c1 */
            tmp11 += z1 - z3 * SLOW_INTEGER_FIX(0.766367282);  /* c1+c11-c9-c13 */
            tmp12 += z1 + z2 * SLOW_INTEGER_FIX(1.971951411);  /* c1+c5+c13-c7 */
            z2 += z4;
            z1 = z2 * (-SLOW_INTEGER_FIX(0.666655658));      /* -c11 */
            tmp1 += z1;
            tmp3 += z1 + z4 * SLOW_INTEGER_FIX(1.065388962);  /* c3+c11+c15-c7 */
            z2 *= (-SLOW_INTEGER_FIX(1.247225013));      /* -c5 */
            tmp10 += z2 + z4 * SLOW_INTEGER_FIX(3.141271809);  /* c1+c5+c9-c13 */
            tmp12 += z2;
            z2 = (z3 + z4) * (-SLOW_INTEGER_FIX(1.353318001)); /* -c3 */
            tmp2 += z2;
            tmp3 += z2;
            z2 = (z4 - z3) * SLOW_INTEGER_FIX(0.410524528);   /* c13 */
            tmp10 += z2;
            tmp11 += z2;

            /* Final output stage */
            int currentOutRow = output_row + ctr;
            var currentOutBuffer = m_componentBuffer[currentOutRow];
            currentOutBuffer[output_col + 0] = limit[limitOffset +
                ((tmp20 + tmp0) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 15] = limit[limitOffset +
                ((tmp20 - tmp0) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 1] = limit[limitOffset +
                ((tmp21 + tmp1) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 14] = limit[limitOffset +
                ((tmp21 - tmp1) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 2] = limit[limitOffset +
                ((tmp22 + tmp2) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 13] = limit[limitOffset +
                ((tmp22 - tmp2) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 3] = limit[limitOffset +
                ((tmp23 + tmp3) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 12] = limit[limitOffset +
                ((tmp23 - tmp3) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 4] = limit[limitOffset +
                ((tmp24 + tmp10) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 11] = limit[limitOffset +
                ((tmp24 - tmp10) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 5] = limit[limitOffset +
                ((tmp25 + tmp11) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 10] = limit[limitOffset +
                ((tmp25 - tmp11) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 6] = limit[limitOffset +
                ((tmp26 + tmp12) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 9] = limit[limitOffset +
                ((tmp26 - tmp12) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 7] = limit[limitOffset +
                ((tmp27 + tmp13) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 8] = limit[limitOffset +
                ((tmp27 - tmp13) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];

            workspaceIndex += 8;		/* advance pointer to next row */
        }
    }

    private void jpeg_idct_16x8(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        /* Pass 1: process columns from input, store into work array. */
        /* Note results are scaled up by sqrt(8) compared to a true IDCT; */
        /* furthermore, we scale the results by 2**SLOW_INTEGER_PASS1_BITS. */
        /* 8 - point IDCT kernel, cK represents sqrt(2) * cos(K * pi / 16). */

        int coefBlockIndex = 0;

        int[] quantTable = m_dctTables[component_index].int_array;
        int quantTableIndex = 0;

        int workspaceIndex = 0;

        for (int ctr = JpegConstants.DCTSIZE; ctr > 0; ctr--)
        {
            /* Due to quantization, we will usually find that many of the input
            * coefficients are zero, especially the AC terms.  We can exploit this
            * by short-circuiting the IDCT calculation for any column in which all
            * the AC terms are zero.  In that case each output is equal to the
            * DC coefficient (with scale factor as needed).
            * With typical images and quantization tables, half or more of the
            * column DCT calculations can be simplified this way.
            */

            if (coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 2] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 4] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 6] == 0 &&
                coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7] == 0)
            {
                /* AC terms all zero */
                int dcval = (coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 0] *
                    quantTable[quantTableIndex + JpegConstants.DCTSIZE * 0]) << SLOW_INTEGER_PASS1_BITS;

                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 0] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 1] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 2] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 3] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 4] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 5] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 6] = dcval;
                m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 7] = dcval;

                /* advance pointers to next column */
                coefBlockIndex++;
                quantTableIndex++;
                workspaceIndex++;
                continue;
            }

            /* Even part: reverse the even part of the forward DCT. */
            /* The rotator is c(-6). */

            int z2 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 2] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 2];
            int z3 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 6] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 6];

            int z1 = (z2 + z3) * SLOW_INTEGER_FIX_0_541196100;
            int tmp2 = z1 + z2 * SLOW_INTEGER_FIX_0_765366865;
            int tmp3 = z1 - z3 * SLOW_INTEGER_FIX_1_847759065;

            z2 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 0] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 0];
            z3 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 4] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 4];

            z2 <<= SLOW_INTEGER_CONST_BITS;
            z3 <<= SLOW_INTEGER_CONST_BITS;
            /* Add fudge factor here for final descale. */
            z2 += 1 << (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS - 1);

            int tmp0 = z2 + z3;
            int tmp1 = z2 - z3;

            int tmp10 = tmp0 + tmp2;
            int tmp13 = tmp0 - tmp2;
            int tmp11 = tmp1 + tmp3;
            int tmp12 = tmp1 - tmp3;

            /* Odd part per figure 8; the matrix is unitary and hence its
            * transpose is its inverse.  i0..i3 are y7,y5,y3,y1 respectively.
            */

            tmp0 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 7];
            tmp1 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 5];
            tmp2 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 3];
            tmp3 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 1];

            z2 = tmp0 + tmp2;
            z3 = tmp1 + tmp3;

            z1 = (z2 + z3) * SLOW_INTEGER_FIX_1_175875602;       /*  c3 */
            z2 *= (-SLOW_INTEGER_FIX_1_961570560);          /* -c3-c5 */
            z3 *= (-SLOW_INTEGER_FIX_0_390180644);          /* -c3+c5 */
            z2 += z1;
            z3 += z1;

            z1 = (tmp0 + tmp3) * (-SLOW_INTEGER_FIX_0_899976223); /* -c3+c7 */
            tmp0 *= SLOW_INTEGER_FIX_0_298631336;        /* -c1+c3+c5-c7 */
            tmp3 *= SLOW_INTEGER_FIX_1_501321110;        /*  c1+c3-c5-c7 */
            tmp0 += z1 + z2;
            tmp3 += z1 + z3;

            z1 = (tmp1 + tmp2) * (-SLOW_INTEGER_FIX_2_562915447); /* -c1-c3 */
            tmp1 *= SLOW_INTEGER_FIX_2_053119869;        /*  c1+c3-c5+c7 */
            tmp2 *= SLOW_INTEGER_FIX_3_072711026;        /*  c1+c3+c5-c7 */
            tmp1 += z1 + z3;
            tmp2 += z1 + z2;

            /* Final output stage: inputs are tmp10..tmp13, tmp0..tmp3 */

            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 0] = (tmp10 + tmp3) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 7] = (tmp10 - tmp3) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 1] = (tmp11 + tmp2) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 6] = (tmp11 - tmp2) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 2] = (tmp12 + tmp1) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 5] = (tmp12 - tmp1) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 3] = (tmp13 + tmp0) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + JpegConstants.DCTSIZE * 4] = (tmp13 - tmp0) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);

            /* advance pointers to next column */
            coefBlockIndex++;
            quantTableIndex++;
            workspaceIndex++;
        }

        /* Pass 2: process 8 rows from work array, store into output array.
         * 16-point IDCT kernel, cK represents sqrt(2) * cos(K*pi/32).
         */
        workspaceIndex = 0;
        byte[] limit = m_cinfo.m_sample_range_limit;
        int limitOffset = m_cinfo.m_sampleRangeLimitOffset - RANGE_SUBSET;

        for (int ctr = 0; ctr < 8; ctr++)
        {
            /* Even part */

            /* Add range center and fudge factor for final descale and range-limit. */
            int tmp0 = m_workspace[workspaceIndex + 0] +
                (RANGE_CENTER << (SLOW_INTEGER_PASS1_BITS + 3)) +
                (1 << (SLOW_INTEGER_PASS1_BITS + 2));
            tmp0 <<= SLOW_INTEGER_CONST_BITS;

            int z1 = m_workspace[workspaceIndex + 4];
            int tmp1 = z1 * SLOW_INTEGER_FIX(1.306562965);      /* c4[16] = c2[8] */
            int tmp2 = z1 * SLOW_INTEGER_FIX_0_541196100;       /* c12[16] = c6[8] */

            int tmp10 = tmp0 + tmp1;
            int tmp11 = tmp0 - tmp1;
            int tmp12 = tmp0 + tmp2;
            int tmp13 = tmp0 - tmp2;

            z1 = m_workspace[workspaceIndex + 2];
            int z2 = m_workspace[workspaceIndex + 6];
            int z3 = z1 - z2;
            int z4 = z3 * SLOW_INTEGER_FIX(0.275899379);        /* c14[16] = c7[8] */
            z3 *= SLOW_INTEGER_FIX(1.387039845);        /* c2[16] = c1[8] */

            tmp0 = z3 + z2 * SLOW_INTEGER_FIX_2_562915447;  /* (c6+c2)[16] = (c3+c1)[8] */
            tmp1 = z4 + z1 * SLOW_INTEGER_FIX_0_899976223;  /* (c6-c14)[16] = (c3-c7)[8] */
            tmp2 = z3 - z1 * SLOW_INTEGER_FIX(0.601344887); /* (c2-c10)[16] = (c1-c5)[8] */
            int tmp3 = z4 - z2 * SLOW_INTEGER_FIX(0.509795579); /* (c10-c14)[16] = (c5-c7)[8] */

            int tmp20 = tmp10 + tmp0;
            int tmp27 = tmp10 - tmp0;
            int tmp21 = tmp12 + tmp1;
            int tmp26 = tmp12 - tmp1;
            int tmp22 = tmp13 + tmp2;
            int tmp25 = tmp13 - tmp2;
            int tmp23 = tmp11 + tmp3;
            int tmp24 = tmp11 - tmp3;

            /* Odd part */

            z1 = m_workspace[workspaceIndex + 1];
            z2 = m_workspace[workspaceIndex + 3];
            z3 = m_workspace[workspaceIndex + 5];
            z4 = m_workspace[workspaceIndex + 7];

            tmp11 = z1 + z3;

            tmp1 = (z1 + z2) * SLOW_INTEGER_FIX(1.353318001);   /* c3 */
            tmp2 = tmp11 * SLOW_INTEGER_FIX(1.247225013);   /* c5 */
            tmp3 = (z1 + z4) * SLOW_INTEGER_FIX(1.093201867);   /* c7 */
            tmp10 = (z1 - z4) * SLOW_INTEGER_FIX(0.897167586);   /* c9 */
            tmp11 *= SLOW_INTEGER_FIX(0.666655658);   /* c11 */
            tmp12 = (z1 - z2) * SLOW_INTEGER_FIX(0.410524528);   /* c13 */
            tmp0 = tmp1 + tmp2 + tmp3 - z1 * SLOW_INTEGER_FIX(2.286341144);        /* c7+c5+c3-c1 */
            tmp13 = tmp10 + tmp11 + tmp12 - z1 * SLOW_INTEGER_FIX(1.835730603);        /* c9+c11+c13-c15 */
            z1 = (z2 + z3) * SLOW_INTEGER_FIX(0.138617169);   /* c15 */
            tmp1 += z1 + z2 * SLOW_INTEGER_FIX(0.071888074);  /* c9+c11-c3-c15 */
            tmp2 += z1 - z3 * SLOW_INTEGER_FIX(1.125726048);  /* c5+c7+c15-c3 */
            z1 = (z3 - z2) * SLOW_INTEGER_FIX(1.407403738);   /* c1 */
            tmp11 += z1 - z3 * SLOW_INTEGER_FIX(0.766367282);  /* c1+c11-c9-c13 */
            tmp12 += z1 + z2 * SLOW_INTEGER_FIX(1.971951411);  /* c1+c5+c13-c7 */
            z2 += z4;
            z1 = z2 * (-SLOW_INTEGER_FIX(0.666655658));      /* -c11 */
            tmp1 += z1;
            tmp3 += z1 + z4 * SLOW_INTEGER_FIX(1.065388962);  /* c3+c11+c15-c7 */
            z2 *= (-SLOW_INTEGER_FIX(1.247225013));      /* -c5 */
            tmp10 += z2 + z4 * SLOW_INTEGER_FIX(3.141271809);  /* c1+c5+c9-c13 */
            tmp12 += z2;
            z2 = (z3 + z4) * (-SLOW_INTEGER_FIX(1.353318001)); /* -c3 */
            tmp2 += z2;
            tmp3 += z2;
            z2 = (z4 - z3) * SLOW_INTEGER_FIX(0.410524528);   /* c13 */
            tmp10 += z2;
            tmp11 += z2;

            /* Final output stage */
            int currentOutRow = output_row + ctr;
            var currentOutBuffer = m_componentBuffer[currentOutRow];
            currentOutBuffer[output_col + 0] = limit[limitOffset +
                ((tmp20 + tmp0) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 15] = limit[limitOffset +
                ((tmp20 - tmp0) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 1] = limit[limitOffset +
                ((tmp21 + tmp1) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 14] = limit[limitOffset +
                ((tmp21 - tmp1) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 2] = limit[limitOffset +
                ((tmp22 + tmp2) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 13] = limit[limitOffset +
                ((tmp22 - tmp2) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 3] = limit[limitOffset +
                ((tmp23 + tmp3) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 12] = limit[limitOffset +
                ((tmp23 - tmp3) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 4] = limit[limitOffset +
                ((tmp24 + tmp10) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 11] = limit[limitOffset +
                ((tmp24 - tmp10) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 5] = limit[limitOffset +
                ((tmp25 + tmp11) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 10] = limit[limitOffset +
                ((tmp25 - tmp11) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 6] = limit[limitOffset +
                ((tmp26 + tmp12) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 9] = limit[limitOffset +
                ((tmp26 - tmp12) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 7] = limit[limitOffset +
                ((tmp27 + tmp13) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 8] = limit[limitOffset +
                ((tmp27 - tmp13) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];

            workspaceIndex += 8;		/* advance pointer to next row */
        }
    }

    private void jpeg_idct_14x7(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_12x6(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_10x5(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_8x4(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_6x3(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_4x2(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_2x1(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_8x16(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        /* Pass 1: process columns from input, store into work array. */
        /* 16 - point IDCT kernel, cK represents sqrt(2) * cos(K * pi / 32). */
        int coefBlockIndex = 0;

        int[] quantTable = m_dctTables[component_index].int_array;
        int quantTableIndex = 0;

        int workspaceIndex = 0;

        for (int ctr = 0; ctr < 8; ctr++)
        {
            /* Even part */
            int tmp0 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 0] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 0];
            tmp0 <<= SLOW_INTEGER_CONST_BITS;
            /* Add fudge factor here for final descale. */
            tmp0 += 1 << (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS - 1);

            int z1 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 4] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 4];
            int tmp1 = z1 * SLOW_INTEGER_FIX(1.306562965);      /* c4[16] = c2[8] */
            int tmp2 = z1 * SLOW_INTEGER_FIX_0_541196100;       /* c12[16] = c6[8] */

            int tmp10 = tmp0 + tmp1;
            int tmp11 = tmp0 - tmp1;
            int tmp12 = tmp0 + tmp2;
            int tmp13 = tmp0 - tmp2;

            z1 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 2] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 2];
            int z2 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 6] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 6];
            int z3 = z1 - z2;
            int z4 = z3 * SLOW_INTEGER_FIX(0.275899379);        /* c14[16] = c7[8] */
            z3 *= SLOW_INTEGER_FIX(1.387039845);        /* c2[16] = c1[8] */

            tmp0 = z3 + z2 * SLOW_INTEGER_FIX_2_562915447;  /* (c6+c2)[16] = (c3+c1)[8] */
            tmp1 = z4 + z1 * SLOW_INTEGER_FIX_0_899976223;  /* (c6-c14)[16] = (c3-c7)[8] */
            tmp2 = z3 - z1 * SLOW_INTEGER_FIX(0.601344887); /* (c2-c10)[16] = (c1-c5)[8] */
            int tmp3 = z4 - z2 * SLOW_INTEGER_FIX(0.509795579); /* (c10-c14)[16] = (c5-c7)[8] */

            int tmp20 = tmp10 + tmp0;
            int tmp27 = tmp10 - tmp0;
            int tmp21 = tmp12 + tmp1;
            int tmp26 = tmp12 - tmp1;
            int tmp22 = tmp13 + tmp2;
            int tmp25 = tmp13 - tmp2;
            int tmp23 = tmp11 + tmp3;
            int tmp24 = tmp11 - tmp3;

            /* Odd part */

            z1 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 1] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 1];
            z2 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 3] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 3];
            z3 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 5] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 5];
            z4 = coef_block[coefBlockIndex + JpegConstants.DCTSIZE * 7] *
                quantTable[quantTableIndex + JpegConstants.DCTSIZE * 7];

            tmp11 = z1 + z3;

            tmp1 = (z1 + z2) * SLOW_INTEGER_FIX(1.353318001);   /* c3 */
            tmp2 = tmp11 * SLOW_INTEGER_FIX(1.247225013);   /* c5 */
            tmp3 = (z1 + z4) * SLOW_INTEGER_FIX(1.093201867);   /* c7 */
            tmp10 = (z1 - z4) * SLOW_INTEGER_FIX(0.897167586);   /* c9 */
            tmp11 *= SLOW_INTEGER_FIX(0.666655658);   /* c11 */
            tmp12 = (z1 - z2) * SLOW_INTEGER_FIX(0.410524528);   /* c13 */
            tmp0 = tmp1 + tmp2 + tmp3 - z1 * SLOW_INTEGER_FIX(2.286341144);        /* c7+c5+c3-c1 */
            tmp13 = tmp10 + tmp11 + tmp12 - z1 * SLOW_INTEGER_FIX(1.835730603);        /* c9+c11+c13-c15 */
            z1 = (z2 + z3) * SLOW_INTEGER_FIX(0.138617169);   /* c15 */
            tmp1 += z1 + z2 * SLOW_INTEGER_FIX(0.071888074);  /* c9+c11-c3-c15 */
            tmp2 += z1 - z3 * SLOW_INTEGER_FIX(1.125726048);  /* c5+c7+c15-c3 */
            z1 = (z3 - z2) * SLOW_INTEGER_FIX(1.407403738);   /* c1 */
            tmp11 += z1 - z3 * SLOW_INTEGER_FIX(0.766367282);  /* c1+c11-c9-c13 */
            tmp12 += z1 + z2 * SLOW_INTEGER_FIX(1.971951411);  /* c1+c5+c13-c7 */
            z2 += z4;
            z1 = z2 * (-SLOW_INTEGER_FIX(0.666655658));      /* -c11 */
            tmp1 += z1;
            tmp3 += z1 + z4 * SLOW_INTEGER_FIX(1.065388962);  /* c3+c11+c15-c7 */
            z2 *= (-SLOW_INTEGER_FIX(1.247225013));      /* -c5 */
            tmp10 += z2 + z4 * SLOW_INTEGER_FIX(3.141271809);  /* c1+c5+c9-c13 */
            tmp12 += z2;
            z2 = (z3 + z4) * (-SLOW_INTEGER_FIX(1.353318001)); /* -c3 */
            tmp2 += z2;
            tmp3 += z2;
            z2 = (z4 - z3) * SLOW_INTEGER_FIX(0.410524528);   /* c13 */
            tmp10 += z2;
            tmp11 += z2;

            /* Final output stage */

            m_workspace[workspaceIndex + 8 * 0] = (tmp20 + tmp0) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 15] = (tmp20 - tmp0) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 1] = (tmp21 + tmp1) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 14] = (tmp21 - tmp1) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 2] = (tmp22 + tmp2) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 13] = (tmp22 - tmp2) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 3] = (tmp23 + tmp3) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 12] = (tmp23 - tmp3) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 4] = (tmp24 + tmp10) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 11] = (tmp24 - tmp10) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 5] = (tmp25 + tmp11) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 10] = (tmp25 - tmp11) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 6] = (tmp26 + tmp12) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 9] = (tmp26 - tmp12) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 7] = (tmp27 + tmp13) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);
            m_workspace[workspaceIndex + 8 * 8] = (tmp27 - tmp13) >> (SLOW_INTEGER_CONST_BITS - SLOW_INTEGER_PASS1_BITS);

            /* advance pointers to next column */
            coefBlockIndex++;
            quantTableIndex++;
            workspaceIndex++;
        }

        /* Pass 2: process rows from work array, store into output array.
         * Note that we must descale the results by a factor of 8 == 2**3,
         * and also undo the PASS1_BITS scaling.
         * 8-point IDCT kernel, cK represents sqrt(2) * cos(K*pi/16).
         */
        workspaceIndex = 0;
        byte[] limit = m_cinfo.m_sample_range_limit;
        int limitOffset = m_cinfo.m_sampleRangeLimitOffset - RANGE_SUBSET;

        for (int ctr = 0; ctr < 16; ctr++)
        {
            /* Even part: reverse the even part of the forward DCT.
             * The rotator is c(-6).
             */

            /* Add range center and fudge factor for final descale and range-limit. */
            int z2 = m_workspace[workspaceIndex + 0] +
                (RANGE_CENTER << (SLOW_INTEGER_PASS1_BITS + 3)) +
                (1 << (SLOW_INTEGER_PASS1_BITS + 2));
            int z3 = m_workspace[workspaceIndex + 4];

            int tmp0 = (z2 + z3) << SLOW_INTEGER_CONST_BITS;
            int tmp1 = (z2 - z3) << SLOW_INTEGER_CONST_BITS;

            z2 = m_workspace[workspaceIndex + 2];
            z3 = m_workspace[workspaceIndex + 6];

            int z1 = (z2 + z3) * SLOW_INTEGER_FIX_0_541196100;       /* c6 */
            int tmp2 = z1 + z2 * SLOW_INTEGER_FIX_0_765366865;     /* c2-c6 */
            int tmp3 = z1 - z3 * SLOW_INTEGER_FIX_1_847759065;     /* c2+c6 */

            int tmp10 = tmp0 + tmp2;
            int tmp13 = tmp0 - tmp2;
            int tmp11 = tmp1 + tmp3;
            int tmp12 = tmp1 - tmp3;

            /* Odd part per figure 8; the matrix is unitary and hence its
             * transpose is its inverse.  i0..i3 are y7,y5,y3,y1 respectively.
             */

            tmp0 = m_workspace[workspaceIndex + 7];
            tmp1 = m_workspace[workspaceIndex + 5];
            tmp2 = m_workspace[workspaceIndex + 3];
            tmp3 = m_workspace[workspaceIndex + 1];

            z2 = tmp0 + tmp2;
            z3 = tmp1 + tmp3;

            z1 = (z2 + z3) * SLOW_INTEGER_FIX_1_175875602;       /*  c3 */
            z2 *= (-SLOW_INTEGER_FIX_1_961570560);          /* -c3-c5 */
            z3 *= (-SLOW_INTEGER_FIX_0_390180644);          /* -c3+c5 */
            z2 += z1;
            z3 += z1;

            z1 = (tmp0 + tmp3) * (-SLOW_INTEGER_FIX_0_899976223); /* -c3+c7 */
            tmp0 *= SLOW_INTEGER_FIX_0_298631336;        /* -c1+c3+c5-c7 */
            tmp3 *= SLOW_INTEGER_FIX_1_501321110;        /*  c1+c3-c5-c7 */
            tmp0 += z1 + z2;
            tmp3 += z1 + z3;

            z1 = (tmp1 + tmp2) * (-SLOW_INTEGER_FIX_2_562915447); /* -c1-c3 */
            tmp1 *= SLOW_INTEGER_FIX_2_053119869;        /*  c1+c3-c5+c7 */
            tmp2 *= SLOW_INTEGER_FIX_3_072711026;        /*  c1+c3+c5-c7 */
            tmp1 += z1 + z3;
            tmp2 += z1 + z2;

            /* Final output stage: inputs are tmp10..tmp13, tmp0..tmp3 */
            int currentOutRow = output_row + ctr;
            var currentOutBuffer = m_componentBuffer[currentOutRow];
            currentOutBuffer[output_col + 0] = limit[limitOffset +
                ((tmp10 + tmp3) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 7] = limit[limitOffset +
                ((tmp10 - tmp3) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 1] = limit[limitOffset +
                ((tmp11 + tmp2) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 6] = limit[limitOffset +
                ((tmp11 - tmp2) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 2] = limit[limitOffset +
                ((tmp12 + tmp1) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 5] = limit[limitOffset +
                ((tmp12 - tmp1) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 3] = limit[limitOffset +
                ((tmp13 + tmp0) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];
            currentOutBuffer[output_col + 4] = limit[limitOffset +
                ((tmp13 - tmp0) >> SLOW_INTEGER_PASS1_PLUS3_BITS) & RANGE_MASK];

            workspaceIndex += JpegConstants.DCTSIZE;		/* advance pointer to next row */
        }
    }

    private void jpeg_idct_7x14(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_6x12(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_5x10(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_4x8(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_3x6(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_2x4(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }

    private void jpeg_idct_1x2(int component_index, ReadOnlySpan<short> coef_block, int output_row, int output_col)
    {
        m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_NOTIMPL);
    }
}
