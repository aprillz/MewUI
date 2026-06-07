namespace BitMiracle.LibJpeg.Classic.Internal;

/// <summary>
/// Bitreading state saved across MCUs
/// </summary>
struct bitread_perm_state
{
    public int get_buffer;    /* current bit-extraction buffer */
    public int bits_left;      /* # of unused bits in it */
}
