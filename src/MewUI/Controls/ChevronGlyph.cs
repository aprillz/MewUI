using Aprillz.MewUI.Rendering;

namespace Aprillz.MewUI.Controls;

public enum ChevronDirection
{
    Up,
    Down,
    Left,
    Right,
}

public static class ChevronGlyph
{
    public static void Draw(IGraphicsContext context, Point center, double size, Color color, ChevronDirection direction)
    {
        // Matches ComboBox drop-down chevron (2 line segments).
        double half = size;

        Point p1;
        Point p2;
        Point p3;

        switch (direction)
        {
            case ChevronDirection.Up:
                p1 = new Point(center.X - half, center.Y + half / 2);
                p2 = new Point(center.X, center.Y - half / 2);
                p3 = new Point(center.X + half, center.Y + half / 2);
                break;
            case ChevronDirection.Down:
                p1 = new Point(center.X - half, center.Y - half / 2);
                p2 = new Point(center.X, center.Y + half / 2);
                p3 = new Point(center.X + half, center.Y - half / 2);
                break;
            case ChevronDirection.Left:
                p1 = new Point(center.X + half / 2, center.Y - half);
                p2 = new Point(center.X - half / 2, center.Y);
                p3 = new Point(center.X + half / 2, center.Y + half);
                break;
            case ChevronDirection.Right:
                p1 = new Point(center.X - half / 2, center.Y - half);
                p2 = new Point(center.X + half / 2, center.Y);
                p3 = new Point(center.X - half / 2, center.Y + half);
                break;
            default:
                return;
        }

        context.DrawLine(p1, p2, color, 1);
        context.DrawLine(p2, p3, color, 1);
    }
}

