using System.Buffers.Binary;

namespace Aprillz.MewUI.Resources;

/// <summary>
/// Decodes ICO files for use as <see cref="ImageSource"/> (e.g. in an Image control).
/// Delegates to <see cref="IconSource"/> for ICO parsing, then picks the largest entry.
/// </summary>
internal sealed class IconDecoder : IImageDecoder
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

        // Pick a large size — 256 is the max standard ICO size.
        var source = icon.Pick(256);
        if (source == null)
        {
            return false;
        }

        return ImageDecoders.TryDecode(source.EncodedBytes.Span, out bitmap);
    }
}
