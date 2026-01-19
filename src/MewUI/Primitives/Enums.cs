namespace Aprillz.MewUI;

public enum ImageStretch
{
    None,
    Fill,
    Uniform,
    UniformToFill
}

public enum ImageViewBoxUnits
{
    Pixels,
    RelativeToBoundingBox
}

public enum ImageAlignmentX
{
    Left,
    Center,
    Right
}

public enum ImageAlignmentY
{
    Top,
    Center,
    Bottom
}

/// <summary>
/// Horizontal alignment options.
/// </summary>
public enum HorizontalAlignment
{
    Left,
    Center,
    Right,
    Stretch
}

/// <summary>
/// Vertical alignment options.
/// </summary>
public enum VerticalAlignment
{
    Top,
    Center,
    Bottom,
    Stretch
}

/// <summary>
/// Font weight values.
/// </summary>
public enum FontWeight
{
    Thin = 100,
    ExtraLight = 200,
    Light = 300,
    Normal = 400,
    Medium = 500,
    SemiBold = 600,
    Bold = 700,
    ExtraBold = 800,
    Black = 900
}

/// <summary>
/// Orientation for layout panels.
/// </summary>
public enum Orientation
{
    Horizontal,
    Vertical
}