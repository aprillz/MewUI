using System.Buffers.Binary;
using System.Text;

namespace Aprillz.MewUI.Resources;

internal static class TrueTypeNameTable
{
    public static bool TryGetFamilyName(string path, out string familyName)
    {
        familyName = string.Empty;

        try
        {
            using var fs = File.OpenRead(path);
            return TryGetFamilyName(fs, out familyName);
        }
        catch
        {
            familyName = string.Empty;
            return false;
        }
    }

    public static bool TryGetFamilyName(Stream stream, out string familyName)
    {
        familyName = string.Empty;

        if (!stream.CanSeek)
        {
            // Copy to memory for simplicity; font files are expected to be small.
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            ms.Position = 0;
            return TryGetFamilyName(ms, out familyName);
        }

        long start = stream.Position;
        try
        {
            Span<byte> header = stackalloc byte[4];
            if (!ReadExactly(stream, header))
            {
                return false;
            }

            // TTC header: 'ttcf'
            if (header[0] == (byte)'t' && header[1] == (byte)'t' && header[2] == (byte)'c' && header[3] == (byte)'f')
            {
                return TryGetFamilyNameFromTtc(stream, start, out familyName);
            }

            stream.Position = start;
            return TryGetFamilyNameFromSfnt(stream, start, out familyName);
        }
        finally
        {
            stream.Position = start;
        }
    }

    private static bool TryGetFamilyNameFromTtc(Stream stream, long baseOffset, out string familyName)
    {
        familyName = string.Empty;

        stream.Position = baseOffset;
        Span<byte> ttcHeader = stackalloc byte[12];
        if (!ReadExactly(stream, ttcHeader))
        {
            return false;
        }

        // 'ttcf' already checked
        uint numFonts = ReadU32BE(ttcHeader[8..12]);
        if (numFonts == 0)
        {
            return false;
        }

        Span<byte> offsetBytes = stackalloc byte[4];
        if (!ReadExactly(stream, offsetBytes))
        {
            return false;
        }

        uint firstFontOffset = ReadU32BE(offsetBytes);
        return TryGetFamilyNameFromSfnt(stream, baseOffset + firstFontOffset, out familyName);
    }

    private static bool TryGetFamilyNameFromSfnt(Stream stream, long offset, out string familyName)
    {
        familyName = string.Empty;
        stream.Position = offset;

        Span<byte> offsetTable = stackalloc byte[12];
        if (!ReadExactly(stream, offsetTable))
        {
            return false;
        }

        ushort numTables = ReadU16BE(offsetTable[4..6]);
        if (numTables == 0 || numTables > 512)
        {
            return false;
        }

        long nameTableOffset = 0;
        uint nameTableLength = 0;

        Span<byte> record = stackalloc byte[16];
        for (int i = 0; i < numTables; i++)
        {
            if (!ReadExactly(stream, record))
            {
                return false;
            }

            uint tag = ReadU32BE(record[0..4]);
            // 'name'
            if (tag == 0x6E616D65)
            {
                nameTableOffset = offset + ReadU32BE(record[8..12]);
                nameTableLength = ReadU32BE(record[12..16]);
                break;
            }
        }

        if (nameTableOffset <= 0 || nameTableLength < 18)
        {
            return false;
        }

        stream.Position = nameTableOffset;
        Span<byte> nameHeader = stackalloc byte[6];
        if (!ReadExactly(stream, nameHeader))
        {
            return false;
        }

        ushort count = ReadU16BE(nameHeader[2..4]);
        ushort stringOffset = ReadU16BE(nameHeader[4..6]);
        if (count == 0 || count > 1024)
        {
            return false;
        }

        long recordsStart = nameTableOffset + 6;
        long storageStart = nameTableOffset + stringOffset;

        var best = default(NameRecordCandidate);
        bool hasBest = false;

        Span<byte> nameRecord = stackalloc byte[12];
        for (int i = 0; i < count; i++)
        {
            stream.Position = recordsStart + i * 12L;
            if (!ReadExactly(stream, nameRecord))
            {
                return false;
            }

            ushort platformId = ReadU16BE(nameRecord[0..2]);
            ushort encodingId = ReadU16BE(nameRecord[2..4]);
            ushort languageId = ReadU16BE(nameRecord[4..6]);
            ushort nameId = ReadU16BE(nameRecord[6..8]);
            ushort length = ReadU16BE(nameRecord[8..10]);
            ushort recordOffset = ReadU16BE(nameRecord[10..12]);

            // Prefer Typographic Family (16), fallback to Font Family (1).
            if (nameId != 16 && nameId != 1)
            {
                continue;
            }

            // Only handle Unicode encodings (platform 0 or Windows platform 3).
            if (platformId != 0 && platformId != 3)
            {
                continue;
            }

            if (platformId == 3 && encodingId != 1 && encodingId != 10)
            {
                continue;
            }

            long strPos = storageStart + recordOffset;
            if (strPos < nameTableOffset || strPos + length > nameTableOffset + nameTableLength)
            {
                continue;
            }

            var candidate = new NameRecordCandidate(platformId, languageId, nameId, length, strPos);
            if (!hasBest || candidate.IsBetterThan(best))
            {
                best = candidate;
                hasBest = true;
            }
        }

        if (!hasBest)
        {
            return false;
        }

        stream.Position = best.StringPos;
        byte[] strBytes = new byte[best.Length];
        if (stream.Read(strBytes, 0, strBytes.Length) != strBytes.Length)
        {
            return false;
        }

        // Unicode is big-endian UTF-16 for Windows entries.
        // Use a BOM-less big-endian decoder.
        string s = Encoding.BigEndianUnicode.GetString(strBytes);
        s = s.Trim().Replace('\0', ' ').Trim();

        if (string.IsNullOrWhiteSpace(s))
        {
            return false;
        }

        familyName = s;
        return true;
    }

    private readonly record struct NameRecordCandidate(ushort PlatformId, ushort LanguageId, ushort NameId, ushort Length, long StringPos)
    {
        public bool IsBetterThan(NameRecordCandidate other)
        {
            int score = Score();
            int otherScore = other.Score();
            if (score != otherScore)
            {
                return score > otherScore;
            }

            return Length > other.Length;
        }

        private int Score()
        {
            int score = 0;

            // Prefer typographic family.
            if (NameId == 16)
            {
                score += 20;
            }

            // Prefer Windows platform.
            if (PlatformId == 3)
            {
                score += 10;
            }

            // Prefer en-US.
            if (LanguageId == 0x0409)
            {
                score += 5;
            }

            return score;
        }
    }

    private static bool ReadExactly(Stream stream, Span<byte> buffer)
    {
        int total = 0;
        while (total < buffer.Length)
        {
            int read = stream.Read(buffer[total..]);
            if (read <= 0)
            {
                return false;
            }
            total += read;
        }
        return true;
    }

    private static ushort ReadU16BE(ReadOnlySpan<byte> s) => BinaryPrimitives.ReadUInt16BigEndian(s);
    private static uint ReadU32BE(ReadOnlySpan<byte> s) => BinaryPrimitives.ReadUInt32BigEndian(s);
}

