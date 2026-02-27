using System.Globalization;

namespace Aprillz.MewUI.Rendering;

/// <summary>
/// Parses SVG path data mini-language strings into <see cref="PathGeometry"/> commands.
/// Supports: M/m, L/l, H/h, V/v, C/c, S/s, Q/q, T/t, A/a, Z/z.
/// </summary>
internal static class SvgPathParser
{
    public static PathGeometry Parse(ReadOnlySpan<char> d)
    {
        var geometry = new PathGeometry();
        int pos = 0;
        char cmd = '\0';

        // Tracks for smooth curves (reflection of previous control point).
        double lastCpX = 0, lastCpY = 0; // last cubic control point 2
        double lastQpX = 0, lastQpY = 0; // last quad control point
        char prevCmd = '\0';

        // Current point for relative commands.
        double cx = 0, cy = 0;

        while (pos < d.Length)
        {
            SkipWhitespaceAndCommas(d, ref pos);
            if (pos >= d.Length) break;

            char c = d[pos];
            if (IsCommand(c))
            {
                cmd = c;
                pos++;
            }
            else if (cmd == '\0')
            {
                // No command yet, skip unexpected character.
                pos++;
                continue;
            }
            // else: implicit repetition of previous command

            bool isRelative = char.IsLower(cmd);
            char upperCmd = char.ToUpperInvariant(cmd);

            switch (upperCmd)
            {
                case 'M':
                {
                    double x = ReadNumber(d, ref pos);
                    double y = ReadNumber(d, ref pos);
                    if (isRelative) { x += cx; y += cy; }
                    geometry.MoveTo(x, y);
                    cx = x; cy = y;
                    // After M, implicit repeats become L/l per SVG spec.
                    cmd = isRelative ? 'l' : 'L';
                    ResetSmooth(ref prevCmd);
                    break;
                }
                case 'L':
                {
                    double x = ReadNumber(d, ref pos);
                    double y = ReadNumber(d, ref pos);
                    if (isRelative) { x += cx; y += cy; }
                    geometry.LineTo(x, y);
                    cx = x; cy = y;
                    ResetSmooth(ref prevCmd);
                    break;
                }
                case 'H':
                {
                    double x = ReadNumber(d, ref pos);
                    if (isRelative) x += cx;
                    geometry.LineTo(x, cy);
                    cx = x;
                    ResetSmooth(ref prevCmd);
                    break;
                }
                case 'V':
                {
                    double y = ReadNumber(d, ref pos);
                    if (isRelative) y += cy;
                    geometry.LineTo(cx, y);
                    cy = y;
                    ResetSmooth(ref prevCmd);
                    break;
                }
                case 'C':
                {
                    double c1x = ReadNumber(d, ref pos);
                    double c1y = ReadNumber(d, ref pos);
                    double c2x = ReadNumber(d, ref pos);
                    double c2y = ReadNumber(d, ref pos);
                    double x = ReadNumber(d, ref pos);
                    double y = ReadNumber(d, ref pos);
                    if (isRelative) { c1x += cx; c1y += cy; c2x += cx; c2y += cy; x += cx; y += cy; }
                    geometry.BezierTo(c1x, c1y, c2x, c2y, x, y);
                    lastCpX = c2x; lastCpY = c2y;
                    cx = x; cy = y;
                    prevCmd = 'C';
                    break;
                }
                case 'S':
                {
                    double c2x = ReadNumber(d, ref pos);
                    double c2y = ReadNumber(d, ref pos);
                    double x = ReadNumber(d, ref pos);
                    double y = ReadNumber(d, ref pos);
                    if (isRelative) { c2x += cx; c2y += cy; x += cx; y += cy; }
                    // Reflect previous c2 to get c1.
                    double c1x, c1y;
                    if (prevCmd == 'C' || prevCmd == 'S')
                    {
                        c1x = 2 * cx - lastCpX;
                        c1y = 2 * cy - lastCpY;
                    }
                    else
                    {
                        c1x = cx; c1y = cy;
                    }
                    geometry.BezierTo(c1x, c1y, c2x, c2y, x, y);
                    lastCpX = c2x; lastCpY = c2y;
                    cx = x; cy = y;
                    prevCmd = 'S';
                    break;
                }
                case 'Q':
                {
                    double cpx = ReadNumber(d, ref pos);
                    double cpy = ReadNumber(d, ref pos);
                    double x = ReadNumber(d, ref pos);
                    double y = ReadNumber(d, ref pos);
                    if (isRelative) { cpx += cx; cpy += cy; x += cx; y += cy; }
                    geometry.QuadTo(cpx, cpy, x, y);
                    lastQpX = cpx; lastQpY = cpy;
                    cx = x; cy = y;
                    prevCmd = 'Q';
                    break;
                }
                case 'T':
                {
                    double x = ReadNumber(d, ref pos);
                    double y = ReadNumber(d, ref pos);
                    if (isRelative) { x += cx; y += cy; }
                    double cpx, cpy;
                    if (prevCmd == 'Q' || prevCmd == 'T')
                    {
                        cpx = 2 * cx - lastQpX;
                        cpy = 2 * cy - lastQpY;
                    }
                    else
                    {
                        cpx = cx; cpy = cy;
                    }
                    geometry.QuadTo(cpx, cpy, x, y);
                    lastQpX = cpx; lastQpY = cpy;
                    cx = x; cy = y;
                    prevCmd = 'T';
                    break;
                }
                case 'A':
                {
                    double rx = ReadNumber(d, ref pos);
                    double ry = ReadNumber(d, ref pos);
                    double xRotation = ReadNumber(d, ref pos);
                    bool largeArc = ReadFlag(d, ref pos);
                    bool sweep = ReadFlag(d, ref pos);
                    double x = ReadNumber(d, ref pos);
                    double y = ReadNumber(d, ref pos);
                    if (isRelative) { x += cx; y += cy; }
                    geometry.SvgArcTo(rx, ry, xRotation, largeArc, sweep, x, y);
                    cx = x; cy = y;
                    ResetSmooth(ref prevCmd);
                    break;
                }
                case 'Z':
                {
                    geometry.Close();
                    // After Z, current point returns to sub-path start (handled by PathGeometry.Close).
                    // We need to track it here too for relative commands.
                    // PathGeometry tracks _startX/_startY internally; we approximate by
                    // noting that subsequent M will reset cx/cy.
                    ResetSmooth(ref prevCmd);
                    break;
                }
                default:
                    // Unknown command, skip.
                    pos++;
                    break;
            }
        }

        return geometry;
    }

    private static void ResetSmooth(ref char prevCmd) => prevCmd = '\0';

    private static bool IsCommand(char c)
        => c is 'M' or 'm' or 'L' or 'l' or 'H' or 'h' or 'V' or 'v'
            or 'C' or 'c' or 'S' or 's' or 'Q' or 'q' or 'T' or 't'
            or 'A' or 'a' or 'Z' or 'z';

    private static void SkipWhitespaceAndCommas(ReadOnlySpan<char> d, ref int pos)
    {
        while (pos < d.Length && (char.IsWhiteSpace(d[pos]) || d[pos] == ','))
            pos++;
    }

    private static bool ReadFlag(ReadOnlySpan<char> d, ref int pos)
    {
        SkipWhitespaceAndCommas(d, ref pos);
        if (pos >= d.Length) return false;
        bool value = d[pos] == '1';
        pos++;
        SkipWhitespaceAndCommas(d, ref pos);
        return value;
    }

    private static double ReadNumber(ReadOnlySpan<char> d, ref int pos)
    {
        SkipWhitespaceAndCommas(d, ref pos);
        if (pos >= d.Length) return 0;

        int start = pos;

        // Optional sign.
        if (pos < d.Length && (d[pos] == '-' || d[pos] == '+'))
            pos++;

        // Integer part.
        while (pos < d.Length && char.IsAsciiDigit(d[pos]))
            pos++;

        // Fractional part.
        if (pos < d.Length && d[pos] == '.')
        {
            pos++;
            while (pos < d.Length && char.IsAsciiDigit(d[pos]))
                pos++;
        }

        // Exponent part.
        if (pos < d.Length && (d[pos] == 'e' || d[pos] == 'E'))
        {
            pos++;
            if (pos < d.Length && (d[pos] == '-' || d[pos] == '+'))
                pos++;
            while (pos < d.Length && char.IsAsciiDigit(d[pos]))
                pos++;
        }

        if (pos == start) return 0;

        var slice = d[start..pos];
#if NET8_0_OR_GREATER
        double.TryParse(slice, NumberStyles.Float, CultureInfo.InvariantCulture, out double result);
#else
        double.TryParse(slice.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out double result);
#endif
        return result;
    }
}
