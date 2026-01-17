namespace Aprillz.MewUI.Resources;

public interface IImageDecoder
{
    /// <summary>
    /// A stable identifier for this decoder (e.g. "png", "jpeg", "webp").
    /// Used for diagnostics only; decoding is capability-based via <see cref="CanDecode"/>.
    /// </summary>
    string Id { get; }

    /// <summary>
    /// Returns true if this decoder can handle the provided encoded bytes.
    /// Should be fast and avoid allocations.
    /// </summary>
    bool CanDecode(ReadOnlySpan<byte> encoded);

    bool TryDecode(ReadOnlySpan<byte> encoded, out DecodedBitmap bitmap);
}
