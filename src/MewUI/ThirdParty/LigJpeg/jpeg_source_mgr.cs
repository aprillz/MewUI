namespace BitMiracle.LibJpeg.Classic;

/// <summary>
/// Data source object for decompression.
/// This fork supports only stream input (no external source manager extensions).
/// </summary>
internal struct jpeg_source_mgr
{
    private const int INPUT_BUF_SIZE = 16384; // 16KB for better I/O efficiency

    private jpeg_decompress_struct m_cinfo;

    private Stream m_infile;
    private byte[] m_streamBuffer;
    private bool m_start_of_file;

    private byte[] m_next_input_byte;
    private int m_bytes_in_buffer; /* # of bytes remaining (unread) in buffer */
    private int m_position;

    internal void Attach(jpeg_decompress_struct cinfo, Stream infile)
    {
        m_cinfo = cinfo;
        m_infile = infile;

        m_streamBuffer ??= new byte[INPUT_BUF_SIZE];

        if (m_infile.CanSeek)
        {
            m_infile.Seek(0, SeekOrigin.Begin);
        }

        initInternalBuffer(null, 0);
    }

    /// <summary>
    /// Initialize source - called by jpeg_read_header before any data is actually read.
    /// </summary>
    public void init_source()
    {
        m_start_of_file = true;
    }

    /// <summary>
    /// Fill the input buffer - called whenever buffer is emptied.
    /// </summary>
    public bool fill_input_buffer()
    {
        int nbytes = m_infile.Read(m_streamBuffer, 0, INPUT_BUF_SIZE);
        if (nbytes <= 0)
        {
            if (m_start_of_file) /* Treat empty input file as fatal error */
            {
                m_cinfo.ERREXIT(J_MESSAGE_CODE.JERR_INPUT_EMPTY);
            }

            m_cinfo.WARNMS(J_MESSAGE_CODE.JWRN_JPEG_EOF);
            /* Insert a fake EOI marker */
            m_streamBuffer[0] = 0xFF;
            m_streamBuffer[1] = (byte)JPEG_MARKER.EOI;
            nbytes = 2;
        }

        initInternalBuffer(m_streamBuffer, nbytes);
        m_start_of_file = false;

        return true;
    }

    private void initInternalBuffer(byte[]? buffer, int size)
    {
        m_bytes_in_buffer = size;
        m_next_input_byte = buffer ?? Array.Empty<byte>();
        m_position = 0;
    }

    /// <summary>
    /// Skip data - used to skip over a potentially large amount of uninteresting data.
    /// </summary>
    public void skip_input_data(int num_bytes)
    {
        if (num_bytes <= 0)
        {
            return;
        }

        while (num_bytes > m_bytes_in_buffer)
        {
            num_bytes -= m_bytes_in_buffer;
            fill_input_buffer();
        }

        m_position += num_bytes;
        m_bytes_in_buffer -= num_bytes;
    }

    /// <summary>
    /// Default resync_to_restart implementation.
    /// </summary>
    public bool resync_to_restart(jpeg_decompress_struct cinfo, int desired)
    {
        if (cinfo.m_unread_marker == 0)
        {
            cinfo.WARNMS(J_MESSAGE_CODE.JWRN_MUST_RESYNC);
            return false;
        }

        for (; ; )
        {
            int action;
            if (cinfo.m_unread_marker < (int)JPEG_MARKER.RST0 || cinfo.m_unread_marker > (int)JPEG_MARKER.RST7)
            {
                /* non-RST marker */
                action = 2;
            }
            else
            {
                if (cinfo.m_unread_marker == ((int)JPEG_MARKER.RST0 + ((desired + 1) & 7)) ||
                    cinfo.m_unread_marker == ((int)JPEG_MARKER.RST0 + ((desired + 2) & 7)))
                {
                    action = 3;
                }
                else if (cinfo.m_unread_marker == ((int)JPEG_MARKER.RST0 + ((desired - 1) & 7)) ||
                    cinfo.m_unread_marker == ((int)JPEG_MARKER.RST0 + ((desired - 2) & 7)))
                {
                    action = 2;
                }
                else
                {
                    action = 1;
                }
            }

            cinfo.TRACEMS(4, J_MESSAGE_CODE.JTRC_RECOVERY_ACTION, cinfo.m_unread_marker, action);

            switch (action)
            {
                case 1:
                    cinfo.m_unread_marker = 0;
                    return true;
                case 2:
                    if (!cinfo.m_marker.next_marker())
                    {
                        return false;
                    }

                    break;
                case 3:
                    return true;
            }
        }
    }

    public void term_source()
    {
    }

    public bool GetTwoBytes(out int V)
    {
        if (!MakeByteAvailable())
        {
            V = 0;
            return false;
        }

        m_bytes_in_buffer--;
        V = m_next_input_byte[m_position++] << 8;

        if (!MakeByteAvailable())
        {
            return false;
        }

        m_bytes_in_buffer--;
        V += m_next_input_byte[m_position++];
        return true;
    }

    public bool GetByte(out int V)
    {
        if (m_bytes_in_buffer == 0 && !fill_input_buffer())
        {
            V = 0;
            return false;
        }

        m_bytes_in_buffer--;
        V = m_next_input_byte[m_position++];
        return true;
    }

    public int GetBytes(byte[] dest, int amount)
        => GetBytes(dest, 0, amount);

    public int GetBytes(byte[] dest, int destOffset, int amount)
    {
        int avail = amount;
        if (avail > m_bytes_in_buffer)
        {
            avail = m_bytes_in_buffer;
        }

        int destIndex = destOffset;
        for (int i = 0; i < avail; i++)
        {
            dest[destIndex] = m_next_input_byte[m_position];
            destIndex++;
            m_position++;
            m_bytes_in_buffer--;
        }

        return avail;
    }

    public bool MakeByteAvailable()
    {
        if (m_bytes_in_buffer == 0 && !fill_input_buffer())
        {
            return false;
        }

        return true;
    }
}
