// Ported from Windows Terminal src/terminal/parser/base64.cpp (MIT).

using System.Text;

namespace Aprillz.MewUI.Terminal.Parser;

/// <summary>Decodes OSC 52 payloads: standard or URL-safe base64 of UTF-8 text, with optional padding.</summary>
internal static class Base64
{
    private const byte INVALID = 255;

    private static ReadOnlySpan<byte> DecodeTable =>
    [
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255,
        255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 255, 62, 255, 62, 255, 63,
        52, 53, 54, 55, 56, 57, 58, 59, 60, 61, 255, 255, 255, 255, 255, 255,
        255, 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14,
        15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 255, 255, 255, 255, 63,
        255, 26, 27, 28, 29, 30, 31, 32, 33, 34, 35, 36, 37, 38, 39, 40,
        41, 42, 43, 44, 45, 46, 47, 48, 49, 50, 51, 255, 255, 255, 255, 255,
    ];

    /// <summary>Decodes <paramref name="source"/>; false when it holds a character outside the alphabet or a stray length.</summary>
    public static bool TryDecode(ReadOnlySpan<char> source, out string result)
    {
        var bytes = new byte[(source.Length + 3) / 4 * 3];
        int outIndex = 0;
        int inIndex = 0;
        int inEndBatched = source.Length - Math.Min(5, source.Length);
        uint accumulator = 0;
        int error = 0;

        void Accumulate(char character)
        {
            int value = DecodeTable[character & 0x7F];
            error |= (character | value) & 0xFF80;
            accumulator = accumulator << 6 | (uint)value;
        }

        while (inIndex < inEndBatched)
        {
            Accumulate(source[inIndex++]);
            Accumulate(source[inIndex++]);
            Accumulate(source[inIndex++]);
            Accumulate(source[inIndex++]);
            bytes[outIndex++] = (byte)(accumulator >> 16);
            bytes[outIndex++] = (byte)(accumulator >> 8);
            bytes[outIndex++] = (byte)accumulator;
        }

        int remaining = 0;
        for (; inIndex < source.Length; inIndex++)
        {
            char character = source[inIndex];
            if (character != '=')
            {
                Accumulate(character);
                remaining++;
            }
        }

        if (remaining == 2)
        {
            bytes[outIndex++] = (byte)(accumulator >> 4);
        }
        else if (remaining == 3)
        {
            bytes[outIndex++] = (byte)(accumulator >> 10);
            bytes[outIndex++] = (byte)(accumulator >> 2);
        }
        else if (remaining == 4)
        {
            bytes[outIndex++] = (byte)(accumulator >> 16);
            bytes[outIndex++] = (byte)(accumulator >> 8);
            bytes[outIndex++] = (byte)accumulator;
        }
        else
        {
            error |= remaining;
        }

        if (error != 0)
        {
            result = string.Empty;
            return false;
        }

        result = Encoding.UTF8.GetString(bytes, 0, outIndex);
        return true;
    }
}
