using System.Buffers.Binary;

namespace Aprillz.MewUI.Resources;

internal sealed class BmpDecoder : IImageDecoder
{
    public string Id => "bmp";

    public bool CanDecode(ReadOnlySpan<byte> encoded) =>
        encoded.Length >= 2 && encoded[0] == (byte)'B' && encoded[1] == (byte)'M';

    public bool TryDecode(ReadOnlySpan<byte> encoded, out DecodedBitmap bitmap)
    {
        // BMP loader:
        // - BITMAPFILEHEADER + BITMAPINFOHEADER (size >= 40)
        // - BI_RGB only (no compression)
        // - 1, 4, 8, 24, 32-bit
        // Output: BGRA32, top-down, alpha preserved for 32-bit.

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
        if (bpp != 1 && bpp != 4 && bpp != 8 && bpp != 24 && bpp != 32)
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

        int srcStride = ((width * bpp + 31) / 32) * 4;

        int required = pixelDataOffset + srcStride * height;
        if (required > encoded.Length)
        {
            return false;
        }

        // Read palette for indexed formats
        ReadOnlySpan<byte> palette = default;
        if (bpp <= 8)
        {
            int clrUsed = ReadInt32LE(encoded, 46);
            int paletteCount = clrUsed > 0 ? clrUsed : (1 << bpp);
            int paletteOffset = 14 + dibSize;
            int paletteSize = paletteCount * 4;
            if (paletteOffset + paletteSize > encoded.Length)
            {
                return false;
            }

            palette = encoded.Slice(paletteOffset, paletteSize);
        }

        byte[] dst = new byte[width * height * 4];
        int dstStride = width * 4;

        for (int y = 0; y < height; y++)
        {
            int srcRow = bottomUp ? (height - 1 - y) : y;
            var src = encoded.Slice(pixelDataOffset + srcRow * srcStride, srcStride);
            int dstOffset = y * dstStride;

            switch (bpp)
            {
                case 1:
                    for (int x = 0; x < width; x++)
                    {
                        int idx = (src[x >> 3] >> (7 - (x & 7))) & 1;
                        int d = dstOffset + x * 4;
                        int p = idx * 4;
                        dst[d + 0] = palette[p + 0];
                        dst[d + 1] = palette[p + 1];
                        dst[d + 2] = palette[p + 2];
                        dst[d + 3] = 0xFF;
                    }
                    break;

                case 4:
                    for (int x = 0; x < width; x++)
                    {
                        int idx = (x & 1) == 0
                            ? (src[x >> 1] >> 4) & 0xF
                            : src[x >> 1] & 0xF;
                        int d = dstOffset + x * 4;
                        int p = idx * 4;
                        dst[d + 0] = palette[p + 0];
                        dst[d + 1] = palette[p + 1];
                        dst[d + 2] = palette[p + 2];
                        dst[d + 3] = 0xFF;
                    }
                    break;

                case 8:
                    for (int x = 0; x < width; x++)
                    {
                        int idx = src[x];
                        int d = dstOffset + x * 4;
                        int p = idx * 4;
                        dst[d + 0] = palette[p + 0];
                        dst[d + 1] = palette[p + 1];
                        dst[d + 2] = palette[p + 2];
                        dst[d + 3] = 0xFF;
                    }
                    break;

                case 24:
                    for (int x = 0; x < width; x++)
                    {
                        int s = x * 3;
                        int d = dstOffset + x * 4;
                        dst[d + 0] = src[s + 0]; // B
                        dst[d + 1] = src[s + 1]; // G
                        dst[d + 2] = src[s + 2]; // R
                        dst[d + 3] = 0xFF;
                    }
                    break;

                case 32:
                    src.Slice(0, dstStride).CopyTo(dst.AsSpan(dstOffset, dstStride));
                    break;
            }
        }

        // 1/4/8/24-bit BMPs are forced opaque (the loader writes 0xFF to every alpha byte).
        // 32-bit BI_RGB technically has an "X" byte, not alpha — but we copy it through as
        // alpha, so callers may have placed real values there. Treat 32-bit as alpha-bearing
        // to be safe; the rest can take the opaque fast path.
        bool hasAlpha = bpp == 32;
        bitmap = new DecodedBitmap(width, height, BitmapPixelFormat.Bgra32, dst, hasAlpha);
        return true;
    }

    private static int ReadInt32LE(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadInt32LittleEndian(data.Slice(offset, 4));

    private static ushort ReadUInt16LE(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2));
}
