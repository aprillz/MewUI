using System.Reflection;

using Aprillz.MewUI.Resources;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a multi-size icon. Picks the nearest bitmap for the requested size.
/// </summary>
public sealed class IconSource
{
    private readonly List<Entry> _entries = new();

    private sealed record Entry(int SizePx, ImageSource Source);

    /// <summary>
    /// Loads an icon from a file path.
    /// </summary>
    /// <param name="path">Path to an .ico file.</param>
    public static IconSource FromFile(string path) => FromBytes(File.ReadAllBytes(path));

    /// <summary>
    /// Loads an embedded resource from the specified assembly.
    /// AOT-friendly: avoids reflection-based discovery; the caller provides the assembly + name.
    /// </summary>
    public static IconSource FromResource(Assembly assembly, string resourceName)
    {
        ArgumentNullException.ThrowIfNull(assembly);

        if (string.IsNullOrWhiteSpace(resourceName))
        {
            throw new ArgumentException("Resource name is required.", nameof(resourceName));
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            throw new FileNotFoundException($"Embedded resource not found: '{resourceName}'", resourceName);
        }

        return FromStream(stream);
    }

    /// <summary>
    /// Loads an embedded resource using an anchor type's assembly (recommended for AOT).
    /// </summary>
    public static IconSource FromResource<TAnchor>(string resourceName) =>
        FromResource(typeof(TAnchor).Assembly, resourceName);

    /// <summary>
    /// Loads an icon from a stream.
    /// </summary>
    /// <param name="stream">Stream containing .ico bytes.</param>
    public static IconSource FromStream(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (stream is MemoryStream ms)
        {
            return FromBytes(ms.ToArray());
        }

        if (stream.CanSeek)
        {
            long len64 = stream.Length;
            if (len64 > int.MaxValue)
            {
                throw new NotSupportedException("ICO stream is too large.");
            }

            int len = (int)len64;
            var data = GC.AllocateUninitializedArray<byte>(len);
            stream.Position = 0;
            stream.ReadExactly(data);
            return FromBytes(data);
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return FromBytes(copy.ToArray());
    }

    /// <summary>
    /// Loads an icon from raw .ico bytes.
    /// </summary>
    /// <param name="icoData">The .ico file bytes.</param>
    public static IconSource FromBytes(byte[] icoData)
    {
        ArgumentNullException.ThrowIfNull(icoData);

        // ICO: ICONDIR (6 bytes) + ICONDIRENTRY (16 bytes * count)
        // We only extract embedded PNG images (common in modern .ico).
        if (icoData.Length < 6)
        {
            throw new InvalidDataException("Invalid ICO file (too small).");
        }

        ushort reserved = ReadU16(icoData, 0);
        ushort type = ReadU16(icoData, 2);
        ushort count = ReadU16(icoData, 4);

        if (reserved != 0 || type != 1 || count == 0)
        {
            throw new InvalidDataException("Invalid ICO header.");
        }

        int dirSize = 6 + 16 * count;
        if (icoData.Length < dirSize)
        {
            throw new InvalidDataException("Invalid ICO directory.");
        }

        var result = new IconSource();

        for (int i = 0; i < count; i++)
        {
            int baseOffset = 6 + 16 * i;
            int w = icoData[baseOffset + 0];
            int h = icoData[baseOffset + 1];
            if (w == 0) w = 256;
            if (h == 0) h = 256;

            uint bytesInRes = ReadU32(icoData, baseOffset + 8);
            uint imageOffset = ReadU32(icoData, baseOffset + 12);

            if (bytesInRes == 0 || imageOffset > int.MaxValue)
            {
                continue;
            }

            int off = (int)imageOffset;
            int len = (int)Math.Min(bytesInRes, int.MaxValue);
            if (off < 0 || len <= 0 || off > icoData.Length - len)
            {
                continue;
            }

            byte[]? blob;
            if (LooksLikePng(icoData, off, len))
            {
                blob = new byte[len];
                Buffer.BlockCopy(icoData, off, blob, 0, len);
            }
            else
            {
                blob = ConvertDibToBmp(icoData, off, len);
            }

            if (blob != null)
            {
                result.Add(sizePx: Math.Max(w, h), source: ImageSource.FromBytes(blob));
            }
        }

        if (result._entries.Count == 0)
        {
            throw new NotSupportedException("ICO did not contain any usable image entries.");
        }

        return result;
    }

    /// <summary>
    /// Adds an image source for a given icon size.
    /// </summary>
    /// <param name="sizePx">Icon size in pixels.</param>
    /// <param name="source">The image source.</param>
    /// <returns>This instance for chaining.</returns>
    public IconSource Add(int sizePx, ImageSource source)
    {
        if (sizePx <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizePx));
        }

        if (source == null)
        {
            ArgumentNullException.ThrowIfNull(source);
        }

        _entries.Add(new Entry(sizePx, source));
        return this;
    }

    /// <summary>
    /// Picks the closest matching image for the requested size.
    /// </summary>
    /// <param name="desiredSizePx">Desired icon size in pixels.</param>
    /// <returns>The closest match, or <see langword="null"/> if no entries exist.</returns>
    public ImageSource? Pick(int desiredSizePx)
    {
        if (_entries.Count == 0)
        {
            return null;
        }

        if (desiredSizePx <= 0)
        {
            desiredSizePx = 1;
        }

        Entry best = _entries[0];
        int bestDelta = Math.Abs(best.SizePx - desiredSizePx);

        for (int i = 1; i < _entries.Count; i++)
        {
            var e = _entries[i];
            int delta = Math.Abs(e.SizePx - desiredSizePx);
            if (delta < bestDelta)
            {
                best = e;
                bestDelta = delta;
            }
        }

        return best.Source;
    }

    /// <summary>
    /// Converts a DIB (BITMAPINFOHEADER + pixel data + AND mask) from an ICO entry
    /// into a standalone 32-bit BGRA BMP with AND mask applied as alpha.
    /// </summary>
    private static byte[]? ConvertDibToBmp(byte[] icoData, int off, int len)
    {
        if (len < 40)
        {
            return null;
        }

        int biSize = (int)ReadU32(icoData, off);
        if (biSize < 40 || biSize > len)
        {
            return null;
        }

        int biWidth = (int)ReadU32(icoData, off + 4);
        int biHeight = (int)ReadU32(icoData, off + 8);
        int actualHeight = biHeight / 2;
        ushort biBitCount = ReadU16(icoData, off + 14);

        if (biWidth <= 0 || actualHeight <= 0)
        {
            return null;
        }

        // Decompression-bomb guard: reject implausibly large declared dimensions
        // before allocating any buffers sized from them.
        if (biWidth > ImageDecoders.MAX_IMAGE_DIMENSION || actualHeight > ImageDecoders.MAX_IMAGE_DIMENSION
            || (long)biWidth * actualHeight > ImageDecoders.MAX_IMAGE_PIXEL_COUNT)
        {
            return null;
        }

        // Color table
        int colorTableEntries = 0;
        if (biBitCount <= 8)
        {
            uint biClrUsed = ReadU32(icoData, off + 32);
            colorTableEntries = biClrUsed > 0 ? (int)biClrUsed : (1 << biBitCount);
        }

        int colorTableSize = colorTableEntries * 4;
        int paletteOff = off + biSize;

        // Source pixel data
        int srcStride = ((biWidth * biBitCount + 31) / 32) * 4;
        int pixelOff = off + biSize + colorTableSize;
        int pixelDataSize = srcStride * actualHeight;

        // AND mask (1-bit, each row padded to 4 bytes)
        int andStride = ((biWidth + 31) / 32) * 4;
        int andOff = pixelOff + pixelDataSize;

        // Decode to 32-bit BGRA pixels (bottom-up, same as source)
        int dstStride = biWidth * 4;
        var pixels = new byte[dstStride * actualHeight];

        for (int y = 0; y < actualHeight; y++)
        {
            int srcRowOff = pixelOff + y * srcStride;
            int dstRowOff = y * dstStride;

            for (int x = 0; x < biWidth; x++)
            {
                int d = dstRowOff + x * 4;
                byte b, g, r, a = 0xFF;

                switch (biBitCount)
                {
                    case 1:
                    {
                        int idx = (icoData[srcRowOff + (x >> 3)] >> (7 - (x & 7))) & 1;
                        if ((uint)idx >= (uint)colorTableEntries)
                        {
                            idx = 0;
                        }

                        int p = paletteOff + idx * 4;
                        b = icoData[p]; g = icoData[p + 1]; r = icoData[p + 2];
                        break;
                    }
                    case 4:
                    {
                        int idx = (x & 1) == 0
                            ? (icoData[srcRowOff + (x >> 1)] >> 4) & 0xF
                            : icoData[srcRowOff + (x >> 1)] & 0xF;
                        if ((uint)idx >= (uint)colorTableEntries)
                        {
                            idx = 0;
                        }

                        int p = paletteOff + idx * 4;
                        b = icoData[p]; g = icoData[p + 1]; r = icoData[p + 2];
                        break;
                    }
                    case 8:
                    {
                        int idx = icoData[srcRowOff + x];
                        if ((uint)idx >= (uint)colorTableEntries)
                        {
                            idx = 0;
                        }

                        int p = paletteOff + idx * 4;
                        b = icoData[p]; g = icoData[p + 1]; r = icoData[p + 2];
                        break;
                    }
                    case 24:
                    {
                        int s = srcRowOff + x * 3;
                        b = icoData[s]; g = icoData[s + 1]; r = icoData[s + 2];
                        break;
                    }
                    case 32:
                    {
                        int s = srcRowOff + x * 4;
                        b = icoData[s]; g = icoData[s + 1]; r = icoData[s + 2]; a = icoData[s + 3];
                        break;
                    }
                    default:
                        return null;
                }

                // Apply AND mask: bit=1 means transparent
                if (andOff + (y * andStride) + (x >> 3) < off + len)
                {
                    int andBit = (icoData[andOff + y * andStride + (x >> 3)] >> (7 - (x & 7))) & 1;
                    if (andBit == 1)
                    {
                        a = 0;
                    }
                }

                pixels[d + 0] = b;
                pixels[d + 1] = g;
                pixels[d + 2] = r;
                pixels[d + 3] = a;
            }
        }

        // Build 32-bit BMP file
        int bmpHeaderSize = 14 + 40; // BITMAPFILEHEADER + BITMAPINFOHEADER (no palette)
        int bmpPixelSize = dstStride * actualHeight;
        int bmpFileSize = bmpHeaderSize + bmpPixelSize;
        var bmp = new byte[bmpFileSize];

        // BITMAPFILEHEADER
        bmp[0] = (byte)'B';
        bmp[1] = (byte)'M';
        WriteU32(bmp, 2, (uint)bmpFileSize);
        WriteU32(bmp, 10, (uint)bmpHeaderSize);

        // BITMAPINFOHEADER
        WriteU32(bmp, 14, 40);
        WriteU32(bmp, 18, (uint)biWidth);
        WriteU32(bmp, 22, (uint)actualHeight); // bottom-up
        bmp[26] = 1; bmp[27] = 0; // planes = 1
        bmp[28] = 32; bmp[29] = 0; // biBitCount = 32
        // compression = 0, rest = 0

        // Copy pixel data
        Buffer.BlockCopy(pixels, 0, bmp, bmpHeaderSize, bmpPixelSize);

        return bmp;
    }

    private static void WriteU32(byte[] data, int offset, uint value)
    {
        data[offset] = (byte)value;
        data[offset + 1] = (byte)(value >> 8);
        data[offset + 2] = (byte)(value >> 16);
        data[offset + 3] = (byte)(value >> 24);
    }

    private static ushort ReadU16(byte[] data, int offset) =>
        (ushort)(data[offset] | (data[offset + 1] << 8));

    private static uint ReadU32(byte[] data, int offset) =>
        (uint)(data[offset] |
               (data[offset + 1] << 8) |
               (data[offset + 2] << 16) |
               (data[offset + 3] << 24));

    private static bool LooksLikePng(byte[] data, int offset, int length)
    {
        if (length < 8)
        {
            return false;
        }

        return data[offset + 0] == 0x89 &&
               data[offset + 1] == 0x50 &&
               data[offset + 2] == 0x4E &&
               data[offset + 3] == 0x47 &&
               data[offset + 4] == 0x0D &&
               data[offset + 5] == 0x0A &&
               data[offset + 6] == 0x1A &&
               data[offset + 7] == 0x0A;
    }
}
