using System.Globalization;

using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.MewCharts.Drawing;

/// <summary>
/// Parses the SVG path "d" mini-language into a MewUI <see cref="PathGeometry"/>. Supports the
/// M/L/H/V/C/S/Q/T/Z commands (absolute and relative); elliptical arcs (A) are approximated by
/// line segments. Used by <see cref="Geometries.SvgGeometry"/> for SVG-based chart markers.
/// </summary>
public static class SvgPathData
{
    public static PathGeometry Parse(string data)
    {
        var path = new PathGeometry();
        if (string.IsNullOrWhiteSpace(data)) return path;

        var pos = 0;
        double currentX = 0, currentY = 0, startX = 0, startY = 0;
        double lastCtrlX = 0, lastCtrlY = 0;
        var prevCmd = '\0';
        var span = data.AsSpan();

        while (pos < span.Length)
        {
            SkipSeparators(span, ref pos);
            if (pos >= span.Length) break;

            var command = span[pos];
            if (char.IsLetter(command)) pos++;
            else command = ImplicitCommand(prevCmd);

            var relative = char.IsLower(command);
            var cmd = char.ToUpperInvariant(command);

            switch (cmd)
            {
                case 'M':
                {
                    var x = ReadNumber(span, ref pos);
                    var y = ReadNumber(span, ref pos);
                    if (relative) { x += currentX; y += currentY; }
                    currentX = x; currentY = y; startX = x; startY = y;
                    path.MoveTo(x, y);
                    break;
                }
                case 'L':
                {
                    var x = ReadNumber(span, ref pos);
                    var y = ReadNumber(span, ref pos);
                    if (relative) { x += currentX; y += currentY; }
                    currentX = x; currentY = y;
                    path.LineTo(x, y);
                    break;
                }
                case 'H':
                {
                    var x = ReadNumber(span, ref pos);
                    if (relative) x += currentX;
                    currentX = x;
                    path.LineTo(x, currentY);
                    break;
                }
                case 'V':
                {
                    var y = ReadNumber(span, ref pos);
                    if (relative) y += currentY;
                    currentY = y;
                    path.LineTo(currentX, y);
                    break;
                }
                case 'C':
                {
                    var c1x = ReadNumber(span, ref pos); var c1y = ReadNumber(span, ref pos);
                    var c2x = ReadNumber(span, ref pos); var c2y = ReadNumber(span, ref pos);
                    var x = ReadNumber(span, ref pos); var y = ReadNumber(span, ref pos);
                    if (relative) { c1x += currentX; c1y += currentY; c2x += currentX; c2y += currentY; x += currentX; y += currentY; }
                    path.BezierTo(c1x, c1y, c2x, c2y, x, y);
                    lastCtrlX = c2x; lastCtrlY = c2y; currentX = x; currentY = y;
                    break;
                }
                case 'S':
                {
                    var c2x = ReadNumber(span, ref pos); var c2y = ReadNumber(span, ref pos);
                    var x = ReadNumber(span, ref pos); var y = ReadNumber(span, ref pos);
                    if (relative) { c2x += currentX; c2y += currentY; x += currentX; y += currentY; }
                    var (c1x, c1y) = prevCmd is 'C' or 'S' or 'c' or 's'
                        ? (2 * currentX - lastCtrlX, 2 * currentY - lastCtrlY)
                        : (currentX, currentY);
                    path.BezierTo(c1x, c1y, c2x, c2y, x, y);
                    lastCtrlX = c2x; lastCtrlY = c2y; currentX = x; currentY = y;
                    break;
                }
                case 'Q':
                {
                    var cx = ReadNumber(span, ref pos); var cy = ReadNumber(span, ref pos);
                    var x = ReadNumber(span, ref pos); var y = ReadNumber(span, ref pos);
                    if (relative) { cx += currentX; cy += currentY; x += currentX; y += currentY; }
                    path.QuadTo(cx, cy, x, y);
                    lastCtrlX = cx; lastCtrlY = cy; currentX = x; currentY = y;
                    break;
                }
                case 'T':
                {
                    var x = ReadNumber(span, ref pos); var y = ReadNumber(span, ref pos);
                    if (relative) { x += currentX; y += currentY; }
                    var (cx, cy) = prevCmd is 'Q' or 'T' or 'q' or 't'
                        ? (2 * currentX - lastCtrlX, 2 * currentY - lastCtrlY)
                        : (currentX, currentY);
                    path.QuadTo(cx, cy, x, y);
                    lastCtrlX = cx; lastCtrlY = cy; currentX = x; currentY = y;
                    break;
                }
                case 'A':
                {
                    var rx = ReadNumber(span, ref pos); var ry = ReadNumber(span, ref pos);
                    var rotation = ReadNumber(span, ref pos);
                    var largeArc = ReadFlag(span, ref pos);
                    var sweep = ReadFlag(span, ref pos);
                    var x = ReadNumber(span, ref pos); var y = ReadNumber(span, ref pos);
                    if (relative) { x += currentX; y += currentY; }
                    AppendArc(path, currentX, currentY, rx, ry, rotation, largeArc, sweep, x, y);
                    currentX = x; currentY = y;
                    break;
                }
                case 'Z':
                    path.Close();
                    currentX = startX; currentY = startY;
                    break;
                default:
                    pos++;
                    break;
            }

            prevCmd = command;
        }

        return path;
    }

    // Approximates an elliptical arc with line segments (endpoint -> center parameterization).
    private static void AppendArc(
        PathGeometry path, double x0, double y0, double rx, double ry, double rotationDegrees,
        bool largeArc, bool sweep, double x, double y)
    {
        if (rx == 0 || ry == 0) { path.LineTo(x, y); return; }

        rx = Math.Abs(rx); ry = Math.Abs(ry);
        var phi = rotationDegrees * Math.PI / 180.0;
        var cosPhi = Math.Cos(phi);
        var sinPhi = Math.Sin(phi);

        var dx = (x0 - x) / 2.0;
        var dy = (y0 - y) / 2.0;
        var x1p = cosPhi * dx + sinPhi * dy;
        var y1p = -sinPhi * dx + cosPhi * dy;

        var lambda = (x1p * x1p) / (rx * rx) + (y1p * y1p) / (ry * ry);
        if (lambda > 1)
        {
            var scale = Math.Sqrt(lambda);
            rx *= scale; ry *= scale;
        }

        var sign = largeArc == sweep ? -1.0 : 1.0;
        var numerator = rx * rx * ry * ry - rx * rx * y1p * y1p - ry * ry * x1p * x1p;
        if (numerator < 0) numerator = 0;
        var denominator = rx * rx * y1p * y1p + ry * ry * x1p * x1p;
        var coefficient = denominator == 0 ? 0 : sign * Math.Sqrt(numerator / denominator);

        var cxp = coefficient * (rx * y1p / ry);
        var cyp = coefficient * -(ry * x1p / rx);

        var cx = cosPhi * cxp - sinPhi * cyp + (x0 + x) / 2.0;
        var cy = sinPhi * cxp + cosPhi * cyp + (y0 + y) / 2.0;

        var startAngle = Angle(1, 0, (x1p - cxp) / rx, (y1p - cyp) / ry);
        var deltaAngle = Angle((x1p - cxp) / rx, (y1p - cyp) / ry, (-x1p - cxp) / rx, (-y1p - cyp) / ry);
        if (!sweep && deltaAngle > 0) deltaAngle -= 2 * Math.PI;
        else if (sweep && deltaAngle < 0) deltaAngle += 2 * Math.PI;

        var segments = Math.Max(2, (int)Math.Ceiling(Math.Abs(deltaAngle) / (Math.PI / 16)));
        for (var i = 1; i <= segments; i++)
        {
            var angle = startAngle + deltaAngle * i / segments;
            var px = cx + rx * Math.Cos(angle) * cosPhi - ry * Math.Sin(angle) * sinPhi;
            var py = cy + rx * Math.Cos(angle) * sinPhi + ry * Math.Sin(angle) * cosPhi;
            path.LineTo(px, py);
        }
    }

    private static double Angle(double ux, double uy, double vx, double vy)
    {
        var dot = ux * vx + uy * vy;
        var len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        var value = len == 0 ? 0 : dot / len;
        value = Math.Max(-1, Math.Min(1, value));
        var angle = Math.Acos(value);
        if (ux * vy - uy * vx < 0) angle = -angle;
        return angle;
    }

    private static char ImplicitCommand(char prevCmd)
    {
        // After an M/m, additional coordinate pairs are implicit L/l commands.
        if (prevCmd is 'M') return 'L';
        if (prevCmd is 'm') return 'l';
        return prevCmd;
    }

    private static void SkipSeparators(ReadOnlySpan<char> span, ref int pos)
    {
        while (pos < span.Length && (span[pos] is ' ' or ',' or '\t' or '\n' or '\r')) pos++;
    }

    private static bool ReadFlag(ReadOnlySpan<char> span, ref int pos)
    {
        SkipSeparators(span, ref pos);
        var flag = pos < span.Length && span[pos] == '1';
        if (pos < span.Length) pos++;
        return flag;
    }

    private static double ReadNumber(ReadOnlySpan<char> span, ref int pos)
    {
        SkipSeparators(span, ref pos);
        var start = pos;
        if (pos < span.Length && (span[pos] is '+' or '-')) pos++;
        var seenDot = false;
        var seenExp = false;
        while (pos < span.Length)
        {
            var c = span[pos];
            if (char.IsDigit(c)) { pos++; }
            else if (c == '.' && !seenDot && !seenExp) { seenDot = true; pos++; }
            else if ((c is 'e' or 'E') && !seenExp) { seenExp = true; pos++; if (pos < span.Length && (span[pos] is '+' or '-')) pos++; }
            else break;
        }

        if (pos == start) { pos++; return 0; }
        return double.TryParse(span.Slice(start, pos - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? value : 0;
    }
}
