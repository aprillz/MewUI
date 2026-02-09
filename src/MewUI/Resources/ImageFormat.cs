namespace Aprillz.MewUI;

/// <summary>
/// Known encoded image formats. Used for hints/diagnostics.
/// </summary>
public enum ImageFormat
{
    /// <summary>Unknown format.</summary>
    Unknown = 0,
    /// <summary>Windows Bitmap (BMP).</summary>
    Bmp = 1,
    /// <summary>Portable Network Graphics (PNG).</summary>
    Png = 2,
    /// <summary>JPEG.</summary>
    Jpeg = 3,
    /// <summary>Scalable Vector Graphics (SVG).</summary>
    Svg = 4,
}
