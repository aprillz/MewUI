using System.Diagnostics;

namespace Aprillz.MewUI;

/// <summary>
/// Represents a color in HSV (Hue, Saturation, Value) color space.
/// </summary>
[DebuggerDisplay("HsvColor({H}, {S}, {V})")]
public readonly struct HsvColor(float h, float s, float v) : IEquatable<HsvColor>
{
    /// <summary>Hue in degrees [0, 360).</summary>
    public float H { get; } = h;

    /// <summary>Saturation [0, 1].</summary>
    public float S { get; } = s;

    /// <summary>Value (brightness) [0, 1].</summary>
    public float V { get; } = v;

    /// <summary>
    /// Converts RGB color to HSV.
    /// </summary>
    public static HsvColor FromRgb(byte r, byte g, byte b)
    {
        float rf = r / 255f;
        float gf = g / 255f;
        float bf = b / 255f;

        float cmax = Math.Max(rf, Math.Max(gf, bf));
        float cmin = Math.Min(rf, Math.Min(gf, bf));
        float delta = cmax - cmin;

        float h;
        if (delta == 0)
            h = 0;
        else if (cmax == rf)
            h = 60f * (((gf - bf) / delta) % 6f);
        else if (cmax == gf)
            h = 60f * (((bf - rf) / delta) + 2f);
        else
            h = 60f * (((rf - gf) / delta) + 4f);

        if (h < 0) h += 360f;

        float s = cmax == 0 ? 0 : delta / cmax;

        return new HsvColor(h, s, cmax);
    }

    /// <summary>
    /// Converts RGB color to HSV.
    /// </summary>
    public static HsvColor FromColor(Color color) => FromRgb(color.R, color.G, color.B);

    /// <summary>
    /// Converts this HSV color to an RGB <see cref="Color"/> with full opacity.
    /// </summary>
    public Color ToRgb() => HsvToRgb(H, S, V);

    /// <summary>
    /// Converts HSV components to an RGB <see cref="Color"/> with full opacity.
    /// </summary>
    public static Color HsvToRgb(float h, float s, float v)
    {
        float c = v * s;
        float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
        float m = v - c;

        float r1, g1, b1;
        switch (h)
        {
            case < 60:  r1 = c; g1 = x; b1 = 0; break;
            case < 120: r1 = x; g1 = c; b1 = 0; break;
            case < 180: r1 = 0; g1 = c; b1 = x; break;
            case < 240: r1 = 0; g1 = x; b1 = c; break;
            case < 300: r1 = x; g1 = 0; b1 = c; break;
            default:    r1 = c; g1 = 0; b1 = x; break;
        }

        return Color.FromRgb(
            (byte)Math.Clamp(Math.Round((r1 + m) * 255f), 0, 255),
            (byte)Math.Clamp(Math.Round((g1 + m) * 255f), 0, 255),
            (byte)Math.Clamp(Math.Round((b1 + m) * 255f), 0, 255));
    }

    /// <summary>
    /// Returns the pure hue color (S=1, V=1) for the given hue angle.
    /// </summary>
    public static Color HueToRgb(float h) => HsvToRgb(h, 1f, 1f);

    public bool Equals(HsvColor other) => H == other.H && S == other.S && V == other.V;
    public override bool Equals(object? obj) => obj is HsvColor other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(H, S, V);
    public static bool operator ==(HsvColor left, HsvColor right) => left.Equals(right);
    public static bool operator !=(HsvColor left, HsvColor right) => !left.Equals(right);

    public override string ToString() =>
        $"HsvColor({H:0.##}, {S:0.##}, {V:0.##})";
}
