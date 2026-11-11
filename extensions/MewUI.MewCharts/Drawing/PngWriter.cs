using System.Buffers.Binary;
using System.IO.Compression;

namespace Aprillz.MewUI.MewCharts.Drawing;

// Writes 8-bit RGBA PNG files: an IHDR, one zlib-compressed IDAT with no row filters, and IEND.
internal static class PngWriter
{
    private static readonly byte[] _signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly uint[] _crcTable = BuildCrcTable();

    /// <summary>Encodes BGRA rows as a PNG into <paramref name="stream"/>, un-premultiplying them first when <paramref name="premultiplied"/> is true.</summary>
    public static void Write(Stream stream, ReadOnlySpan<byte> bgra, int width, int height, int strideBytes, bool premultiplied)
    {
        stream.Write(_signature);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;  // bit depth
        header[9] = 6;  // color type: RGBA
        header[10] = 0; // deflate
        header[11] = 0; // adaptive filtering, filter type None on every row
        header[12] = 0; // no interlace
        WriteChunk(stream, "IHDR"u8, header);

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            var row = new byte[1 + width * 4];
            for (int y = 0; y < height; y++)
            {
                var source = bgra.Slice(y * strideBytes, width * 4);
                row[0] = 0;
                for (int x = 0; x < width; x++)
                {
                    byte blue = source[x * 4];
                    byte green = source[x * 4 + 1];
                    byte red = source[x * 4 + 2];
                    byte alpha = source[x * 4 + 3];
                    if (premultiplied && alpha is > 0 and < 255)
                    {
                        red = (byte)Math.Min(255, red * 255 / alpha);
                        green = (byte)Math.Min(255, green * 255 / alpha);
                        blue = (byte)Math.Min(255, blue * 255 / alpha);
                    }

                    int offset = 1 + x * 4;
                    row[offset] = red;
                    row[offset + 1] = green;
                    row[offset + 2] = blue;
                    row[offset + 3] = alpha;
                }
                zlib.Write(row);
            }
        }

        WriteChunk(stream, "IDAT"u8, compressed.GetBuffer().AsSpan(0, (int)compressed.Length));
        WriteChunk(stream, "IEND"u8, ReadOnlySpan<byte>.Empty);
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, data.Length);
        stream.Write(buffer);
        stream.Write(type);
        stream.Write(data);

        uint crc = Update(0xFFFFFFFFu, type);
        crc = Update(crc, data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(buffer, crc);
        stream.Write(buffer);
    }

    private static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
        {
            crc = _crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }
        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (uint index = 0; index < 256; index++)
        {
            uint value = index;
            for (int bit = 0; bit < 8; bit++)
            {
                value = (value & 1) != 0 ? 0xEDB88320u ^ (value >> 1) : value >> 1;
            }
            table[index] = value;
        }
        return table;
    }
}
