using System.Buffers.Binary;

namespace Aprillz.MewUI.Resources;

internal sealed class BmpDecoder : IImageDecoder
{
    public string Id => "bmp";

    public bool CanDecode(ReadOnlySpan<byte> encoded) =>
        encoded.Length >= 2 && encoded[0] == (byte)'B' && encoded[1] == (byte)'M';

    public bool TryDecode(ReadOnlySpan<byte> encoded, out DecodedBitmap bitmap)
    {
        // Minimal BMP loader:
        // - BITMAPFILEHEADER + BITMAPINFOHEADER (size 40)
        // - BI_RGB only (no compression)
        // - 24-bit and 32-bit only
        // Output: BGRA32, top-down, alpha preserved for 32-bit, 24-bit alpha=255.

        bitmap = default;

        if (encoded.Length < 14 + 40)
        {
            return false;
        }

        if (!CanDecode(encoded))
        {
            return false;
        }

        int pixelDataOffset = ReadInt32LE(encoded, 10);
        int dibSize = ReadInt32LE(encoded, 14);
        if (dibSize < 40)
        {
            return false;
        }

        int width = ReadInt32LE(encoded, 18);
        int heightSigned = ReadInt32LE(encoded, 22);
        if (width <= 0 || heightSigned == 0)
        {
            return false;
        }

        bool bottomUp = heightSigned > 0;
        int height = Math.Abs(heightSigned);

        ushort planes = ReadUInt16LE(encoded, 26);
        if (planes != 1)
        {
            return false;
        }

        ushort bpp = ReadUInt16LE(encoded, 28);
        if (bpp != 24 && bpp != 32)
        {
            return false;
        }

        int compression = ReadInt32LE(encoded, 30);
        if (compression != 0) // BI_RGB
        {
            return false;
        }

        if (pixelDataOffset < 0 || pixelDataOffset >= encoded.Length)
        {
            return false;
        }

        int srcStride = bpp == 24
            ? ((width * 3 + 3) / 4) * 4
            : width * 4;

        int required = pixelDataOffset + srcStride * height;
        if (required > encoded.Length)
        {
            return false;
        }

        byte[] dst = new byte[width * height * 4];

        int dstStride = width * 4;
        for (int y = 0; y < height; y++)
        {
            int srcRow = bottomUp ? (height - 1 - y) : y;
            var src = encoded.Slice(pixelDataOffset + srcRow * srcStride, srcStride);
            int dstOffset = y * dstStride;

            if (bpp == 24)
            {
                for (int x = 0; x < width; x++)
                {
                    int s = x * 3;
                    int d = dstOffset + x * 4;
                    dst[d + 0] = src[s + 0]; // B
                    dst[d + 1] = src[s + 1]; // G
                    dst[d + 2] = src[s + 2]; // R
                    dst[d + 3] = 0xFF;
                }
            }
            else
            {
                // BGRA in file (common). We preserve alpha byte.
                src.Slice(0, dstStride).CopyTo(dst.AsSpan(dstOffset, dstStride));
            }
        }

        bitmap = new DecodedBitmap(width, height, BitmapPixelFormat.Bgra32, dst);
        return true;
    }

    private static int ReadInt32LE(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));

    private static ushort ReadUInt16LE(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
}
