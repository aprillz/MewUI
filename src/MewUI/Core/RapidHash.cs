using System.Buffers.Binary;

namespace Aprillz.MewUI;

/// <summary>64-bit rapidhash (v1) over a byte span; scalar, so it hashes the same on every platform.</summary>
internal static class RapidHash
{
    private const ulong SECRET0 = 0x2d358dccaa6c78a5UL;
    private const ulong SECRET1 = 0x8bb84b93962eacc9UL;
    private const ulong SECRET2 = 0x4b33a62ed433d4a3UL;

    public static ulong Hash(ReadOnlySpan<byte> data, ulong seed = 0)
    {
        int length = data.Length;
        seed ^= Mix(seed ^ SECRET0, SECRET1) ^ (ulong)length;
        ulong wordA;
        ulong wordB;
        if (length <= 16)
        {
            if (length >= 4)
            {
                ReadOnlySpan<byte> last = data.Slice(length - 4);
                wordA = ((ulong)Read32(data) << 32) | Read32(last);
                int delta = (length & 24) >> (length >> 3);
                wordB = ((ulong)Read32(data.Slice(delta)) << 32) | Read32(data.Slice(length - 4 - delta));
            }
            else if (length > 0)
            {
                wordA = ((ulong)data[0] << 56) | ((ulong)data[length >> 1] << 32) | data[length - 1];
                wordB = 0;
            }
            else
            {
                wordA = 0;
                wordB = 0;
            }
        }
        else
        {
            int offset = 0;
            int remaining = length;
            if (remaining > 48)
            {
                ulong seed1 = seed;
                ulong seed2 = seed;
                while (remaining >= 96)
                {
                    seed = Mix(Read64(data, offset) ^ SECRET0, Read64(data, offset + 8) ^ seed);
                    seed1 = Mix(Read64(data, offset + 16) ^ SECRET1, Read64(data, offset + 24) ^ seed1);
                    seed2 = Mix(Read64(data, offset + 32) ^ SECRET2, Read64(data, offset + 40) ^ seed2);
                    seed = Mix(Read64(data, offset + 48) ^ SECRET0, Read64(data, offset + 56) ^ seed);
                    seed1 = Mix(Read64(data, offset + 64) ^ SECRET1, Read64(data, offset + 72) ^ seed1);
                    seed2 = Mix(Read64(data, offset + 80) ^ SECRET2, Read64(data, offset + 88) ^ seed2);
                    offset += 96;
                    remaining -= 96;
                }

                if (remaining >= 48)
                {
                    seed = Mix(Read64(data, offset) ^ SECRET0, Read64(data, offset + 8) ^ seed);
                    seed1 = Mix(Read64(data, offset + 16) ^ SECRET1, Read64(data, offset + 24) ^ seed1);
                    seed2 = Mix(Read64(data, offset + 32) ^ SECRET2, Read64(data, offset + 40) ^ seed2);
                    offset += 48;
                    remaining -= 48;
                }

                seed ^= seed1 ^ seed2;
            }

            if (remaining > 16)
            {
                seed = Mix(Read64(data, offset) ^ SECRET2, Read64(data, offset + 8) ^ seed ^ SECRET1);
                if (remaining > 32)
                {
                    seed = Mix(Read64(data, offset + 16) ^ SECRET2, Read64(data, offset + 24) ^ seed);
                }
            }

            wordA = Read64(data, offset + remaining - 16);
            wordB = Read64(data, offset + remaining - 8);
        }

        wordA ^= SECRET1;
        wordB ^= seed;
        ulong high = Math.BigMul(wordA, wordB, out ulong low);
        return Mix(low ^ SECRET0 ^ (ulong)length, high ^ SECRET1);
    }

    /// <summary>Folds one 64-bit value into <paramref name="seed"/> with a single multiply; the seed survives a zero product.</summary>
    public static ulong Combine(ulong seed, ulong value)
        => seed ^ Mix(seed ^ SECRET0, value ^ SECRET1);

    private static ulong Mix(ulong left, ulong right)
    {
        ulong high = Math.BigMul(left, right, out ulong low);
        return low ^ high;
    }

    private static ulong Read64(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt64LittleEndian(data.Slice(offset));

    private static uint Read32(ReadOnlySpan<byte> data)
        => BinaryPrimitives.ReadUInt32LittleEndian(data);
}
