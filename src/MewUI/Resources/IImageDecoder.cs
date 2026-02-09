namespace Aprillz.MewUI.Resources;

/// <summary>
/// Decodes encoded image bytes into a <see cref="DecodedBitmap"/>.
/// </summary>
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

    /// <summary>
    /// Attempts to decode the provided encoded bytes.
    /// </summary>
    /// <param name="encoded">Encoded image bytes.</param>
    /// <param name="bitmap">Decoded bitmap on success.</param>
    /// <returns><see langword="true"/> if decoding succeeded; otherwise, <see langword="false"/>.</returns>
    bool TryDecode(ReadOnlySpan<byte> encoded, out DecodedBitmap bitmap);
}
