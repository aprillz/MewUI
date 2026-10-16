using System.Buffers.Binary;

namespace Aprillz.MewUI.Platform.Linux.Framebuffer;

/// <summary>Row copies between the BGRA layout the backends produce and the byte order a device expects.</summary>
internal static class FramebufferPixelCopy
{
    /// <summary>Copies 32-bit pixels while exchanging the first and third byte (BGRA to RGBA and back).</summary>
    public static void SwapRedBlue(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int pixelCount = Math.Min(source.Length, destination.Length) / 4;
        for (int pixel = 0; pixel < pixelCount; pixel++)
        {
            uint value = BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(pixel * 4, 4));
            uint swapped = (value & 0xFF00FF00u) | ((value & 0x00FF0000u) >> 16) | ((value & 0x000000FFu) << 16);
            BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(pixel * 4, 4), swapped);
        }
    }

    /// <summary>Packs BGRA pixels into little-endian RGB565, dropping the low bits of each channel.</summary>
    public static void ToRgb565(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        int pixelCount = Math.Min(source.Length / 4, destination.Length / 2);
        for (int pixel = 0; pixel < pixelCount; pixel++)
        {
            int blue = source[pixel * 4];
            int green = source[pixel * 4 + 1];
            int red = source[pixel * 4 + 2];
            ushort packed = (ushort)(((red >> 3) << 11) | ((green >> 2) << 5) | (blue >> 3));
            BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(pixel * 2, 2), packed);
        }
    }
}
