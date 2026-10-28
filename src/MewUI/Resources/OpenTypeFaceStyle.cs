using System.Buffers.Binary;

namespace Aprillz.MewUI.Resources;

/// <summary>Reads the weight and slant a font file declares for itself in its <c>OS/2</c> table.</summary>
internal static class OpenTypeFaceStyle
{
    private const uint TTC_TAG = 0x74746366;
    private const uint OS2_TAG = 0x4F532F32;
    private const ushort FS_SELECTION_ITALIC = 1 << 0;
    private const ushort FS_SELECTION_OBLIQUE = 1 << 9;

    /// <summary>Reads the first face of <paramref name="path"/>; a file without a readable <c>OS/2</c> table reports 400 upright.</summary>
    public static bool TryRead(string path, out int weight, out bool italic)
    {
        weight = (int)FontWeight.Normal;
        italic = false;

        try
        {
            using var stream = File.OpenRead(path);
            return TryRead(stream, out weight, out italic);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryRead(Stream stream, out int weight, out bool italic)
    {
        weight = (int)FontWeight.Normal;
        italic = false;

        Span<byte> header = stackalloc byte[12];
        if (!ReadAt(stream, 0, header))
        {
            return false;
        }

        long faceOffset = 0;
        if (BinaryPrimitives.ReadUInt32BigEndian(header) == TTC_TAG)
        {
            Span<byte> firstOffset = stackalloc byte[4];
            if (BinaryPrimitives.ReadUInt32BigEndian(header[8..12]) == 0 || !ReadAt(stream, 12, firstOffset))
            {
                return false;
            }

            faceOffset = BinaryPrimitives.ReadUInt32BigEndian(firstOffset);
            if (!ReadAt(stream, faceOffset, header))
            {
                return false;
            }
        }

        ushort tableCount = BinaryPrimitives.ReadUInt16BigEndian(header[4..6]);
        if (tableCount == 0 || tableCount > 512)
        {
            return false;
        }

        Span<byte> record = stackalloc byte[16];
        for (int index = 0; index < tableCount; index++)
        {
            if (!ReadAt(stream, faceOffset + 12 + index * 16L, record))
            {
                return false;
            }

            if (BinaryPrimitives.ReadUInt32BigEndian(record) != OS2_TAG)
            {
                continue;
            }

            long tableOffset = BinaryPrimitives.ReadUInt32BigEndian(record[8..12]);
            Span<byte> table = stackalloc byte[64];
            if (BinaryPrimitives.ReadUInt32BigEndian(record[12..16]) < table.Length || !ReadAt(stream, tableOffset, table))
            {
                return false;
            }

            int weightClass = BinaryPrimitives.ReadUInt16BigEndian(table[4..6]);
            ushort selection = BinaryPrimitives.ReadUInt16BigEndian(table[62..64]);
            if (weightClass > 0)
            {
                // Some old fonts give the weight on a 1-9 scale.
                weight = Math.Clamp(weightClass < 10 ? weightClass * 100 : weightClass, 1, 1000);
            }

            italic = (selection & (FS_SELECTION_ITALIC | FS_SELECTION_OBLIQUE)) != 0;
            return true;
        }

        return false;
    }

    private static bool ReadAt(Stream stream, long offset, Span<byte> buffer)
    {
        stream.Position = offset;
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
}
