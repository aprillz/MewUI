namespace Aprillz.MewUI.Primitives;

/// <summary>
/// Represents a 32-bit ARGB color.
/// </summary>
public readonly struct Color : IEquatable<Color>
{
    private readonly uint _value;

    public Color(byte a, byte r, byte g, byte b)
    {
        _value = ((uint)a << 24) | ((uint)r << 16) | ((uint)g << 8) | b;
    }

    public Color(byte r, byte g, byte b) : this(255, r, g, b) { }

    private Color(uint value)
    {
        _value = value;
    }

    public byte A => (byte)((_value >> 24) & 0xFF);

    public byte R => (byte)((_value >> 16) & 0xFF);

    public byte G => (byte)((_value >> 8) & 0xFF);

    public byte B => (byte)(_value & 0xFF);

    /// <summary>
    /// Gets the color value in COLORREF format (0x00BBGGRR) for GDI.
    /// </summary>
    public uint ToCOLORREF() => ((uint)B << 16) | ((uint)G << 8) | R;

    /// <summary>
    /// Gets the color value in ARGB format (0xAARRGGBB).
    /// </summary>
    public uint ToArgb() => _value;

    public static Color FromArgb(byte a, byte r, byte g, byte b) => new(a, r, g, b);

    public static Color FromRgb(byte r, byte g, byte b) => new(255, r, g, b);

    public static Color FromArgb(uint argb) => new(argb);

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

    public Color WithAlpha(byte alpha) => new(alpha, R, G, B);

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

    // Common colors
    public static Color Transparent => new(0, 0, 0, 0);

    public static Color Black => new(0, 0, 0);

    public static Color White => new(255, 255, 255);

    public static Color Red => new(255, 0, 0);

    public static Color Green => new(0, 128, 0);

    public static Color Blue => new(0, 0, 255);

    public static Color Yellow => new(255, 255, 0);

    public static Color Cyan => new(0, 255, 255);

    public static Color Magenta => new(255, 0, 255);

    public static Color Gray => new(128, 128, 128);

    public static Color LightGray => new(211, 211, 211);

    public static Color DarkGray => new(64, 64, 64);

    public static Color Orange => new(255, 165, 0);

    public static Color Purple => new(128, 0, 128);

    public static Color Pink => new(255, 192, 203);

    public static Color Brown => new(139, 69, 19);

    // Windows system-like colors
    public static Color WindowBackground => new(240, 240, 240);

    public static Color WindowText => new(0, 0, 0);

    public static Color ControlBackground => new(255, 255, 255);

    public static Color ControlBorder => new(173, 173, 173);

    public static Color ButtonFace => new(225, 225, 225);

    public static Color ButtonText => new(0, 0, 0);

    public static Color Highlight => new(0, 120, 215);

    public static Color HighlightText => new(255, 255, 255);

    public static bool operator ==(Color left, Color right) => left.Equals(right);

    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    public bool Equals(Color other) => _value == other._value;

    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    public override int GetHashCode() => _value.GetHashCode();

    public override string ToString() => $"Color(A={A}, R={R}, G={G}, B={B})";
}