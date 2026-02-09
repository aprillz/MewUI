namespace Aprillz.MewUI;

/// <summary>
/// Specifies how an image is stretched to fill its destination rectangle.
/// </summary>
public enum ImageStretch
{
    /// <summary>No scaling is applied.</summary>
    None,
    /// <summary>The image is stretched to fill the destination (aspect ratio is not preserved).</summary>
    Fill,
    /// <summary>The image is scaled uniformly to fit inside the destination (aspect ratio preserved).</summary>
    Uniform,
    /// <summary>The image is scaled uniformly to fill the destination (aspect ratio preserved; may be clipped).</summary>
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
/// Orientation for layout panels.
/// </summary>
public enum Orientation
{
    /// <summary>Horizontal layout.</summary>
    Horizontal,
    /// <summary>Vertical layout.</summary>
    Vertical
}

/// <summary>
/// Text horizontal/vertical alignment options.
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
    Wrap,
    WrapWithOverflow
}