using System.Buffers.Binary;
using System.IO.Compression;

namespace Aprillz.MewUI.Resources;

internal sealed class PngDecoder : IImageDecoder
{
    public string Id => "png";

    public bool CanDecode(ReadOnlySpan<byte> encoded)
    {
        // PNG signature
        if (encoded.Length < 8)
        {
            return false;
        }

        ReadOnlySpan<byte> sig = stackalloc byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A };
        return encoded.Slice(0, 8).SequenceEqual(sig);
    }

    public bool TryDecode(ReadOnlySpan<byte> encoded, out DecodedBitmap bitmap)
    {
        bitmap = default;

        if (!CanDecode(encoded))
        {
            return false;
        }

        int width = 0;
        int height = 0;
        byte bitDepth = 0;
        byte colorType = 0;
        byte interlace = 0;

        byte[]? palette = null;       // RGB triples
        byte[]? paletteAlpha = null;  // optional alpha per entry

        using var idat = new MemoryStream();

        int offset = 8;
        while (offset + 12 <= encoded.Length)
        {
            int length = ReadInt32BE(encoded, offset);
            offset += 4;
            if (length < 0 || offset + 4 + length + 4 > encoded.Length)
            {
                return false;
            }

            var type = encoded.Slice(offset, 4);
            offset += 4;

            var data = encoded.Slice(offset, length);
            offset += length;

            // Skip CRC
            offset += 4;

            if (type.SequenceEqual("IHDR"u8))
            {
                if (length != 13)
                {
                    return false;
                }

                width = ReadInt32BE(data, 0);
                height = ReadInt32BE(data, 4);
                bitDepth = data[8];
                colorType = data[9];
                byte compression = data[10];
                byte filter = data[11];
                interlace = data[12];

                if (width <= 0 || height <= 0)
                {
                    return false;
                }

                if (compression != 0 || filter != 0)
                {
                    return false;
                }

                if (interlace != 0)
                {
                    return false; // no Adam7 yet
                }

                if (bitDepth != 8)
                {
                    return false; // keep it simple (fast path)
                }

                // Support: grayscale(0), rgb(2), indexed(3), rgba(6)
                if (colorType != 0 && colorType != 2 && colorType != 3 && colorType != 6)
                {
                    return false;
                }
            }
            else if (type.SequenceEqual("PLTE"u8))
            {
                if (length == 0 || (length % 3) != 0)
                {
                    return false;
                }

                palette = data.ToArray();
            }
            else if (type.SequenceEqual("tRNS"u8))
            {
                // For indexed: alpha table up to palette entries.
                if (colorType == 3)
                {
                    paletteAlpha = data.ToArray();
                }
                // Other color types ignored in this minimal loader.
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                idat.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                break;
            }
        }

        if (width == 0 || height == 0)
        {
            return false;
        }

        int srcBpp = colorType switch
        {
            0 => 1,
            2 => 3,
            3 => 1,
            6 => 4,
            _ => 0
        };
        if (srcBpp == 0)
        {
            return false;
        }

        if (colorType == 3 && palette == null)
        {
            return false;
        }

        int rowBytes = checked(width * srcBpp);
        int expected = checked(height * (1 + rowBytes));

        byte[] inflated;
        try
        {
            inflated = InflateZlib(idat.ToArray(), expected);
        }
        catch
        {
            return false;
        }

        if (inflated.Length < expected)
        {
            return false;
        }

        byte[] raw = new byte[checked(height * rowBytes)];
        Unfilter(inflated, raw, width, height, srcBpp, rowBytes);

        byte[] dst = new byte[checked(width * height * 4)];
        DecodeToBgra(dst, raw, width, height, colorType, palette, paletteAlpha);

        bitmap = new DecodedBitmap(width, height, BitmapPixelFormat.Bgra32, dst);
        return true;
    }

    private static byte[] InflateZlib(byte[] zlibData, int expectedSize)
    {
        using var ms = new MemoryStream(zlibData);
        using var z = new ZLibStream(ms, CompressionMode.Decompress, false);
        using var outMs = expectedSize > 0 ? new MemoryStream(expectedSize) : new MemoryStream();
        z.CopyTo(outMs);
        return outMs.ToArray();
    }

    private static void Unfilter(byte[] filtered, byte[] raw, int width, int height, int bpp, int rowBytes)
    {
        int src = 0;
        int dst = 0;

        for (int y = 0; y < height; y++)
        {
            byte filter = filtered[src++];
            var cur = filtered.AsSpan(src, rowBytes);
            var outRow = raw.AsSpan(dst, rowBytes);

            switch (filter)
            {
                case 0: // None
                    cur.CopyTo(outRow);
                    break;

                case 1: // Sub
                    for (int i = 0; i < rowBytes; i++)
                    {
                        byte left = i >= bpp ? outRow[i - bpp] : (byte)0;
                        outRow[i] = (byte)(cur[i] + left);
                    }
                    break;

                case 2: // Up
                    for (int i = 0; i < rowBytes; i++)
                    {
                        byte up = y > 0 ? raw[(y - 1) * rowBytes + i] : (byte)0;
                        outRow[i] = (byte)(cur[i] + up);
                    }
                    break;

                case 3: // Average
                    for (int i = 0; i < rowBytes; i++)
                    {
                        byte left = i >= bpp ? outRow[i - bpp] : (byte)0;
                        byte up = y > 0 ? raw[(y - 1) * rowBytes + i] : (byte)0;
                        outRow[i] = (byte)(cur[i] + ((left + up) >> 1));
                    }
                    break;

                case 4: // Paeth
                    for (int i = 0; i < rowBytes; i++)
                    {
                        byte a = i >= bpp ? outRow[i - bpp] : (byte)0;
                        byte b = y > 0 ? raw[(y - 1) * rowBytes + i] : (byte)0;
                        byte c = (y > 0 && i >= bpp) ? raw[(y - 1) * rowBytes + i - bpp] : (byte)0;
                        outRow[i] = (byte)(cur[i] + Paeth(a, b, c));
                    }
                    break;

                default:
                    // Unsupported filter
                    cur.CopyTo(outRow);
                    break;
            }

            src += rowBytes;
            dst += rowBytes;
        }
    }

    private static byte Paeth(byte a, byte b, byte c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a);
        int pb = Math.Abs(p - b);
        int pc = Math.Abs(p - c);
        if (pa <= pb && pa <= pc)
        {
            return a;
        }

        if (pb <= pc)
        {
            return b;
        }

        return c;
    }

    private static void DecodeToBgra(
        byte[] dstBgra,
        byte[] raw,
        int width,
        int height,
        byte colorType,
        byte[]? palette,
        byte[]? paletteAlpha)
    {
        int di = 0;
        int si = 0;

        if (colorType == 6) // RGBA
        {
            for (int i = 0; i < width * height; i++)
            {
                byte r = raw[si + 0];
                byte g = raw[si + 1];
                byte b = raw[si + 2];
                byte a = raw[si + 3];
                dstBgra[di + 0] = b;
                dstBgra[di + 1] = g;
                dstBgra[di + 2] = r;
                dstBgra[di + 3] = a;
                si += 4;
                di += 4;
            }
            return;
        }

        if (colorType == 2) // RGB
        {
            for (int i = 0; i < width * height; i++)
            {
                byte r = raw[si + 0];
                byte g = raw[si + 1];
                byte b = raw[si + 2];
                dstBgra[di + 0] = b;
                dstBgra[di + 1] = g;
                dstBgra[di + 2] = r;
                dstBgra[di + 3] = 0xFF;
                si += 3;
                di += 4;
            }
            return;
        }

        if (colorType == 0) // Grayscale
        {
            for (int i = 0; i < width * height; i++)
            {
                byte v = raw[si++];
                dstBgra[di + 0] = v;
                dstBgra[di + 1] = v;
                dstBgra[di + 2] = v;
                dstBgra[di + 3] = 0xFF;
                di += 4;
            }
            return;
        }

        // Indexed
        if (palette == null)
        {
            return;
        }

        int entries = palette.Length / 3;
        for (int i = 0; i < width * height; i++)
        {
            int idx = raw[si++];
            if ((uint)idx >= (uint)entries)
            {
                idx = 0;
            }

            int p = idx * 3;
            byte r = palette[p + 0];
            byte g = palette[p + 1];
            byte b = palette[p + 2];
            byte a = 0xFF;
            if (paletteAlpha != null && (uint)idx < (uint)paletteAlpha.Length)
            {
                a = paletteAlpha[idx];
            }

            dstBgra[di + 0] = b;
            dstBgra[di + 1] = g;
            dstBgra[di + 2] = r;
            dstBgra[di + 3] = a;
            di += 4;
        }
    }

    private static int ReadInt32BE(ReadOnlySpan<byte> data, int offset)
        => BinaryPrimitives.ReadInt32BigEndian(data.Slice(offset, 4));
}
