using System.Diagnostics;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a 32-bit ARGB color.
/// </summary>
[DebuggerDisplay("Color(A={A}, R={R}, G={G}, B={B})")]
public readonly partial struct Color : IEquatable<Color>
{
    private readonly uint _value;

    /// <summary>
    /// Initializes a new instance of the <see cref="Color"/> struct from ARGB components.
    /// </summary>
    /// <param name="a">The alpha component.</param>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    public Color(byte a, byte r, byte g, byte b)
    {
        _value = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="Color"/> struct from RGB components with full opacity.
    /// </summary>
    /// <param name="r">The red component.</param>
    /// <param name="g">The green component.</param>
    /// <param name="b">The blue component.</param>
    public Color(byte r, byte g, byte b) : this(255, r, g, b) { }

    private Color(uint value)
    {
        _value = value;
    }

    /// <summary>
    /// Gets the alpha component.
    /// </summary>
    public byte A => (byte)((_value >> 24) & 0xFF);

    /// <summary>
    /// Gets the red component.
    /// </summary>
    public byte R => (byte)((_value >> 16) & 0xFF);

    /// <summary>
    /// Gets the green component.
    /// </summary>
    public byte G => (byte)((_value >> 8) & 0xFF);

    /// <summary>
    /// Gets the blue component.
    /// </summary>
    public byte B => (byte)(_value & 0xFF);

    /// <summary>
    /// Gets the color value in COLORREF format (0x00BBGGRR) for GDI.
    /// </summary>
    public uint ToCOLORREF() => ((uint)B << 16) | ((uint)G << 8) | R;

    /// <summary>
    /// Gets the color value in ARGB format (0xAARRGGBB).
    /// </summary>
    public uint ToArgb() => _value;

    /// <summary>
    /// Creates a color from ARGB components.
    /// </summary>
    public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    /// <summary>
    /// Creates a color from RGB components with full opacity.
    /// </summary>
    public static Color FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    /// <summary>
    /// Creates a color from an ARGB packed integer (0xAARRGGBB).
    /// </summary>
    /// <param name="argb">The packed ARGB value.</param>
    public static Color FromArgb(uint argb) => new(argb);

    /// <summary>
    /// Parses a color from a hex string in <c>#RRGGBB</c> or <c>#RRGGBBAA</c> format.
    /// </summary>
    /// <param name="hex">The hex string.</param>
    public static Color FromHex(string hex)
    {
        hex = hex.TrimStart('#');

        return hex.Length switch
        {
            6 => new Color(
                Convert.ToByte(hex[0..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16)),
            8 => new Color(
                Convert.ToByte(hex[0..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16),
                Convert.ToByte(hex[6..8], 16)),
            _ => throw new ArgumentException("Invalid hex color format", nameof(hex))
        };
    }

    /// <summary>
    /// Returns a copy of this color with a new alpha component.
    /// </summary>
    /// <param name="alpha">The alpha component.</param>
    public Color WithAlpha(byte alpha) => new(alpha, R, G, B);

    /// <summary>
    /// Linearly interpolates between this color and another.
    /// </summary>
    /// <param name="other">The target color.</param>
    /// <param name="t">The interpolation factor in the range [0, 1].</param>
    public Color Lerp(Color other, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return new Color(
            (byte)(A + (other.A - A) * t),
            (byte)(R + (other.R - R) * t),
            (byte)(G + (other.G - G) * t),
            (byte)(B + (other.B - B) * t)
        );
    }

    /// <summary>
    /// Alpha-composites <paramref name="overlay"/> over <paramref name="backdrop"/> using straight (unpremultiplied) alpha.
    /// </summary>
    public static Color Composite(Color backdrop, Color overlay)
    {
        if (overlay.A == 0)
        {
            return backdrop;
        }

        if (backdrop.A == 0)
        {
            return overlay;
        }

        double oa = overlay.A / 255.0;
        double ba = backdrop.A / 255.0;

        double outA = oa + (ba * (1 - oa));
        if (outA <= 0)
        {
            return Transparent;
        }

        double outR = ((overlay.R * oa) + (backdrop.R * ba * (1 - oa))) / outA;
        double outG = ((overlay.G * oa) + (backdrop.G * ba * (1 - oa))) / outA;
        double outB = ((overlay.B * oa) + (backdrop.B * ba * (1 - oa))) / outA;

        return new Color(
            (byte)Math.Clamp(Math.Round(outA * 255.0), 0, 255),
            (byte)Math.Clamp(Math.Round(outR), 0, 255),
            (byte)Math.Clamp(Math.Round(outG), 0, 255),
            (byte)Math.Clamp(Math.Round(outB), 0, 255));
    }

    /// <summary>
    /// Determines whether two colors are equal.
    /// </summary>
    public static bool operator ==(Color left, Color right) => left.Equals(right);

    /// <summary>
    /// Determines whether two colors are not equal.
    /// </summary>
    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    /// <summary>
    /// Determines whether this instance is equal to another color.
    /// </summary>
    public bool Equals(Color other) => _value == other._value;

    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    public override int GetHashCode() => _value.GetHashCode();
}
