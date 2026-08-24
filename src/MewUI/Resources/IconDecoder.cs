using System.Buffers.Binary;

namespace Aprillz.MewUI.Resources;

/// <summary>
/// Decodes ICO files for use as <see cref="ImageSource"/> (e.g. in an Image control).
/// Delegates to <see cref="IconSource"/> for ICO parsing, then picks the largest entry.
/// </summary>
internal sealed class IconDecoder : IImageDecoder, IImageMetadataDecoder
{
    public string Id => "ico";

    public bool CanDecode(ReadOnlySpan<byte> encoded)
    {
        // ICO signature: 00 00 01 00 (reserved=0, type=1 for icon)
        return encoded.Length >= 6
            && encoded[0] == 0 && encoded[1] == 0
            && encoded[2] == 1 && encoded[3] == 0
            && BinaryPrimitives.ReadUInt16LittleEndian(encoded.Slice(4)) > 0;
    }

    public bool TryDecode(ReadOnlySpan<byte> encoded, out Bgra32PixelBuffer bitmap)
    {
        bitmap = default;

        if (!CanDecode(encoded))
        {
            return false;
        }

        // IconSource already handles full ICO parsing (PNG entries + DIB→BMP conversion).
        // Pick the largest entry and decode it.
        IconSource icon;
        try
        {
            icon = IconSource.FromBytes(encoded.ToArray());
        }
        catch
        {
            return false;
        }

        // Pick a large size - 256 is the max standard ICO size.
        var source = icon.Pick(256);
        if (source == null)
        {
            return false;
        }

        return ImageDecoders.TryDecode(source.EncodedBytes.Span, out bitmap);
    }

    public bool TryReadMetadata(ReadOnlySpan<byte> encoded, out ImageMetadata metadata)
    {
        metadata = default;
        if (!CanDecode(encoded))
        {
            return false;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(encoded.Slice(4, 2));
        if (count <= 0 || count > (encoded.Length - 6) / 16)
        {
            return false;
        }

        int bestWidth = 0;
        int bestHeight = 0;
        long bestArea = 0;
        for (int i = 0; i < count; i++)
        {
            int entryOffset = 6 + i * 16;
            int width = encoded[entryOffset] == 0 ? 256 : encoded[entryOffset];
            int height = encoded[entryOffset + 1] == 0 ? 256 : encoded[entryOffset + 1];
            long area = (long)width * height;
            if (area > bestArea)
            {
                bestWidth = width;
                bestHeight = height;
                bestArea = area;
            }
        }

        if (!ImageMetadataValidation.IsValidSize(bestWidth, bestHeight))
        {
            return false;
        }

        metadata = new ImageMetadata(bestWidth, bestHeight, ImageOrientation.Identity, HasAlpha: true);
        return true;
    }
}
