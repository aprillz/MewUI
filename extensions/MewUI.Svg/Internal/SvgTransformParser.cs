using System.Globalization;

namespace Aprillz.MewUI.Svg.Internal;

/// <summary>
/// Parses an SVG transform attribute string into a <see cref="SvgMatrix"/>.
/// Handles matrix(), translate(), scale(), rotate(), skewX(), skewY().
/// Multiple transforms are concatenated left-to-right.
/// </summary>
internal static class SvgTransformParser
{
    public static SvgMatrix Parse(ReadOnlySpan<char> value)
    {
        value = value.Trim();
        var result = SvgMatrix.Identity;

        while (!value.IsEmpty)
        {
            value = value.TrimStart();
            if (value.IsEmpty) break;

            // Find function name (up to '(')
            int paren = value.IndexOf('(');
            if (paren < 0) break;

            var name = value[..paren].Trim();
            value = value[(paren + 1)..];

            // Find closing paren
            int close = value.IndexOf(')');
            ReadOnlySpan<char> args = close >= 0 ? value[..close] : value;
            value = close >= 0 ? value[(close + 1)..] : ReadOnlySpan<char>.Empty;

            // Skip comma/whitespace between functions
            value = value.TrimStart(" \t\r\n,");

            SvgMatrix local;
            if (name.Equals("matrix", StringComparison.OrdinalIgnoreCase))
                local = ParseMatrix(args);
            else if (name.Equals("translate", StringComparison.OrdinalIgnoreCase))
                local = ParseTranslate(args);
            else if (name.Equals("scale", StringComparison.OrdinalIgnoreCase))
                local = ParseScale(args);
            else if (name.Equals("rotate", StringComparison.OrdinalIgnoreCase))
                local = ParseRotate(args);
            else if (name.Equals("skewX", StringComparison.OrdinalIgnoreCase))
                local = SvgMatrix.SkewX(ReadFirst(args) * Math.PI / 180.0);
            else if (name.Equals("skewY", StringComparison.OrdinalIgnoreCase))
                local = SvgMatrix.SkewY(ReadFirst(args) * Math.PI / 180.0);
            else
                continue;

            // SVG: result = result * local  (left to right concatenation)
            result = result.Append(local);
        }

        return result;
    }

    private static SvgMatrix ParseMatrix(ReadOnlySpan<char> args)
    {
        Span<double> v = stackalloc double[6];
        ReadNumbers(args, v);
        return new SvgMatrix(v[0], v[1], v[2], v[3], v[4], v[5]);
    }

    private static SvgMatrix ParseTranslate(ReadOnlySpan<char> args)
    {
        Span<double> v = stackalloc double[2];
        ReadNumbers(args, v);
        return SvgMatrix.Translate(v[0], v[1]);
    }

    private static SvgMatrix ParseScale(ReadOnlySpan<char> args)
    {
        Span<double> v = stackalloc double[2];
        int count = ReadNumbers(args, v);
        double sy = count >= 2 ? v[1] : v[0];
        return SvgMatrix.Scale(v[0], sy);
    }

    private static SvgMatrix ParseRotate(ReadOnlySpan<char> args)
    {
        Span<double> v = stackalloc double[3];
        int count = ReadNumbers(args, v);
        double angle = v[0] * Math.PI / 180.0;
        if (count >= 3)
            return SvgMatrix.RotateAround(angle, v[1], v[2]);
        return SvgMatrix.Rotate(angle);
    }

    private static double ReadFirst(ReadOnlySpan<char> args)
    {
        Span<double> v = stackalloc double[1];
        ReadNumbers(args, v);
        return v[0];
    }

    private static int ReadNumbers(ReadOnlySpan<char> s, Span<double> dest)
    {
        int idx = 0;
        int pos = 0;
        while (pos < s.Length && idx < dest.Length)
        {
            // Skip separators
            while (pos < s.Length && (s[pos] == ' ' || s[pos] == '\t' || s[pos] == ',' || s[pos] == '\r' || s[pos] == '\n'))
                pos++;
            if (pos >= s.Length) break;

            int start = pos;
            bool hasDot = false;
            bool hasE   = false;
            if (pos < s.Length && (s[pos] == '-' || s[pos] == '+')) pos++;
            while (pos < s.Length)
            {
                char c = s[pos];
                if (char.IsDigit(c)) pos++;
                else if (c == '.' && !hasDot && !hasE) { hasDot = true; pos++; }
                else if ((c == 'e' || c == 'E') && !hasE)
                {
                    hasE = true; pos++;
                    if (pos < s.Length && (s[pos] == '+' || s[pos] == '-')) pos++;
                }
                else break;
            }
            if (pos == start) break;
            double.TryParse(s[start..pos], NumberStyles.Float, CultureInfo.InvariantCulture, out dest[idx]);
            idx++;
        }
        return idx;
    }
}
