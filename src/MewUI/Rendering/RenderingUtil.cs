using System.Numerics;

namespace Aprillz.MewUI.Rendering;

internal static class RenderingUtil
{
    // The grid a font stack already works on, and the precision below which two coordinates are the
    // same position rather than two positions.
    private const double DEVICE_SUBPIXEL_GRID = 64;

    /// <summary>Rounds a device-pixel coordinate to a whole pixel, the same way in every pass.</summary>
    // A bitmap cache capture differs from the window pass by an integer-pixel translation carried in
    // a float matrix, whose error is enough to send a coordinate landing on an exact half pixel to a
    // different pixel in each pass. Quantizing to the subpixel grid first folds that error away.
    public static int RoundDevicePixel(double devicePixels)
    {
        double quantized = Math.Round(devicePixels * DEVICE_SUBPIXEL_GRID) / DEVICE_SUBPIXEL_GRID;
        return (int)Math.Round(quantized, MidpointRounding.AwayFromZero);
    }

    public static int RoundToPixelInt(double value, double dpiScale)
        => RoundDevicePixel(value * dpiScale);

    /// <summary>
    /// Snaps a text origin onto the device pixel grid with the transform's translation included, and
    /// returns it in the caller's own coordinates. Rotation and skew are left where they are.
    /// </summary>
    // Snapping local coordinates instead would put a cache capture's rows on a different grid than
    // the window pass, because only one of the two carries the capture's translation.
    public static (double X, double Y) SnapTextOriginToDevice(double x, double y, in Matrix3x2 transform, double dpiScale)
    {
        if (transform.M12 != 0f || transform.M21 != 0f)
        {
            return (x, y);
        }

        if (transform.M11 != 0f)
        {
            double world = x * transform.M11 + transform.M31;
            x = (RoundDevicePixel(world * dpiScale) / dpiScale - transform.M31) / transform.M11;
        }

        if (transform.M22 != 0f)
        {
            double world = y * transform.M22 + transform.M32;
            y = (RoundDevicePixel(world * dpiScale) / dpiScale - transform.M32) / transform.M22;
        }

        return (x, y);
    }

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
