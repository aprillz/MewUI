using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public enum GlyphKind
{
    ChevronUp,
    ChevronDown,
    ChevronLeft,
    ChevronRight,
    Plus,
    Minus,
    Cross,

    CheckMark,
    IndeterminateMark,

    // Window chrome
    WindowMinimize,

    WindowMaximize,
    WindowRestore,
}

public static class Glyph
{
    [ThreadStatic]
    private static PathGeometry? _cachedPath;

    public static void Draw(IGraphicsContext context, Point center, double size, Color color, GlyphKind glyph, double thickness = 1)
    {
        if (size <= 0 || thickness <= 0)
        {
            return;
        }

        double half = size;

        switch (glyph)
        {
            case GlyphKind.ChevronUp:
                DrawChevron(context, center, half, color, thickness, up: true);
                return;

            case GlyphKind.ChevronDown:
                DrawChevron(context, center, half, color, thickness, up: false);
                return;

            case GlyphKind.ChevronLeft:
                DrawChevronSide(context, center, half, color, thickness, left: true);
                return;

            case GlyphKind.ChevronRight:
                DrawChevronSide(context, center, half, color, thickness, left: false);
                return;

            case GlyphKind.Plus:
                context.DrawLine(new Point(center.X - half, center.Y), new Point(center.X + half, center.Y), color, thickness);
                context.DrawLine(new Point(center.X, center.Y - half), new Point(center.X, center.Y + half), color, thickness);
                return;

            case GlyphKind.Minus:
                context.DrawLine(new Point(center.X - half, center.Y), new Point(center.X + half, center.Y), color, thickness);
                return;

            case GlyphKind.Cross:
                context.DrawLine(new Point(center.X - half, center.Y - half), new Point(center.X + half, center.Y + half), color, thickness);
                context.DrawLine(new Point(center.X - half, center.Y + half), new Point(center.X + half, center.Y - half), color, thickness);
                return;

            case GlyphKind.CheckMark:
                var g = _cachedPath ??= new PathGeometry();
                g.Reset();
                g.MoveTo(center.X - half, center.Y + half * 0.1);
                g.LineTo(center.X - half * 0.1, center.Y + half);
                g.LineTo(center.X + half, center.Y - half);
                context.DrawPath(g, color, thickness);
                return;

            case GlyphKind.IndeterminateMark:
                context.DrawLine(new Point(center.X - half, center.Y), new Point(center.X + half, center.Y), color, thickness);
                return;

            case GlyphKind.WindowMinimize:
                context.DrawLine(new Point(center.X - half, center.Y + half), new Point(center.X + half, center.Y + half), color, thickness);
                return;

            case GlyphKind.WindowMaximize:
                context.DrawRectangle(new Rect(center.X - half, center.Y - half, half * 2, half * 2), color, thickness);
                return;

            case GlyphKind.WindowRestore:
            {
                double offset = half * 0.4;
                double s = half * 2 - offset;
                double bx = center.X - half + offset;
                double by = center.Y - half;
                var g = _cachedPath ??= new PathGeometry();
                g.Reset();
                g.MoveTo(bx, by);
                g.LineTo(bx + s, by);
                g.LineTo(bx + s, by + s);
                g.LineTo(bx + s - offset, by + s);
                context.DrawPath(g, color, thickness);
                context.DrawRectangle(new Rect(center.X - half, center.Y - half + offset, s, s), color, thickness);
                return;
            }

            default:
                return;
        }
    }

    private static void DrawChevron(IGraphicsContext context, Point center, double half, Color color, double thickness, bool up)
    {
        Point p1, p2, p3;

        if (up)
        {
            p1 = new Point(center.X - half, center.Y + half / 2);
            p2 = new Point(center.X, center.Y - half / 2);
            p3 = new Point(center.X + half, center.Y + half / 2);
        }
        else
        {
            p1 = new Point(center.X - half, center.Y - half / 2);
            p2 = new Point(center.X, center.Y + half / 2);
            p3 = new Point(center.X + half, center.Y - half / 2);
        }

        var g = _cachedPath ??= new PathGeometry();
        g.Reset();
        g.MoveTo(p1);
        g.LineTo(p2);
        g.LineTo(p3);
        context.DrawPath(g, color, thickness);
    }

    private static void DrawChevronSide(IGraphicsContext context, Point center, double half, Color color, double thickness, bool left)
    {
        Point p1, p2, p3;

        if (left)
        {
            p1 = new Point(center.X + half / 2, center.Y - half);
            p2 = new Point(center.X - half / 2, center.Y);
            p3 = new Point(center.X + half / 2, center.Y + half);
        }
        else
        {
            p1 = new Point(center.X - half / 2, center.Y - half);
            p2 = new Point(center.X + half / 2, center.Y);
            p3 = new Point(center.X - half / 2, center.Y + half);
        }

        var g = _cachedPath ??= new PathGeometry();
        g.Reset();
        g.MoveTo(p1);
        g.LineTo(p2);
        g.LineTo(p3);
        context.DrawPath(g, color, thickness);
    }
}
