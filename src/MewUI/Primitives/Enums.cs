namespace Aprillz.MewUI;

/// <summary>
/// Specifies how content is stretched to fill its destination rectangle.
/// </summary>
public enum Stretch
{
    /// <summary>No scaling is applied.</summary>
    None,
    /// <summary>Content is stretched to fill the destination (aspect ratio is not preserved).</summary>
    Fill,
    /// <summary>Content is scaled uniformly to fit inside the destination (aspect ratio preserved).</summary>
    Uniform,
    /// <summary>Content is scaled uniformly to fill the destination (aspect ratio preserved; may be clipped).</summary>
    UniformToFill
}

/// <summary>
/// Specifies how <see cref="Rect"/> values used as an image view box are interpreted.
/// </summary>
public enum ImageViewBoxUnits
{
    /// <summary>View box values are specified in pixels.</summary>
    Pixels,
    /// <summary>View box values are specified relative to the image bounds (0..1).</summary>
    RelativeToBoundingBox
}

/// <summary>
/// Specifies horizontal alignment for image placement.
/// </summary>
public enum ImageAlignmentX
{
    /// <summary>Align to the left.</summary>
    Left,
    /// <summary>Align to the center.</summary>
    Center,
    /// <summary>Align to the right.</summary>
    Right
}

/// <summary>
/// Specifies vertical alignment for image placement.
/// </summary>
public enum ImageAlignmentY
{
    /// <summary>Align to the top.</summary>
    Top,
    /// <summary>Align to the center.</summary>
    Center,
    /// <summary>Align to the bottom.</summary>
    Bottom
}

/// <summary>
/// Horizontal alignment options.
/// </summary>
public enum HorizontalAlignment
{
    /// <summary>Align to the left.</summary>
    Left,
    /// <summary>Align to the center.</summary>
    Center,
    /// <summary>Align to the right.</summary>
    Right,
    /// <summary>Stretch to fill the available width.</summary>
    Stretch
}

/// <summary>
/// Vertical alignment options.
/// </summary>
public enum VerticalAlignment
{
    /// <summary>Align to the top.</summary>
    Top,
    /// <summary>Align to the center.</summary>
    Center,
    /// <summary>Align to the bottom.</summary>
    Bottom,
    /// <summary>Stretch to fill the available height.</summary>
    Stretch
}

/// <summary>
/// Font weight values.
/// </summary>
public enum FontWeight
{
    /// <summary>Thin.</summary>
    Thin = 100,
    /// <summary>Extra light.</summary>
    ExtraLight = 200,
    /// <summary>Light.</summary>
    Light = 300,
    /// <summary>Normal (regular).</summary>
    Normal = 400,
    /// <summary>Medium.</summary>
    Medium = 500,
    /// <summary>Semi bold.</summary>
    SemiBold = 600,
    /// <summary>Bold.</summary>
    Bold = 700,
    /// <summary>Extra bold.</summary>
    ExtraBold = 800,
    /// <summary>Black (heavy).</summary>
    Black = 900
}

/// <summary>
/// Font style values.
/// </summary>
public enum FontStyle
{
    /// <summary>Upright.</summary>
    Normal,
    /// <summary>The family's italic face.</summary>
    Italic
}

/// <summary>
/// Specifies a horizontal or vertical orientation.
/// </summary>
public enum Orientation
{
/// <remarks>
/// Oblique, the slanted upright a family may carry beside a true italic, has no value of its own; a slanted face is requested as <see cref="Italic"/>.
/// </remarks>
    /// <summary>Horizontal layout.</summary>
    Horizontal,
    /// <summary>Vertical layout.</summary>
    Vertical
}

/// <summary>
/// Text alignment along an axis; <see cref="Top"/> and <see cref="Bottom"/> alias <see cref="Left"/> and <see cref="Right"/> for vertical use.
/// </summary>
public enum TextAlignment
{
    Left,
    Center,
    Right,
    Top = Left,
    Bottom = Right
}

/// <summary>
/// Text wrapping options.
/// </summary>
public enum TextWrapping
{
    NoWrap,
    /// <summary>Align to the left (or top).</summary>
    Wrap,
    /// <summary>Align to the center.</summary>
    WrapWithOverflow
    /// <summary>Align to the right (or bottom).</summary>
}
    /// <summary>Align to the top; the same value as <see cref="Left"/>.</summary>

    /// <summary>Align to the bottom; the same value as <see cref="Right"/>.</summary>
/// <summary>
/// Text trimming options for text that overflows its bounds.
/// </summary>
public enum TextTrimming
{
    /// <summary>Text is not trimmed (may overflow bounds).</summary>
    None,

    /// <summary>Text stays on one line and overflows.</summary>
    /// <summary>Text is trimmed at character boundary with an ellipsis (…).</summary>
    /// <summary>Text breaks at word boundaries, and inside a word wider than the line.</summary>
    CharacterEllipsis,
    /// <summary>Text breaks at word boundaries only; a word wider than the line overflows.</summary>
}