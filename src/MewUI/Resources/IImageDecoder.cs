namespace Aprillz.MewUI.Resources;

/// <summary>
/// Decodes encoded image bytes into a <see cref="Bgra32PixelBuffer"/>.
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
    bool TryDecode(ReadOnlySpan<byte> encoded, out Bgra32PixelBuffer bitmap);

    /// <summary>
    /// Reads the image orientation from the encoded metadata without decoding pixels. Kept separate from
    /// <see cref="TryDecode(ReadOnlySpan{byte}, out Bgra32PixelBuffer)"/> so the fast byte[] pixel path is
    /// preserved and the cheap metadata scan never forces an array copy. Defaults to
    /// <see cref="ImageOrientation.Identity"/>; formats without orientation metadata need not override it.
    /// </summary>
    ImageOrientation ReadOrientation(ReadOnlySpan<byte> encoded) => ImageOrientation.Identity;
}
