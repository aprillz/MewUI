namespace Aprillz.MewUI.Rendering;

internal static class RenderingUtil
{
    public static int RoundToPixelInt(double value, double dpiScale)
        => (int)Math.Round(value * dpiScale, MidpointRounding.AwayFromZero);

    public static int CeilToPixelInt(double value, double dpiScale)
        => (int)Math.Ceiling(value * dpiScale);

    public static (int X, int Y) ToDevicePoint(Point pt, double translateX, double translateY, double dpiScale)
        => (RoundToPixelInt(pt.X + translateX, dpiScale), RoundToPixelInt(pt.Y + translateY, dpiScale));

    public static (int Left, int Top, int Right, int Bottom) ToDeviceRect(Rect rect, double translateX, double translateY, double dpiScale)
    {
        int left = RoundToPixelInt(rect.X + translateX, dpiScale);
        int top = RoundToPixelInt(rect.Y + translateY, dpiScale);
        int right = RoundToPixelInt(rect.Right + translateX, dpiScale);
        int bottom = RoundToPixelInt(rect.Bottom + translateY, dpiScale);
        return (left, top, right, bottom);
    }

    public static Rect Intersect(in Rect a, in Rect b)
    {
        double x1 = Math.Max(a.X, b.X);
        double y1 = Math.Max(a.Y, b.Y);
        double x2 = Math.Min(a.Right, b.Right);
        double y2 = Math.Min(a.Bottom, b.Bottom);

        if (x2 <= x1 || y2 <= y1)
        {
            return Rect.Empty;
        }

        return new Rect(x1, y1, x2 - x1, y2 - y1);
    }

    public static (double Left, double Top, double Width, double Height) Intersect(
        double ax, double ay, double aw, double ah,
        double bx, double by, double bw, double bh)
    {
        double x1 = Math.Max(ax, bx);
        double y1 = Math.Max(ay, by);
        double x2 = Math.Min(ax + aw, bx + bw);
        double y2 = Math.Min(ay + ah, by + bh);

        if (x2 <= x1 || y2 <= y1)
        {
            return (0, 0, 0, 0);
        }

        return (x1, y1, x2 - x1, y2 - y1);
    }

    public static (int Left, int Top, int Width, int Height) Intersect(
        int ax, int ay, int aw, int ah,
        int bx, int by, int bw, int bh)
    {
        int x1 = Math.Max(ax, bx);
        int y1 = Math.Max(ay, by);
        int x2 = Math.Min(ax + aw, bx + bw);
        int y2 = Math.Min(ay + ah, by + bh);

        if (x2 <= x1 || y2 <= y1)
        {
            return (0, 0, 0, 0);
        }

        return (x1, y1, x2 - x1, y2 - y1);
    }
}
