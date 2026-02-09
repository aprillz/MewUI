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

            if (!LooksLikePng(icoData, off, len))
            {
                continue;
            }

            var blob = new byte[len];
            Buffer.BlockCopy(icoData, off, blob, 0, len);

            result.Add(sizePx: Math.Max(w, h), source: ImageSource.FromBytes(blob));
        }

        if (result._entries.Count == 0)
        {
            throw new NotSupportedException("ICO did not contain any embedded PNG images.");
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
