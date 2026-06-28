namespace Aprillz.MewUI.Resources;

/// <summary>
/// Normalized image orientation. The values match the EXIF Orientation tag (1-8) so a parsed EXIF
/// value maps without an intermediate table, and the diagonal cases (5/7) stay unambiguous - each is
/// a single named transform rather than a rotation+mirror pair whose application order could differ.
/// </summary>
/// <remarks>
/// Raw decoded pixels are never rotated; this value travels as metadata and is applied at draw/layout
/// time by the consumer (see <see cref="OrientationTransform"/>). Unknown or invalid values normalize
/// to <see cref="Normal"/>.
/// </remarks>
public enum ImageOrientation : byte
{
    /// <summary>No transform. Row 0 is top, column 0 is left.</summary>
    Normal = 1,

    /// <summary>Alias of <see cref="Normal"/> for readability where "no orientation" is meant.</summary>
    Identity = Normal,

    /// <summary>Mirror across the vertical axis (flip left-right).</summary>
    MirrorHorizontal = 2,

    /// <summary>Rotate 180 degrees.</summary>
    Rotate180 = 3,

    /// <summary>Mirror across the horizontal axis (flip top-bottom).</summary>
    MirrorVertical = 4,

    /// <summary>Reflect across the main (top-left to bottom-right) diagonal.</summary>
    Transpose = 5,

    /// <summary>Rotate 90 degrees clockwise.</summary>
    Rotate90 = 6,

    /// <summary>Reflect across the anti (top-right to bottom-left) diagonal.</summary>
    Transverse = 7,

    /// <summary>Rotate 90 degrees counter-clockwise (270 clockwise).</summary>
    Rotate270 = 8,
}
