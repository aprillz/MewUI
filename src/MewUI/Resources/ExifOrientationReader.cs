namespace Aprillz.MewUI.Resources;

/// <summary>
/// Reads the EXIF Orientation tag (0x0112) straight from JPEG bytes without decoding pixels. Every
/// malformed, truncated, missing, or unsupported case returns <see cref="ImageOrientation.Normal"/>;
/// parsing never throws and never turns into a decode failure.
/// </summary>
internal static class ExifOrientationReader
{
    private const int OrientationTag = 0x0112;
    private const int TypeShort = 3;
    private const int TiffHeaderSize = 8;

    // "Exif\0\0"
    private static ReadOnlySpan<byte> ExifIdentifier => [0x45, 0x78, 0x69, 0x66, 0x00, 0x00];

    /// <summary>
    /// Scans JPEG markers for the first valid EXIF Orientation, stopping at start-of-scan or end-of-image.
    /// </summary>
    public static ImageOrientation ReadJpegOrientation(ReadOnlySpan<byte> jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)   // SOI
        {
            return ImageOrientation.Normal;
        }

        int pos = 2;
        while (pos + 1 < jpeg.Length)
        {
            if (jpeg[pos] != 0xFF)
            {
                return ImageOrientation.Normal;   // lost marker alignment
            }

            // Skip 0xFF fill bytes preceding the marker code.
            int markerStart = pos;
            while (markerStart < jpeg.Length && jpeg[markerStart] == 0xFF)
            {
                markerStart++;
            }
            if (markerStart >= jpeg.Length)
            {
                return ImageOrientation.Normal;
            }

            byte marker = jpeg[markerStart];
            pos = markerStart + 1;

            if (marker is 0xD9 /*EOI*/ or 0xDA /*SOS: scan data begins*/)
            {
                return ImageOrientation.Normal;
            }
            if (marker == 0x01 || marker is >= 0xD0 and <= 0xD7)
            {
                continue;   // TEM / RSTn: standalone, no length payload
            }

            // Length-prefixed segment: 2-byte big-endian length that includes the length bytes.
            if (pos + 2 > jpeg.Length)
            {
                return ImageOrientation.Normal;
            }
            int segLength = (jpeg[pos] << 8) | jpeg[pos + 1];
            if (segLength < 2)
            {
                return ImageOrientation.Normal;
            }
            int dataStart = pos + 2;
            int dataLength = segLength - 2;
            if (dataStart + dataLength > jpeg.Length)
            {
                return ImageOrientation.Normal;   // truncated segment
            }

            if (marker == 0xE1   // APP1
                && TryReadExifOrientation(jpeg.Slice(dataStart, dataLength), out var orientation))
            {
                return orientation;
            }

            pos = dataStart + dataLength;
        }

        return ImageOrientation.Normal;
    }

    // segment = APP1 payload (after the length bytes): expects "Exif\0\0" then a TIFF block.
    private static bool TryReadExifOrientation(ReadOnlySpan<byte> segment, out ImageOrientation orientation)
    {
        orientation = ImageOrientation.Normal;

        if (segment.Length < ExifIdentifier.Length + TiffHeaderSize
            || !segment.Slice(0, ExifIdentifier.Length).SequenceEqual(ExifIdentifier))
        {
            return false;
        }

        var tiff = segment.Slice(ExifIdentifier.Length);   // offsets below are relative to this TIFF base

        bool little;
        if (tiff[0] == 0x49 && tiff[1] == 0x49) little = true;        // "II" little-endian
        else if (tiff[0] == 0x4D && tiff[1] == 0x4D) little = false;  // "MM" big-endian
        else return false;

        if (ReadU16(tiff, 2, little) != 42)
        {
            return false;
        }

        uint ifd0 = ReadU32(tiff, 4, little);
        if (ifd0 < TiffHeaderSize || ifd0 > (uint)tiff.Length || ifd0 + 2 > (uint)tiff.Length)
        {
            return false;
        }

        int entryCount = ReadU16(tiff, (int)ifd0, little);
        int entriesStart = (int)ifd0 + 2;
        if ((long)entriesStart + (long)entryCount * 12 > tiff.Length)
        {
            return false;
        }

        for (int i = 0; i < entryCount; i++)
        {
            int entry = entriesStart + i * 12;
            if (ReadU16(tiff, entry, little) != OrientationTag)
            {
                continue;
            }

            int type = ReadU16(tiff, entry + 2, little);
            uint count = ReadU32(tiff, entry + 4, little);
            if (type != TypeShort || count != 1)
            {
                return false;
            }

            int value = ReadU16(tiff, entry + 8, little);   // SHORT value lives in the first 2 bytes of the field
            if (value is >= 1 and <= 8)
            {
                orientation = (ImageOrientation)value;
                return true;
            }

            return false;
        }

        return false;
    }

    private static int ReadU16(ReadOnlySpan<byte> data, int offset, bool little) =>
        little
            ? data[offset] | (data[offset + 1] << 8)
            : (data[offset] << 8) | data[offset + 1];

    private static uint ReadU32(ReadOnlySpan<byte> data, int offset, bool little) =>
        little
            ? (uint)(data[offset] | (data[offset + 1] << 8) | (data[offset + 2] << 16) | (data[offset + 3] << 24))
            : (uint)((data[offset] << 24) | (data[offset + 1] << 16) | (data[offset + 2] << 8) | data[offset + 3]);
}
