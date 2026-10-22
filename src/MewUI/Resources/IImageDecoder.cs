namespace Aprillz.MewUI.Resources;

/// <summary>
/// Decodes encoded image bytes into a <see cref="Bgra32PixelBuffer"/>.
/// </summary>
public interface IImageDecoder
{
    /// <summary>
    /// Gets a stable identifier for this decoder (for example "png", "jpeg", "webp"), used for diagnostics only.
    /// </summary>
    /// <remarks>
    /// Decoding is capability-based via <see cref="CanDecode"/>.
    /// </remarks>
    string Id { get; }

    /// <summary>
    /// Returns whether this decoder can handle the provided encoded bytes; implementations should be fast and
    /// avoid allocations.
    /// </summary>
    bool CanDecode(ReadOnlySpan<byte> encoded);

    /// <summary>
    /// Attempts to decode the provided encoded bytes.
    /// </summary>
    /// <param name="encoded">Encoded image bytes.</param>
    /// <param name="bitmap">Decoded bitmap on success.</param>
    /// <returns><see langword="true"/> if decoding succeeded; otherwise, <see langword="false"/>.</returns>
    bool TryDecode(ReadOnlySpan<byte> encoded, out Bgra32PixelBuffer bitmap);

    /// <summary>
    /// Reads the image orientation from the encoded metadata without decoding pixels;
    /// <see cref="ImageOrientation.Identity"/> by default.
    /// </summary>
    /// <remarks>
    /// Kept separate from <see cref="TryDecode(ReadOnlySpan{byte}, out Bgra32PixelBuffer)"/> so the fast byte[]
    /// pixel path is preserved and the cheap metadata scan never forces an array copy. Formats without
    /// orientation metadata need not override it.
    /// </remarks>
    ImageOrientation ReadOrientation(ReadOnlySpan<byte> encoded) => ImageOrientation.Identity;
}
