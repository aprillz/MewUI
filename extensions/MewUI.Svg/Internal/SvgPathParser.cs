using System.Globalization;
using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Svg.Internal;

/// <summary>
/// Parses an SVG path data string (the "d" attribute) into a <see cref="PathGeometry"/>.
/// Supports M,m,L,l,H,h,V,v,C,c,S,s,Q,q,T,t,A,a,Z,z commands.
/// Arc segments are converted to cubic Bézier curves.
/// </summary>
internal static class SvgPathParser
{
    public static PathGeometry Parse(string d, SvgMatrix ctm)
    {
        var path = new PathGeometry();
        if (string.IsNullOrWhiteSpace(d)) return path;

        var reader = new PathReader(d.AsSpan());
        double curX = 0, curY = 0;
        double subPathStartX = 0, subPathStartY = 0;
        // Last cubic control point (for S/s)
        double lastCpX = 0, lastCpY = 0;
        bool lastWasCubic = false;
        // Last quadratic control point (for T/t)
        double lastQcpX = 0, lastQcpY = 0;
        bool lastWasQuad = false;

        char cmd = 'M';
        while (reader.HasMore)
        {
            if (reader.TryReadCommand(out char newCmd))
            {
                cmd = newCmd;
                lastWasCubic = false;
                lastWasQuad = false;
            }

            switch (cmd)
            {
                case 'M':
                {
                    double x = reader.ReadNumber();
                    double y = reader.ReadNumber();
                    subPathStartX = curX = x;
                    subPathStartY = curY = y;
                    var (tx, ty) = ctm.Apply(curX, curY);
                    path.MoveTo(tx, ty);
                    cmd = 'L'; // subsequent coords are implicit LineTo
                    break;
                }
                case 'm':
                {
                    double dx = reader.ReadNumber();
                    double dy = reader.ReadNumber();
                    curX += dx; curY += dy;
                    subPathStartX = curX;
                    subPathStartY = curY;
                    var (tx, ty) = ctm.Apply(curX, curY);
                    path.MoveTo(tx, ty);
                    cmd = 'l';
                    break;
                }
                case 'L':
                {
                    double x = reader.ReadNumber();
                    double y = reader.ReadNumber();
                    curX = x; curY = y;
                    var (tx, ty) = ctm.Apply(curX, curY);
                    path.LineTo(tx, ty);
                    break;
                }
                case 'l':
                {
                    double dx = reader.ReadNumber();
                    double dy = reader.ReadNumber();
                    curX += dx; curY += dy;
                    var (tx, ty) = ctm.Apply(curX, curY);
                    path.LineTo(tx, ty);
                    break;
                }
                case 'H':
                {
                    curX = reader.ReadNumber();
                    var (tx, ty) = ctm.Apply(curX, curY);
                    path.LineTo(tx, ty);
                    break;
                }
                case 'h':
                {
                    curX += reader.ReadNumber();
                    var (tx, ty) = ctm.Apply(curX, curY);
                    path.LineTo(tx, ty);
                    break;
                }
                case 'V':
                {
                    curY = reader.ReadNumber();
                    var (tx, ty) = ctm.Apply(curX, curY);
                    path.LineTo(tx, ty);
                    break;
                }
                case 'v':
                {
                    curY += reader.ReadNumber();
                    var (tx, ty) = ctm.Apply(curX, curY);
                    path.LineTo(tx, ty);
                    break;
                }
                case 'C':
                {
                    double c1x = reader.ReadNumber(), c1y = reader.ReadNumber();
                    double c2x = reader.ReadNumber(), c2y = reader.ReadNumber();
                    double  ex = reader.ReadNumber(),  ey = reader.ReadNumber();
                    lastCpX = c2x; lastCpY = c2y; lastWasCubic = true;
                    curX = ex; curY = ey;
                    var (tc1x, tc1y) = ctm.Apply(c1x, c1y);
                    var (tc2x, tc2y) = ctm.Apply(c2x, c2y);
                    var (tex,  tey)  = ctm.Apply(ex, ey);
                    path.BezierTo(tc1x, tc1y, tc2x, tc2y, tex, tey);
                    break;
                }
                case 'c':
                {
                    double c1x = curX + reader.ReadNumber(), c1y = curY + reader.ReadNumber();
                    double c2x = curX + reader.ReadNumber(), c2y = curY + reader.ReadNumber();
                    double  ex = curX + reader.ReadNumber(),  ey = curY + reader.ReadNumber();
                    lastCpX = c2x; lastCpY = c2y; lastWasCubic = true;
                    curX = ex; curY = ey;
                    var (tc1x, tc1y) = ctm.Apply(c1x, c1y);
                    var (tc2x, tc2y) = ctm.Apply(c2x, c2y);
                    var (tex,  tey)  = ctm.Apply(ex, ey);
                    path.BezierTo(tc1x, tc1y, tc2x, tc2y, tex, tey);
                    break;
                }
                case 'S':
                {
                    double c1x = lastWasCubic ? 2 * curX - lastCpX : curX;
                    double c1y = lastWasCubic ? 2 * curY - lastCpY : curY;
                    double c2x = reader.ReadNumber(), c2y = reader.ReadNumber();
                    double  ex = reader.ReadNumber(),  ey = reader.ReadNumber();
                    lastCpX = c2x; lastCpY = c2y; lastWasCubic = true;
                    curX = ex; curY = ey;
                    var (tc1x, tc1y) = ctm.Apply(c1x, c1y);
                    var (tc2x, tc2y) = ctm.Apply(c2x, c2y);
                    var (tex,  tey)  = ctm.Apply(ex, ey);
                    path.BezierTo(tc1x, tc1y, tc2x, tc2y, tex, tey);
                    break;
                }
                case 's':
                {
                    double c1x = lastWasCubic ? 2 * curX - lastCpX : curX;
                    double c1y = lastWasCubic ? 2 * curY - lastCpY : curY;
                    double c2x = curX + reader.ReadNumber(), c2y = curY + reader.ReadNumber();
                    double  ex = curX + reader.ReadNumber(),  ey = curY + reader.ReadNumber();
                    lastCpX = c2x; lastCpY = c2y; lastWasCubic = true;
                    curX = ex; curY = ey;
                    var (tc1x, tc1y) = ctm.Apply(c1x, c1y);
                    var (tc2x, tc2y) = ctm.Apply(c2x, c2y);
                    var (tex,  tey)  = ctm.Apply(ex, ey);
                    path.BezierTo(tc1x, tc1y, tc2x, tc2y, tex, tey);
                    break;
                }
                case 'Q':
                {
                    double qcx = reader.ReadNumber(), qcy = reader.ReadNumber();
                    double  ex = reader.ReadNumber(),  ey = reader.ReadNumber();
                    // Convert quadratic to cubic
                    double c1x = curX + 2.0 / 3.0 * (qcx - curX);
                    double c1y = curY + 2.0 / 3.0 * (qcy - curY);
                    double c2x =   ex + 2.0 / 3.0 * (qcx -   ex);
                    double c2y =   ey + 2.0 / 3.0 * (qcy -   ey);
                    lastQcpX = qcx; lastQcpY = qcy; lastWasQuad = true;
                    curX = ex; curY = ey;
                    var (tc1x, tc1y) = ctm.Apply(c1x, c1y);
                    var (tc2x, tc2y) = ctm.Apply(c2x, c2y);
                    var (tex,  tey)  = ctm.Apply(ex, ey);
                    path.BezierTo(tc1x, tc1y, tc2x, tc2y, tex, tey);
                    break;
                }
                case 'q':
                {
                    double qcx = curX + reader.ReadNumber(), qcy = curY + reader.ReadNumber();
                    double  ex = curX + reader.ReadNumber(),  ey = curY + reader.ReadNumber();
                    double c1x = curX + 2.0 / 3.0 * (qcx - curX);
                    double c1y = curY + 2.0 / 3.0 * (qcy - curY);
                    double c2x =   ex + 2.0 / 3.0 * (qcx -   ex);
                    double c2y =   ey + 2.0 / 3.0 * (qcy -   ey);
                    lastQcpX = qcx; lastQcpY = qcy; lastWasQuad = true;
                    curX = ex; curY = ey;
                    var (tc1x, tc1y) = ctm.Apply(c1x, c1y);
                    var (tc2x, tc2y) = ctm.Apply(c2x, c2y);
                    var (tex,  tey)  = ctm.Apply(ex, ey);
                    path.BezierTo(tc1x, tc1y, tc2x, tc2y, tex, tey);
                    break;
                }
                case 'T':
                {
                    double qcx = lastWasQuad ? 2 * curX - lastQcpX : curX;
                    double qcy = lastWasQuad ? 2 * curY - lastQcpY : curY;
                    double  ex = reader.ReadNumber(),  ey = reader.ReadNumber();
                    double c1x = curX + 2.0 / 3.0 * (qcx - curX);
                    double c1y = curY + 2.0 / 3.0 * (qcy - curY);
                    double c2x =   ex + 2.0 / 3.0 * (qcx -   ex);
                    double c2y =   ey + 2.0 / 3.0 * (qcy -   ey);
                    lastQcpX = qcx; lastQcpY = qcy; lastWasQuad = true;
                    curX = ex; curY = ey;
                    var (tc1x, tc1y) = ctm.Apply(c1x, c1y);
                    var (tc2x, tc2y) = ctm.Apply(c2x, c2y);
                    var (tex,  tey)  = ctm.Apply(ex, ey);
                    path.BezierTo(tc1x, tc1y, tc2x, tc2y, tex, tey);
                    break;
                }
                case 't':
                {
                    double qcx = lastWasQuad ? 2 * curX - lastQcpX : curX;
                    double qcy = lastWasQuad ? 2 * curY - lastQcpY : curY;
                    double  ex = curX + reader.ReadNumber(),  ey = curY + reader.ReadNumber();
                    double c1x = curX + 2.0 / 3.0 * (qcx - curX);
                    double c1y = curY + 2.0 / 3.0 * (qcy - curY);
                    double c2x =   ex + 2.0 / 3.0 * (qcx -   ex);
                    double c2y =   ey + 2.0 / 3.0 * (qcy -   ey);
                    lastQcpX = qcx; lastQcpY = qcy; lastWasQuad = true;
                    curX = ex; curY = ey;
                    var (tc1x, tc1y) = ctm.Apply(c1x, c1y);
                    var (tc2x, tc2y) = ctm.Apply(c2x, c2y);
                    var (tex,  tey)  = ctm.Apply(ex, ey);
                    path.BezierTo(tc1x, tc1y, tc2x, tc2y, tex, tey);
                    break;
                }
                case 'A':
                {
                    double rx = Math.Abs(reader.ReadNumber());
                    double ry = Math.Abs(reader.ReadNumber());
                    double xRot = reader.ReadNumber();
                    bool largeArc = reader.ReadFlag();
                    bool sweep    = reader.ReadFlag();
                    double ex     = reader.ReadNumber();
                    double ey     = reader.ReadNumber();
                    AppendArc(path, ctm, curX, curY, rx, ry, xRot, largeArc, sweep, ex, ey);
                    curX = ex; curY = ey;
                    break;
                }
                case 'a':
                {
                    double rx = Math.Abs(reader.ReadNumber());
                    double ry = Math.Abs(reader.ReadNumber());
                    double xRot = reader.ReadNumber();
                    bool largeArc = reader.ReadFlag();
                    bool sweep    = reader.ReadFlag();
                    double ex     = curX + reader.ReadNumber();
                    double ey     = curY + reader.ReadNumber();
                    AppendArc(path, ctm, curX, curY, rx, ry, xRot, largeArc, sweep, ex, ey);
                    curX = ex; curY = ey;
                    break;
                }
                case 'Z':
                case 'z':
                    path.Close();
                    curX = subPathStartX;
                    curY = subPathStartY;
                    break;

                default:
                    reader.SkipNumber();
                    break;
            }

            if (cmd != 'Z' && cmd != 'z')
            {
                lastWasCubic = cmd is 'C' or 'c' or 'S' or 's';
                lastWasQuad  = cmd is 'Q' or 'q' or 'T' or 't';
            }
        }

        return path;
    }

    // ──────────────────────────────────────────────
    // Arc → Cubic Bézier conversion
    // ──────────────────────────────────────────────

    private static void AppendArc(PathGeometry path, SvgMatrix ctm,
        double x1, double y1,
        double rx, double ry, double xRot,
        bool largeArc, bool sweep,
        double x2, double y2)
    {
        if (x1 == x2 && y1 == y2) return;
        if (rx == 0 || ry == 0)
        {
            var (lx, ly) = ctm.Apply(x2, y2);
            path.LineTo(lx, ly);
            return;
        }

        double phi = xRot * Math.PI / 180.0;
        double cosPhi = Math.Cos(phi);
        double sinPhi = Math.Sin(phi);

        double dx = (x1 - x2) / 2.0;
        double dy = (y1 - y2) / 2.0;
        double x1p =  cosPhi * dx + sinPhi * dy;
        double y1p = -sinPhi * dx + cosPhi * dy;

        double x1pSq = x1p * x1p;
        double y1pSq = y1p * y1p;
        double rxSq  = rx * rx;
        double rySq  = ry * ry;

        double lambda = x1pSq / rxSq + y1pSq / rySq;
        if (lambda > 1.0)
        {
            double sl = Math.Sqrt(lambda);
            rx *= sl; ry *= sl;
            rxSq = rx * rx; rySq = ry * ry;
        }

        double num = rxSq * rySq - rxSq * y1pSq - rySq * x1pSq;
        double den = rxSq * y1pSq + rySq * x1pSq;
        double sq  = (num <= 0) ? 0 : Math.Sqrt(num / den);
        if (largeArc == sweep) sq = -sq;

        double cxp =  sq * rx * y1p / ry;
        double cyp = -sq * ry * x1p / rx;

        double cx = cosPhi * cxp - sinPhi * cyp + (x1 + x2) / 2.0;
        double cy = sinPhi * cxp + cosPhi * cyp + (y1 + y2) / 2.0;

        double ux = (x1p - cxp) / rx;
        double uy = (y1p - cyp) / ry;
        double vx = (-x1p - cxp) / rx;
        double vy = (-y1p - cyp) / ry;

        double theta1 = VectorAngle(1, 0, ux, uy);
        double dtheta = VectorAngle(ux, uy, vx, vy);
        if (!sweep && dtheta > 0) dtheta -= 2 * Math.PI;
        if ( sweep && dtheta < 0) dtheta += 2 * Math.PI;

        int n = Math.Max(1, (int)Math.Ceiling(Math.Abs(dtheta) / (Math.PI / 2.0)));
        double dt = dtheta / n;

        for (int i = 0; i < n; i++)
        {
            double t1 = theta1 + i * dt;
            double t2 = theta1 + (i + 1) * dt;
            AppendArcSegment(path, ctm, cx, cy, rx, ry, cosPhi, sinPhi, t1, t2);
        }
    }

    private static void AppendArcSegment(PathGeometry path, SvgMatrix ctm,
        double cx, double cy, double rx, double ry,
        double cosPhi, double sinPhi,
        double t1, double t2)
    {
        double halfDt = (t2 - t1) / 2.0;
        double alpha  = Math.Sin(t2 - t1) * (Math.Sqrt(4 + 3 * Math.Pow(Math.Tan(halfDt), 2)) - 1) / 3.0;

        double cosT1 = Math.Cos(t1), sinT1 = Math.Sin(t1);
        double cosT2 = Math.Cos(t2), sinT2 = Math.Sin(t2);

        double ex1 = rx * cosT1, ey1 = ry * sinT1;
        double ex2 = rx * cosT2, ey2 = ry * sinT2;
        double dx1 = -rx * sinT1, dy1 = ry * cosT1;
        double dx2 = -rx * sinT2, dy2 = ry * cosT2;

        double c1x = cx + cosPhi * (ex1 + alpha * dx1) - sinPhi * (ey1 + alpha * dy1);
        double c1y = cy + sinPhi * (ex1 + alpha * dx1) + cosPhi * (ey1 + alpha * dy1);
        double c2x = cx + cosPhi * (ex2 - alpha * dx2) - sinPhi * (ey2 - alpha * dy2);
        double c2y = cy + sinPhi * (ex2 - alpha * dx2) + cosPhi * (ey2 - alpha * dy2);
        double epx  = cx + cosPhi * ex2 - sinPhi * ey2;
        double epy  = cy + sinPhi * ex2 + cosPhi * ey2;

        var (tc1x, tc1y) = ctm.Apply(c1x, c1y);
        var (tc2x, tc2y) = ctm.Apply(c2x, c2y);
        var (tex,  tey)  = ctm.Apply(epx, epy);
        path.BezierTo(tc1x, tc1y, tc2x, tc2y, tex, tey);
    }

    private static double VectorAngle(double ux, double uy, double vx, double vy)
    {
        double dot = ux * vx + uy * vy;
        double len = Math.Sqrt((ux * ux + uy * uy) * (vx * vx + vy * vy));
        double angle = Math.Acos(Math.Clamp(dot / len, -1, 1));
        if (ux * vy - uy * vx < 0) angle = -angle;
        return angle;
    }

    // ──────────────────────────────────────────────
    // Token reader
    // ──────────────────────────────────────────────

    private ref struct PathReader
    {
        private ReadOnlySpan<char> _s;
        private int _pos;

        public PathReader(ReadOnlySpan<char> s) { _s = s; _pos = 0; }

        public bool HasMore
        {
            get
            {
                SkipWhitespace();
                return _pos < _s.Length;
            }
        }

        public bool TryReadCommand(out char cmd)
        {
            SkipWhitespace();
            if (_pos < _s.Length && char.IsLetter(_s[_pos]))
            {
                cmd = _s[_pos++];
                return true;
            }
            cmd = default;
            return false;
        }

        public double ReadNumber()
        {
            SkipSeparators();
            int start = _pos;
            bool hasDot = false;
            bool hasE   = false;
            if (_pos < _s.Length && (_s[_pos] == '-' || _s[_pos] == '+')) _pos++;
            while (_pos < _s.Length)
            {
                char c = _s[_pos];
                if (char.IsDigit(c)) { _pos++; }
                else if (c == '.' && !hasDot && !hasE) { hasDot = true; _pos++; }
                else if ((c == 'e' || c == 'E') && !hasE) { hasE = true; _pos++;
                    if (_pos < _s.Length && (_s[_pos] == '+' || _s[_pos] == '-')) _pos++; }
                else break;
            }
            if (_pos == start) return 0;
            double.TryParse(_s[start.._pos], NumberStyles.Float, CultureInfo.InvariantCulture, out double v);
            return v;
        }

        public bool ReadFlag()
        {
            SkipSeparators();
            if (_pos < _s.Length && (_s[_pos] == '0' || _s[_pos] == '1'))
                return _s[_pos++] == '1';
            return false;
        }

        public void SkipNumber() => ReadNumber();

        private void SkipWhitespace()
        {
            while (_pos < _s.Length && (_s[_pos] == ' ' || _s[_pos] == '\t' ||
                   _s[_pos] == '\r' || _s[_pos] == '\n'))
                _pos++;
        }

        private void SkipSeparators()
        {
            while (_pos < _s.Length)
            {
                char c = _s[_pos];
                if (c == ' ' || c == '\t' || c == '\r' || c == '\n' || c == ',')
                    _pos++;
                else
                    break;
            }
        }
    }
}
