/*
 * This file contains output colorspace conversion routines.
 */

namespace BitMiracle.LibJpeg.Classic.Internal;

/// <summary>
/// Colorspace conversion
/// </summary>
internal sealed class jpeg_color_deconverter
{
    private const int SCALEBITS = 16;  /* speediest right-shift on some machines */
    private const int ONE_HALF = 1 << (SCALEBITS - 1);

    /* We allocate one big table for RGB->Y conversion and divide it up into
     * three parts, instead of doing three alloc_small requests.  This lets us
     * use a single table base address, which can be held in a register in the
     * inner loops on many machines (more than can hold all three addresses,
     * anyway).
     */

    private const int R_Y_OFF = 0; 			/* offset to R => Y section */
    private const int G_Y_OFF = (1 * (JpegConstants.MAXJSAMPLE + 1));	/* offset to G => Y section */
    private const int B_Y_OFF = (2 * (JpegConstants.MAXJSAMPLE + 1));	/* etc. */
    private const int TABLE_SIZE = (3 * (JpegConstants.MAXJSAMPLE + 1));

    private enum ColorConverter : byte
    {
        None = 0,
        YccRgb = 1,
        RgbGray = 2,
        Rgb1Rgb = 3,
        Rgb1Gray = 4,
        Rgb = 5,
        YcckCmyk = 6,
        GrayRgb = 7,
        Grayscale = 8,
        CmykRgb = 9,
        YcckRgb = 10,
        Null = 11,
    }

    private ColorConverter m_converter;

    private jpeg_decompress_struct m_cinfo;

    private int[] m_perComponentOffsets = null!;

    /* Private state for YCbCr->RGB and BG_YCC->RGB conversion */
    private int[] m_Cr_r_tab = null!;      /* => table for Cr to R conversion */
    private int[] m_Cb_b_tab = null!;      /* => table for Cb to B conversion */
    private int[] m_Cr_g_tab = null!;        /* => table for Cr to G conversion */
    private int[] m_Cb_g_tab = null!;        /* => table for Cb to G conversion */

    /* Private state for RGB->Y conversion */
    private int[] rgb_y_tab = null!;        /* => table for RGB to Y conversion */

    /// <summary>
    /// Module initialization routine for output colorspace conversion.
    /// </summary>
    public jpeg_color_deconverter(jpeg_decompress_struct cinfo)
    {
        m_cinfo = cinfo;

        /* Make sure num_components agrees with jpeg_color_space */
        switch (cinfo.m_jpeg_color_space)
        {
            case J_COLOR_SPACE.JCS_GRAYSCALE:
                if (cinfo.m_num_components != 1)
                {
                    cinfo.ERREXIT(J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE);
                }

                break;

            case J_COLOR_SPACE.JCS_RGB:
            case J_COLOR_SPACE.JCS_YCbCr:
            case J_COLOR_SPACE.JCS_BG_RGB:
            case J_COLOR_SPACE.JCS_BG_YCC:
                if (cinfo.m_num_components != 3)
                {
                    cinfo.ERREXIT(J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE);
                }

                break;

            case J_COLOR_SPACE.JCS_CMYK:
            case J_COLOR_SPACE.JCS_YCCK:
                if (cinfo.m_num_components != 4)
                {
                    cinfo.ERREXIT(J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE);
                }

                break;

            case J_COLOR_SPACE.JCS_NCHANNEL:
                if (cinfo.m_num_components < 1)
                {
                    cinfo.ERREXIT(J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE);
                }

                break;

            default:
                /* JCS_UNKNOWN can be anything */
                if (cinfo.m_num_components < 1)
                {
                    cinfo.ERREXIT(J_MESSAGE_CODE.JERR_BAD_J_COLORSPACE);
                }

                break;
        }

        /* Support color transform only for RGB colorspaces */
        if (cinfo.color_transform != J_COLOR_TRANSFORM.JCT_NONE &&
            cinfo.m_jpeg_color_space != J_COLOR_SPACE.JCS_RGB &&
            cinfo.m_jpeg_color_space != J_COLOR_SPACE.JCS_BG_RGB)
        {
            cinfo.ERREXIT(J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
        }

        /* Set out_color_components and conversion method based on requested space.
        * Also clear the component_needed flags for any unused components,
        * so that earlier pipeline stages can avoid useless computation.
        */

        switch (cinfo.m_out_color_space)
        {
            case J_COLOR_SPACE.JCS_GRAYSCALE:
                cinfo.m_out_color_components = 1;
                switch (cinfo.m_jpeg_color_space)
                {
                    case J_COLOR_SPACE.JCS_GRAYSCALE:
                    case J_COLOR_SPACE.JCS_YCbCr:
                    case J_COLOR_SPACE.JCS_BG_YCC:
                        m_converter = ColorConverter.Grayscale;
                        /* For color->grayscale conversion, only the Y (0) component is needed */
                        for (int ci = 1; ci < cinfo.m_num_components; ci++)
                        {
                            cinfo.Comp_info![ci].component_needed = false;
                        }

                        break;

                    case J_COLOR_SPACE.JCS_RGB:
                        switch (cinfo.color_transform)
                        {
                            case J_COLOR_TRANSFORM.JCT_NONE:
                                m_converter = ColorConverter.RgbGray;
                                break;

                            case J_COLOR_TRANSFORM.JCT_SUBTRACT_GREEN:
                                m_converter = ColorConverter.Rgb1Gray;
                                break;

                            default:
                                cinfo.ERREXIT(J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
                                break;
                        }

                        build_rgb_y_table();
                        break;

                    default:
                        cinfo.ERREXIT(J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
                        break;
                }
                break;

            case J_COLOR_SPACE.JCS_RGB:
                cinfo.m_out_color_components = JpegConstants.RGB_PIXELSIZE;
                switch (cinfo.m_jpeg_color_space)
                {
                    case J_COLOR_SPACE.JCS_GRAYSCALE:
                        m_converter = ColorConverter.GrayRgb;
                        break;

                    case J_COLOR_SPACE.JCS_YCbCr:
                        m_converter = ColorConverter.YccRgb;
                        build_ycc_rgb_table();
                        break;

                    case J_COLOR_SPACE.JCS_BG_YCC:
                        m_converter = ColorConverter.YccRgb;
                        build_bg_ycc_rgb_table();
                        break;

                    case J_COLOR_SPACE.JCS_RGB:
                        switch (cinfo.color_transform)
                        {
                            case J_COLOR_TRANSFORM.JCT_NONE:
                                m_converter = ColorConverter.Rgb;
                                break;

                            case J_COLOR_TRANSFORM.JCT_SUBTRACT_GREEN:
                                m_converter = ColorConverter.Rgb1Rgb;
                                break;

                            default:
                                cinfo.ERREXIT(J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
                                break;
                        }
                        break;

                    case J_COLOR_SPACE.JCS_CMYK:
                        m_converter = ColorConverter.CmykRgb;
                        break;

                    case J_COLOR_SPACE.JCS_YCCK:
                        m_converter = ColorConverter.YcckRgb;
                        build_ycc_rgb_table();
                        break;

                    default:
                        cinfo.ERREXIT(J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
                        break;
                }
                break;

            case J_COLOR_SPACE.JCS_BG_RGB:
                cinfo.m_out_color_components = JpegConstants.RGB_PIXELSIZE;
                if (cinfo.m_jpeg_color_space == J_COLOR_SPACE.JCS_BG_RGB)
                {
                    switch (cinfo.color_transform)
                    {
                        case J_COLOR_TRANSFORM.JCT_NONE:
                            m_converter = ColorConverter.Rgb;
                            break;

                        case J_COLOR_TRANSFORM.JCT_SUBTRACT_GREEN:
                            m_converter = ColorConverter.Rgb1Rgb;
                            break;

                        default:
                            cinfo.ERREXIT(J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
                            break;
                    }
                }
                else
                {
                    cinfo.ERREXIT(J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
                }
                break;

            case J_COLOR_SPACE.JCS_CMYK:
                cinfo.m_out_color_components = 4;
                switch (cinfo.m_jpeg_color_space)
                {
                    case J_COLOR_SPACE.JCS_YCCK:
                        m_converter = ColorConverter.YcckCmyk;
                        build_ycc_rgb_table();
                        break;

                    case J_COLOR_SPACE.JCS_CMYK:
                        m_converter = ColorConverter.Null;
                        break;

                    default:
                        cinfo.ERREXIT(J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
                        break;
                }
                break;

            case J_COLOR_SPACE.JCS_NCHANNEL:
                if (cinfo.m_jpeg_color_space == J_COLOR_SPACE.JCS_NCHANNEL)
                {
                    m_converter = ColorConverter.Null;
                }
                else
                {
                    cinfo.ERREXIT(J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
                }

                break;

            default:
                /* Permit null conversion to same output space */
                if (cinfo.m_out_color_space == cinfo.m_jpeg_color_space)
                {
                    cinfo.m_out_color_components = cinfo.m_num_components;
                    m_converter = ColorConverter.Null;
                }
                else
                {
                    /* unsupported non-null conversion */
                    cinfo.ERREXIT(J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
                }
                break;
        }

        if (cinfo.m_quantize_colors)
        {
            cinfo.m_output_components = 1; /* single colormapped output component */
        }
        else
        {
            cinfo.m_output_components = cinfo.m_out_color_components;
        }
    }

    /// <summary>
    /// Convert some rows of samples to the output colorspace.
    /// 
    /// Note that we change from noninterleaved, one-plane-per-component format
    /// to interleaved-pixel format.  The output buffer is therefore three times
    /// as wide as the input buffer.
    /// A starting row offset is provided only for the input buffer.  The caller
    /// can easily adjust the passed output_buf value to accommodate any row
    /// offset required on that side.
    /// </summary>
    public void color_convert(ComponentBuffer[] input_buf, int[] perComponentOffsets, int input_row, byte[][] output_buf, int output_row, int num_rows)
    {
        m_perComponentOffsets = perComponentOffsets;
        switch (m_converter)
        {
            case ColorConverter.Grayscale:
                grayscale_convert(input_buf, input_row, output_buf, output_row, num_rows);
                break;
            case ColorConverter.RgbGray:
                rgb_gray_convert(input_buf, input_row, output_buf, output_row, num_rows);
                break;
            case ColorConverter.Rgb1Gray:
                rgb1_gray_convert(input_buf, input_row, output_buf, output_row, num_rows);
                break;
            case ColorConverter.GrayRgb:
                gray_rgb_convert(input_buf, input_row, output_buf, output_row, num_rows);
                break;
            case ColorConverter.YccRgb:
                ycc_rgb_convert(input_buf, input_row, output_buf, output_row, num_rows);
                break;
            case ColorConverter.Rgb:
                rgb_convert(input_buf, input_row, output_buf, output_row, num_rows);
                break;
            case ColorConverter.Rgb1Rgb:
                rgb1_rgb_convert(input_buf, input_row, output_buf, output_row, num_rows);
                break;
            case ColorConverter.CmykRgb:
                cmyk_rgb_convert(input_buf, input_row, output_buf, output_row, num_rows);
                break;
            case ColorConverter.YcckRgb:
                ycck_rgb_convert(input_buf, input_row, output_buf, output_row, num_rows);
                break;
            case ColorConverter.YcckCmyk:
                ycck_cmyk_convert(input_buf, input_row, output_buf, output_row, num_rows);
                break;
            case ColorConverter.Null:
                null_convert(input_buf, input_row, output_buf, output_row, num_rows);
                break;
            default:
                m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_CONVERSION_NOTIMPL);
                break;
        }
    }

    /**************** YCbCr -> RGB conversion: most common case **************/
    /*************** BG_YCC -> RGB conversion: less common case **************/
    /***************    RGB -> Y   conversion: less common case **************/

    /*
     * YCbCr is defined per Recommendation ITU-R BT.601-7 (03/2011),
     * previously known as Recommendation CCIR 601-1, except that Cb and Cr
     * are normalized to the range 0..MAXJSAMPLE rather than -0.5 .. 0.5.
     * sRGB (standard RGB color space) is defined per IEC 61966-2-1:1999.
     * sYCC (standard luma-chroma-chroma color space with extended gamut)
     * is defined per IEC 61966-2-1:1999 Amendment A1:2003 Annex F.
     * bg-sRGB and bg-sYCC (big gamut standard color spaces)
     * are defined per IEC 61966-2-1:1999 Amendment A1:2003 Annex G.
     * Note that the derived conversion coefficients given in some of these
     * documents are imprecise.  The general conversion equations are
     *
     *	R = Y + K * (1 - Kr) * Cr
     *	G = Y - K * (Kb * (1 - Kb) * Cb + Kr * (1 - Kr) * Cr) / (1 - Kr - Kb)
     *	B = Y + K * (1 - Kb) * Cb
     *
     *	Y = Kr * R + (1 - Kr - Kb) * G + Kb * B
     *
     * With Kr = 0.299 and Kb = 0.114 (derived according to SMPTE RP 177-1993
     * from the 1953 FCC NTSC primaries and CIE Illuminant C), K = 2 for sYCC,
     * the conversion equations to be implemented are therefore
     *
     *	R = Y + 1.402 * Cr
     *	G = Y - 0.344136286 * Cb - 0.714136286 * Cr
     *	B = Y + 1.772 * Cb
     *
     *	Y = 0.299 * R + 0.587 * G + 0.114 * B
     *
     * where Cb and Cr represent the incoming values less CENTERJSAMPLE.
     * For bg-sYCC, with K = 4, the equations are
     *
     *	R = Y + 2.804 * Cr
     *	G = Y - 0.688272572 * Cb - 1.428272572 * Cr
     *	B = Y + 3.544 * Cb
     *
     * To avoid floating-point arithmetic, we represent the fractional constants
     * as integers scaled up by 2^16 (about 4 digits precision); we have to divide
     * the products by 2^16, with appropriate rounding, to get the correct answer.
     * Notice that Y, being an integral input, does not contribute any fraction
     * so it need not participate in the rounding.
     *
     * For even more speed, we avoid doing any multiplications in the inner loop
     * by precalculating the constants times Cb and Cr for all possible values.
     * For 8-bit JSAMPLEs this is very reasonable (only 256 entries per table);
     * for 9-bit to 12-bit samples it is still acceptable.  It's not very
     * reasonable for 16-bit samples, but if you want lossless storage you
     * shouldn't be changing colorspace anyway.
     * The Cr=>R and Cb=>B values can be rounded to integers in advance; the
     * values for the G calculation are left scaled up, since we must add them
     * together before rounding.
     */

    /// <summary>
    /// Initialize tables for YCbCr->RGB colorspace conversion.
    /// </summary>
    private void build_ycc_rgb_table()
    {
        /* Normal case, sYCC */
        m_Cr_r_tab = new int[JpegConstants.MAXJSAMPLE + 1];
        m_Cb_b_tab = new int[JpegConstants.MAXJSAMPLE + 1];
        m_Cr_g_tab = new int[JpegConstants.MAXJSAMPLE + 1];
        m_Cb_g_tab = new int[JpegConstants.MAXJSAMPLE + 1];

        for (int i = 0, x = -JpegConstants.CENTERJSAMPLE; i <= JpegConstants.MAXJSAMPLE; i++, x++)
        {
            /* i is the actual input pixel value, in the range 0..MAXJSAMPLE */
            /* The Cb or Cr value we are thinking of is x = i - CENTERJSAMPLE */
            /* Cr=>R value is nearest int to 1.402 * x */
            m_Cr_r_tab[i] = (FIX(1.402) * x + ONE_HALF) >> SCALEBITS;

            /* Cb=>B value is nearest int to 1.772 * x */
            m_Cb_b_tab[i] = (FIX(1.772) * x + ONE_HALF) >> SCALEBITS;

            /* Cr=>G value is scaled-up -0.714136286 * x */
            m_Cr_g_tab[i] = (-FIX(0.714136286)) * x;

            /* Cb=>G value is scaled-up -0.344136286 * x */
            /* We also add in ONE_HALF so that need not do it in inner loop */
            m_Cb_g_tab[i] = (-FIX(0.344136286)) * x + ONE_HALF;
        }
    }

    /// <summary>
    /// Initialize tables for BG_YCC->RGB colorspace conversion.
    /// </summary>
    private void build_bg_ycc_rgb_table()
    {
        /* Wide gamut case, bg-sYCC */
        m_Cr_r_tab = new int[JpegConstants.MAXJSAMPLE + 1];
        m_Cb_b_tab = new int[JpegConstants.MAXJSAMPLE + 1];
        m_Cr_g_tab = new int[JpegConstants.MAXJSAMPLE + 1];
        m_Cb_g_tab = new int[JpegConstants.MAXJSAMPLE + 1];

        for (int i = 0, x = -JpegConstants.CENTERJSAMPLE; i <= JpegConstants.MAXJSAMPLE; i++, x++)
        {
            /* i is the actual input pixel value, in the range 0..MAXJSAMPLE */
            /* The Cb or Cr value we are thinking of is x = i - CENTERJSAMPLE */
            /* Cr=>R value is nearest int to 2.804 * x */
            m_Cr_r_tab[i] = (FIX(2.804) * x + ONE_HALF) >> SCALEBITS;

            /* Cb=>B value is nearest int to 3.544 * x */
            m_Cb_b_tab[i] = (FIX(3.544) * x + ONE_HALF) >> SCALEBITS;

            /* Cr=>G value is scaled-up -1.428272572 * x */
            m_Cr_g_tab[i] = (-FIX(1.428272572)) * x;

            /* Cb=>G value is scaled-up -0.688272572 * x */
            /* We also add in ONE_HALF so that need not do it in inner loop */
            m_Cb_g_tab[i] = (-FIX(0.688272572)) * x + ONE_HALF;
        }
    }

    private void ycc_rgb_convert(ComponentBuffer[] input_buf, int input_row, byte[][] output_buf, int output_row, int num_rows)
    {
        int component0RowOffset = m_perComponentOffsets[0];
        int component1RowOffset = m_perComponentOffsets[1];
        int component2RowOffset = m_perComponentOffsets[2];

        byte[] limit = m_cinfo.m_sample_range_limit;
        int limitOffset = m_cinfo.m_sampleRangeLimitOffset;

        var component0 = input_buf[0];
        var component1 = input_buf[1];
        var component2 = input_buf[2];
        for (int row = 0; row < num_rows; row++)
        {
            int columnOffset = 0;
            var inputBuffer0 = component0[input_row + component0RowOffset];
            var inputBuffer1 = component1[input_row + component1RowOffset];
            var inputBuffer2 = component2[input_row + component2RowOffset];
            var outputBuffer = output_buf[output_row + row];
            for (int col = 0; col < m_cinfo.m_output_width; col++)
            {
                int yPlusOffset = inputBuffer0[col] + limitOffset;
                int cb = inputBuffer1[col];
                int cr = inputBuffer2[col];

                /* Range-limiting is essential due to noise introduced by DCT losses.
                 * for extended gamut (sYCC) and wide gamut (bg-sYCC) encodings.
                 */
                outputBuffer[columnOffset++] = limit[yPlusOffset + m_Cr_r_tab[cr]];
                outputBuffer[columnOffset++] = limit[yPlusOffset + ((m_Cb_g_tab[cb] + m_Cr_g_tab[cr]) >> SCALEBITS)];
                outputBuffer[columnOffset++] = limit[yPlusOffset + m_Cb_b_tab[cb]];
            }

            input_row++;
        }
    }

    /**************** Cases other than YCC -> RGB **************/

    /*
     * Initialize for RGB->grayscale colorspace conversion.
     */
    private void build_rgb_y_table()
    {
        /* Allocate and fill in the conversion tables. */
        rgb_y_tab = new int[TABLE_SIZE];

        for (int i = 0; i <= JpegConstants.MAXJSAMPLE; i++)
        {
            rgb_y_tab[i + R_Y_OFF] = FIX(0.299) * i;
            rgb_y_tab[i + G_Y_OFF] = FIX(0.587) * i;
            rgb_y_tab[i + B_Y_OFF] = FIX(0.114) * i + ONE_HALF;
        }
    }

    /*
     * Convert RGB to grayscale.
     */
    private void rgb_gray_convert(ComponentBuffer[] input_buf, int input_row, byte[][] output_buf, int output_row, int num_rows)
    {
        int component0RowOffset = m_perComponentOffsets[0];
        int component1RowOffset = m_perComponentOffsets[1];
        int component2RowOffset = m_perComponentOffsets[2];

        int num_cols = m_cinfo.m_output_width;

        for (int row = 0; row < num_rows; row++)
        {
            int columnOffset = 0;
            var inputBuffer0 = input_buf[0][input_row + component0RowOffset];
            var inputBuffer1 = input_buf[1][input_row + component1RowOffset];
            var inputBuffer2 = input_buf[2][input_row + component2RowOffset];
            var outputBuffer = output_buf[output_row + row];
            for (int col = 0; col < num_cols; col++)
            {
                int r = inputBuffer0[col];
                int g = inputBuffer1[col];
                int b = inputBuffer2[col];

                /* Y */
                outputBuffer[columnOffset++] = (byte)((rgb_y_tab[r + R_Y_OFF] + rgb_y_tab[g + G_Y_OFF] + rgb_y_tab[b + B_Y_OFF]) >> SCALEBITS);
            }

            input_row++;
        }
    }

    /*
     * [R-G,G,B-G] to [R,G,B] conversion with modulo calculation
     * (inverse color transform).
     * This can be seen as an adaption of the general YCbCr->RGB
     * conversion equation with Kr = Kb = 0, while replacing the
     * normalization by modulo calculation.
     */
    private void rgb1_rgb_convert(ComponentBuffer[] input_buf, int input_row, byte[][] output_buf, int output_row, int num_rows)
    {
        int component0RowOffset = m_perComponentOffsets[0];
        int component1RowOffset = m_perComponentOffsets[1];
        int component2RowOffset = m_perComponentOffsets[2];

        int num_cols = m_cinfo.m_output_width;

        for (int row = 0; row < num_rows; row++)
        {
            int columnOffset = 0;
            var inputBuffer0 = input_buf[0][input_row + component0RowOffset];
            var inputBuffer1 = input_buf[1][input_row + component1RowOffset];
            var inputBuffer2 = input_buf[2][input_row + component2RowOffset];
            var outputBuffer = output_buf[output_row + row];
            for (int col = 0; col < num_cols; col++)
            {
                int r = inputBuffer0[col];
                int g = inputBuffer1[col];
                int b = inputBuffer2[col];

                /* Assume that MAXJSAMPLE+1 is a power of 2, so that the MOD
                 * (modulo) operator is equivalent to the bitmask operator AND.
                 */
                outputBuffer[columnOffset + JpegConstants.RGB_RED] = (byte)((r + g - JpegConstants.CENTERJSAMPLE) & JpegConstants.MAXJSAMPLE);
                outputBuffer[columnOffset + JpegConstants.RGB_GREEN] = (byte)g;
                outputBuffer[columnOffset + JpegConstants.RGB_BLUE] = (byte)((b + g - JpegConstants.CENTERJSAMPLE) & JpegConstants.MAXJSAMPLE);
                columnOffset += JpegConstants.RGB_PIXELSIZE;
            }

            input_row++;
        }
    }

    /*
     * [R-G,G,B-G] to grayscale conversion with modulo calculation
     * (inverse color transform).
     */
    private void rgb1_gray_convert(ComponentBuffer[] input_buf, int input_row, byte[][] output_buf, int output_row, int num_rows)
    {
        int component0RowOffset = m_perComponentOffsets[0];
        int component1RowOffset = m_perComponentOffsets[1];
        int component2RowOffset = m_perComponentOffsets[2];

        int num_cols = m_cinfo.m_output_width;

        for (int row = 0; row < num_rows; row++)
        {
            int columnOffset = 0;
            var inputBuffer0 = input_buf[0][input_row + component0RowOffset];
            var inputBuffer1 = input_buf[1][input_row + component1RowOffset];
            var inputBuffer2 = input_buf[2][input_row + component2RowOffset];
            var outputBuffer = output_buf[output_row + row];
            for (int col = 0; col < num_cols; col++)
            {
                int r = inputBuffer0[col];
                int g = inputBuffer1[col];
                int b = inputBuffer2[col];

                /* Assume that MAXJSAMPLE+1 is a power of 2, so that the MOD
                 * (modulo) operator is equivalent to the bitmask operator AND.
                 */
                r = (r + g - JpegConstants.CENTERJSAMPLE) & JpegConstants.MAXJSAMPLE;
                b = (b + g - JpegConstants.CENTERJSAMPLE) & JpegConstants.MAXJSAMPLE;

                /* Y */
                outputBuffer[columnOffset++] = (byte)((rgb_y_tab[r + R_Y_OFF] + rgb_y_tab[g + G_Y_OFF] + rgb_y_tab[b + B_Y_OFF]) >> SCALEBITS);
            }

            input_row++;
        }
    }

    /*
     * No colorspace change, but conversion from separate-planes
     * to interleaved representation.
     */
    private void rgb_convert(ComponentBuffer[] input_buf, int input_row, byte[][] output_buf, int output_row, int num_rows)
    {
        int component0RowOffset = m_perComponentOffsets[0];
        int component1RowOffset = m_perComponentOffsets[1];
        int component2RowOffset = m_perComponentOffsets[2];

        int num_cols = m_cinfo.m_output_width;

        for (int row = 0; row < num_rows; row++)
        {
            int columnOffset = 0;
            var inputBuffer0 = input_buf[0][input_row + component0RowOffset];
            var inputBuffer1 = input_buf[1][input_row + component1RowOffset];
            var inputBuffer2 = input_buf[2][input_row + component2RowOffset];
            var outputBuffer = output_buf[output_row + row];
            for (int col = 0; col < num_cols; col++)
            {
                int r = inputBuffer0[col];
                int g = inputBuffer1[col];
                int b = inputBuffer2[col];

                /* We can dispense with GETJSAMPLE() here */
                outputBuffer[columnOffset + JpegConstants.RGB_RED] = (byte)r;
                outputBuffer[columnOffset + JpegConstants.RGB_GREEN] = (byte)g;
                outputBuffer[columnOffset + JpegConstants.RGB_BLUE] = (byte)b;
                columnOffset += JpegConstants.RGB_PIXELSIZE;
            }

            input_row++;
        }
    }

    /// <summary>
    /// Adobe-style YCCK->CMYK conversion.
    /// We convert YCbCr to R=1-C, G=1-M, and B=1-Y using the same
    /// conversion as above, while passing K (black) unchanged.
    /// We assume build_ycc_rgb_table has been called.
    /// </summary>
    private void ycck_cmyk_convert(ComponentBuffer[] input_buf, int input_row, byte[][] output_buf, int output_row, int num_rows)
    {
        int component0RowOffset = m_perComponentOffsets[0];
        int component1RowOffset = m_perComponentOffsets[1];
        int component2RowOffset = m_perComponentOffsets[2];
        int component3RowOffset = m_perComponentOffsets[3];

        byte[] limit = m_cinfo.m_sample_range_limit;
        int limitOffset = m_cinfo.m_sampleRangeLimitOffset;

        int num_cols = m_cinfo.m_output_width;
        var component0 = input_buf[0];
        var component1 = input_buf[1];
        var component2 = input_buf[2];
        var component3 = input_buf[3];
        for (int row = 0; row < num_rows; row++)
        {
            int columnOffset = 0;
            var inputBuffer0 = component0[input_row + component0RowOffset];
            var inputBuffer1 = component1[input_row + component1RowOffset];
            var inputBuffer2 = component2[input_row + component2RowOffset];
            var inputBuffer3 = component3[input_row + component3RowOffset];
            var outputBuffer = output_buf[output_row + row];
            for (int col = 0; col < num_cols; col++)
            {
                int yAdjusted = limitOffset + JpegConstants.MAXJSAMPLE - inputBuffer0[col];
                int cb = inputBuffer1[col];
                int cr = inputBuffer2[col];

                /* Range-limiting is essential due to noise introduced by DCT losses,
                 * and for extended gamut encodings (sYCC).
                 */
                outputBuffer[columnOffset++] = limit[yAdjusted - m_Cr_r_tab[cr]]; /* red */
                outputBuffer[columnOffset++] = limit[yAdjusted - ((m_Cb_g_tab[cb] + m_Cr_g_tab[cr]) >> SCALEBITS)]; /* green */
                outputBuffer[columnOffset++] = limit[yAdjusted - m_Cb_b_tab[cb]]; /* blue */

                /* K passes through unchanged */
                /* don't need GETJSAMPLE here */
                outputBuffer[columnOffset++] = inputBuffer3[col];
            }

            input_row++;
        }
    }

    /// <summary>
    /// Convert grayscale to RGB: just duplicate the graylevel three times.
    /// This is provided to support applications that don't want to cope
    /// with grayscale as a separate case.
    /// </summary>
    private void gray_rgb_convert(ComponentBuffer[] input_buf, int input_row, byte[][] output_buf, int output_row, int num_rows)
    {
        int component0RowOffset = m_perComponentOffsets[0];
        int component1RowOffset = m_perComponentOffsets[1];
        int component2RowOffset = m_perComponentOffsets[2];

        int num_cols = m_cinfo.m_output_width;
        for (int row = 0; row < num_rows; row++)
        {
            int columnOffset = 0;
            var inputBuffer0 = input_buf[0][input_row + component0RowOffset];
            var inputBuffer1 = input_buf[0][input_row + component1RowOffset];
            var inputBuffer2 = input_buf[0][input_row + component2RowOffset];
            var outputBuffer = output_buf[output_row + row];
            for (int col = 0; col < num_cols; col++)
            {
                /* We can dispense with GETJSAMPLE() here */
                outputBuffer[columnOffset + JpegConstants.RGB_RED] = inputBuffer0[col];
                outputBuffer[columnOffset + JpegConstants.RGB_GREEN] = inputBuffer1[col];
                outputBuffer[columnOffset + JpegConstants.RGB_BLUE] = inputBuffer2[col];
                columnOffset += JpegConstants.RGB_PIXELSIZE;
            }

            input_row++;
        }
    }

    /// <summary>
    /// Color conversion for grayscale: just copy the data.
    /// This also works for YCC -> grayscale conversion, in which
    /// we just copy the Y (luminance) component and ignore chrominance.
    /// </summary>
    private void grayscale_convert(ComponentBuffer[] input_buf, int input_row, byte[][] output_buf, int output_row, int num_rows)
    {
        JpegUtils.jcopy_sample_rows(input_buf[0], input_row + m_perComponentOffsets[0], output_buf, output_row, num_rows, m_cinfo.m_output_width);
    }

    /// <summary>
    /// Color conversion for CMYK -> RGB
    /// </summary>
    private void cmyk_rgb_convert(ComponentBuffer[] input_buf, int input_row, byte[][] output_buf, int output_row, int num_rows)
    {
        int component0RowOffset = m_perComponentOffsets[0];
        int component1RowOffset = m_perComponentOffsets[1];
        int component2RowOffset = m_perComponentOffsets[2];
        int component3RowOffset = m_perComponentOffsets[3];

        for (int row = 0; row < num_rows; row++)
        {
            int columnOffset = 0;
            var inputBuffer0 = input_buf[0][input_row + component0RowOffset];
            var inputBuffer1 = input_buf[1][input_row + component1RowOffset];
            var inputBuffer2 = input_buf[2][input_row + component2RowOffset];
            var inputBuffer3 = input_buf[3][input_row + component3RowOffset];
            var outputBuffer = output_buf[output_row + row];
            for (int col = 0; col < m_cinfo.m_output_width; col++)
            {
                int c = inputBuffer0[col];
                int m = inputBuffer1[col];
                int y = inputBuffer2[col];
                int k = inputBuffer3[col];

                outputBuffer[columnOffset + JpegConstants.RGB_RED] = (byte)((c * k) / 255);
                outputBuffer[columnOffset + JpegConstants.RGB_GREEN] = (byte)((m * k) / 255);
                outputBuffer[columnOffset + JpegConstants.RGB_BLUE] = (byte)((y * k) / 255);
                columnOffset += JpegConstants.RGB_PIXELSIZE;
            }

            input_row++;
        }
    }

    /// <summary>
    /// Color conversion for YCCK -> RGB
    /// it's just a gybrid of YCCK -> CMYK and CMYK -> RGB conversions
    /// </summary>
    private void ycck_rgb_convert(ComponentBuffer[] input_buf, int input_row, byte[][] output_buf, int output_row, int num_rows)
    {
        int component0RowOffset = m_perComponentOffsets[0];
        int component1RowOffset = m_perComponentOffsets[1];
        int component2RowOffset = m_perComponentOffsets[2];
        int component3RowOffset = m_perComponentOffsets[3];

        byte[] limit = m_cinfo.m_sample_range_limit;
        int limitOffset = m_cinfo.m_sampleRangeLimitOffset;

        int num_cols = m_cinfo.m_output_width;
        for (int row = 0; row < num_rows; row++)
        {
            int columnOffset = 0;
            var inputBuffer0 = input_buf[0][input_row + component0RowOffset];
            var inputBuffer1 = input_buf[1][input_row + component1RowOffset];
            var inputBuffer2 = input_buf[2][input_row + component2RowOffset];
            var inputBuffer3 = input_buf[3][input_row + component3RowOffset];
            var outputBuffer = output_buf[output_row + row];
            for (int col = 0; col < num_cols; col++)
            {
                int y = inputBuffer0[col];
                int cb = inputBuffer1[col];
                int cr = inputBuffer2[col];

                int cmyk_c = limit[limitOffset + JpegConstants.MAXJSAMPLE - (y + m_Cr_r_tab[cr])];
                int cmyk_m = limit[limitOffset + JpegConstants.MAXJSAMPLE - (y + ((m_Cb_g_tab[cb] + m_Cr_g_tab[cr]) >> SCALEBITS))];
                int cmyk_y = limit[limitOffset + JpegConstants.MAXJSAMPLE - (y + m_Cb_b_tab[cb])];
                int cmyk_k = inputBuffer3[col];

                outputBuffer[columnOffset + JpegConstants.RGB_RED] = (byte)((cmyk_c * cmyk_k) / 255);
                outputBuffer[columnOffset + JpegConstants.RGB_GREEN] = (byte)((cmyk_m * cmyk_k) / 255);
                outputBuffer[columnOffset + JpegConstants.RGB_BLUE] = (byte)((cmyk_y * cmyk_k) / 255);
                columnOffset += JpegConstants.RGB_PIXELSIZE;
            }

            input_row++;
        }
    }

    /// <summary>
    /// Color conversion for no colorspace change: just copy the data,
    /// converting from separate-planes to interleaved representation.
    /// </summary>
    private void null_convert(ComponentBuffer[] input_buf, int input_row, byte[][] output_buf, int output_row, int num_rows)
    {
        for (int row = 0; row < num_rows; row++)
        {
            for (int ci = 0; ci < m_cinfo.m_num_components; ci++)
            {
                int columnIndex = 0;
                int componentOffset = 0;
                int perComponentOffset = m_perComponentOffsets[ci];
                var inputBuffer = input_buf[ci][input_row + perComponentOffset];
                var outputBuffer = output_buf[output_row + row];
                for (int col = 0; col < m_cinfo.m_output_width; col++)
                {
                    /* needn't bother with GETJSAMPLE() here */
                    outputBuffer[ci + componentOffset] = inputBuffer[columnIndex];
                    componentOffset += m_cinfo.m_num_components;
                    columnIndex++;
                }
            }

            input_row++;
        }
    }

    private static int FIX(double x)
    {
        return (int)(x * (1L << SCALEBITS) + 0.5);
    }
}
