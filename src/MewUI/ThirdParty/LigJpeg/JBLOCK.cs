namespace BitMiracle.LibJpeg.Classic;

/// <summary>
/// One block of coefficients (DCTSIZE2).
/// </summary>
internal unsafe struct JBLOCK
{
    private fixed short m_data[JpegConstants.DCTSIZE2];

    /// <summary>
    /// Gets or sets the element at the specified index.
    /// </summary>
    public short this[int index]
    {
        get
        {
            if ((uint)index >= (uint)JpegConstants.DCTSIZE2)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            fixed (short* p = m_data)
            {
                return p[index];
            }
        }
        set
        {
            if ((uint)index >= (uint)JpegConstants.DCTSIZE2)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            fixed (short* p = m_data)
            {
                p[index] = value;
            }
        }
    }

    public Span<short> Coefficients
    {
        get
        {
            fixed (short* p = m_data)
            {
                return new Span<short>(p, JpegConstants.DCTSIZE2);
            }
        }
    }

    public ReadOnlySpan<short> ReadOnlyCoefficients
    {
        get
        {
            fixed (short* p = m_data)
            {
                return new ReadOnlySpan<short>(p, JpegConstants.DCTSIZE2);
            }
        }
    }

    public void Clear() => Coefficients.Clear();

    public void CopyFrom(in JBLOCK source) => source.ReadOnlyCoefficients.CopyTo(Coefficients);
}

