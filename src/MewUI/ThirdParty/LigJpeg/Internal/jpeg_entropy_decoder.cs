namespace BitMiracle.LibJpeg.Classic.Internal;

/// <summary>
/// Entropy decoding
/// </summary>
abstract class jpeg_entropy_decoder
{
    internal delegate bool decode_mcu_delegate(JBLOCK[] MCU_data);
    internal delegate void finish_pass_delegate();

    public decode_mcu_delegate decode_mcu = null!;
    public finish_pass_delegate finish_pass = null!;

    public abstract void start_pass();
}
