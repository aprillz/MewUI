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

    // ── World Transform ─────────────────────────────────────────────────────
    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateMatrix2(float m11, float m12, float m21, float m22, float dx, float dy, out nint matrix);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeleteMatrix(nint matrix);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetWorldTransform(nint graphics, nint matrix);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipResetWorldTransform(nint graphics);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePath(FillMode fillMode, out nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePath(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathArcI(nint path, int x, int y, int width, int height, float startAngle, float sweepAngle);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathArc(nint path, float x, float y, float width, float height, float startAngle, float sweepAngle);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathRectangleI(nint path, int x, int y, int width, int height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathRectangle(nint path, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipStartPathFigure(nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathLine(nint path, float x1, float y1, float x2, float y2);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathBezier(nint path, float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4);

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
    public static partial int GdipCreatePen2(nint brush, float width, Unit unit, out nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDeletePen(nint pen);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenMode(nint pen, PenAlignment mode);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenLineCap197819(nint pen, GpLineCap startCap, GpLineCap endCap, GpDashCap dashCap);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenLineJoin(nint pen, GpLineJoin lineJoin);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenMiterLimit(nint pen, float miterLimit);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenDashStyle(nint pen, GpDashStyle dashStyle);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPenDashOffset(nint pen, float offset);

    [LibraryImport("gdiplus.dll")]
    private static unsafe partial int GdipSetPenDashArray(nint pen, float* dash, int count);

    public static unsafe int SetPenDashArray(nint pen, float[] dashes)
    {
        fixed (float* p = dashes)
            return GdipSetPenDashArray(pen, p, dashes.Length);
    }

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawRectangleI(nint graphics, nint pen, int x, int y, int width, int height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawRectangle(nint graphics, nint pen, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawLineI(nint graphics, nint pen, int x1, int y1, int x2, int y2);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawLine(nint graphics, nint pen, float x1, float y1, float x2, float y2);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawPath(nint graphics, nint pen, nint path);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawEllipseI(nint graphics, nint pen, int x, int y, int width, int height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipDrawEllipse(nint graphics, nint pen, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipFillEllipseI(nint graphics, nint brush, int x, int y, int width, int height);

    // ── Linear gradient brush ───────────────────────────────────────────────

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreateLineBrush(
        ref PointF point1, ref PointF point2,
        uint color1, uint color2,
        WrapMode wrapMode,
        out nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetLineWrapMode(nint brush, WrapMode wrapMode);

    [LibraryImport("gdiplus.dll")]
    private static unsafe partial int GdipSetLinePresetBlend(
        nint brush, uint* blend, float* positions, int count);

    public static unsafe int SetLinePresetBlend(nint brush, uint[] colors, float[] positions)
    {
        fixed (uint* pc = colors)
        fixed (float* pp = positions)
            return GdipSetLinePresetBlend(brush, pc, pp, colors.Length);
    }

    // ── Path (radial) gradient brush ────────────────────────────────────────

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipAddPathEllipse(nint path, float x, float y, float width, float height);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipCreatePathGradientFromPath(nint path, out nint brush);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPathGradientCenterPoint(nint brush, ref PointF point);

    [LibraryImport("gdiplus.dll")]
    public static partial int GdipSetPathGradientWrapMode(nint brush, WrapMode wrapMode);

    [LibraryImport("gdiplus.dll")]
    private static unsafe partial int GdipSetPathGradientPresetBlend(
        nint brush, uint* blend, float* positions, int count);

    public static unsafe int SetPathGradientPresetBlend(nint brush, uint[] colors, float[] positions)
    {
        fixed (uint* pc = colors)
        fixed (float* pp = positions)
            return GdipSetPathGradientPresetBlend(brush, pc, pp, colors.Length);
    }

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
        Default = 0,
        HighSpeed = 1,
        HighQuality = 2,
        None = 3,
        AntiAlias = 4,
    }

    public enum PixelOffsetMode
    {
        Default = 0,
        HighSpeed = 1,
        HighQuality = 2,
        None = 3,
        Half = 4,
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

    public enum PenAlignment
    {
        Center = 0,
        Inset = 1
    }

    public enum MatrixOrder
    {
        Prepend = 0,
        Append = 1
    }

    public enum WrapMode
    {
        Tile = 0,
        TileFlipX = 1,
        TileFlipY = 2,
        TileFlipXY = 3,
        Clamp = 4,
    }

    public enum GpLineCap
    {
        Flat = 0,
        Square = 1,
        Round = 2,
    }

    public enum GpDashCap
    {
        Flat = 0,
        Round = 2,
    }

    public enum GpLineJoin
    {
        Miter = 0,
        Bevel = 1,
        Round = 2,
    }

    public enum GpDashStyle
    {
        Solid = 0,
        Dash = 1,
        Dot = 2,
        DashDot = 3,
        DashDotDot = 4,
        Custom = 5,
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PointF
    {
        public float X;
        public float Y;
        public PointF(float x, float y) { X = x; Y = y; }
    }
}
