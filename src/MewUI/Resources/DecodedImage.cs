namespace Aprillz.MewUI.Resources;

/// <summary>
/// A decoded image: the raw BGRA pixels plus the orientation parsed from the source metadata. The
/// pixels keep their raw (as-decoded) dimensions and order; <see cref="Orientation"/> is advisory and
/// applied by the consumer, not baked into the buffer.
/// </summary>
/// <param name="Pixels">Raw decoded BGRA pixels.</param>
/// <param name="Orientation">Orientation parsed from the source, or <see cref="ImageOrientation.Identity"/> when none.</param>
public readonly record struct DecodedImage(
    Bgra32PixelBuffer Pixels,
    ImageOrientation Orientation);
