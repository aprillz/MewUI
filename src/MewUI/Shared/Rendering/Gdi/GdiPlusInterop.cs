using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Rendering.Gdi;

internal static partial class GdiPlusInterop
{
    private static int _initialized;
    private static nint _token;

    public static void EnsureInitialized()
    {
        if (Interlocked.Exchange(ref _initialized, 1) != 0)
        {
            return;
        }

        var input = new GdiplusStartupInput
        {
            GdiplusVersion = 1,
            DebugEventCallback = 0,
            SuppressBackgroundThread = 0,
            SuppressExternalCodecs = 0
        };

        GdiplusStartup(out _token, ref input, nint.Zero);
    }

    [LibraryImport("gdiplus.dll")]
    public static partial int GdiplusStartup(out nint token, ref GdiplusStartupInput input, nint output);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateFromHDC(nint hdc, out nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteGraphics(nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetSmoothingMode(nint graphics, SmoothingMode mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPixelOffsetMode(nint graphics, PixelOffsetMode mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetCompositingMode(nint graphics, CompositingMode mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetCompositingQuality(nint graphics, CompositingQuality quality);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipTranslateWorldTransform(nint graphics, float dx, float dy, MatrixOrder order);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSaveGraphics(nint graphics, out uint state);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipRestoreGraphics(nint graphics, uint state);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetClipRectI(nint graphics, int x, int y, int width, int height, CombineMode combineMode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetClipPath(nint graphics, nint path, CombineMode combineMode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipResetClip(nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePath(FillMode fillMode, out nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePath(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathArcI(nint path, int x, int y, int width, int height, float startAngle, float sweepAngle);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipClosePathFigure(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateSolidFill(uint color, out nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteBrush(nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillRectangleI(nint graphics, nint brush, int x, int y, int width, int height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillPath(nint graphics, nint brush, nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePen1(uint color, float width, Unit unit, out nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePen(nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawRectangleI(nint graphics, nint pen, int x, int y, int width, int height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawLineI(nint graphics, nint pen, int x1, int y1, int x2, int y2);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawPath(nint graphics, nint pen, nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawEllipseI(nint graphics, nint pen, int x, int y, int width, int height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillEllipseI(nint graphics, nint brush, int x, int y, int width, int height);

    [StructLayout(LayoutKind.Sequential)]
    public struct GdiplusStartupInput
    {
        public uint GdiplusVersion;
        public nint DebugEventCallback;
        public byte SuppressBackgroundThread;
        public byte SuppressExternalCodecs;
    }

    public enum CombineMode
    {
        Replace = 0,
        Intersect = 1
    }

    public enum FillMode
    {
        Alternate = 0,
        Winding = 1
    }

    public enum SmoothingMode
    {
        AntiAlias = 4
    }

    public enum PixelOffsetMode
    {
        Half = 4
    }

    public enum CompositingMode
    {
        SourceOver = 0
    }

    public enum CompositingQuality
    {
        HighQuality = 4
    }

    public enum Unit
    {
        Pixel = 2
    }

    public enum MatrixOrder
    {
        Prepend = 0,
        Append = 1
    }
}
