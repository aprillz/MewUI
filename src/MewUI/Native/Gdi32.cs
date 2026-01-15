using System.Runtime.InteropServices;

using Aprillz.MewUI.Native.Structs;

namespace Aprillz.MewUI.Native;

internal static partial class Gdi32
{
    private const string LibraryName = "gdi32.dll";

    #region Device Context

    [LibraryImport(LibraryName, EntryPoint = "CreateCompatibleDC")]
    public static partial nint CreateCompatibleDC(nint hdc);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteDC(nint hdc);

    [LibraryImport(LibraryName)]
    public static partial int SaveDC(nint hdc);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RestoreDC(nint hdc, int nSavedDC);

    #endregion

    #region Pixel Format / SwapBuffers (OpenGL)

    [LibraryImport(LibraryName)]
    public static partial int ChoosePixelFormat(nint hdc, ref PIXELFORMATDESCRIPTOR ppfd);

    [LibraryImport(LibraryName)]
    public static partial int DescribePixelFormat(nint hdc, int iPixelFormat, uint nBytes, ref PIXELFORMATDESCRIPTOR ppfd);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetPixelFormat(nint hdc, int iPixelFormat, ref PIXELFORMATDESCRIPTOR ppfd);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SwapBuffers(nint hdc);

    #endregion

    #region Object Management

    [LibraryImport(LibraryName)]
    public static partial nint SelectObject(nint hdc, nint hgdiobj);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint hObject);

    [LibraryImport(LibraryName)]
    public static partial nint GetStockObject(int fnObject);

    [LibraryImport(LibraryName)]
    public static partial nint GetCurrentObject(nint hdc, uint uObjectType);

    #endregion

    #region Brush

    [LibraryImport(LibraryName)]
    public static partial nint CreateSolidBrush(uint crColor);

    [LibraryImport(LibraryName)]
    public static partial nint CreateHatchBrush(int iHatch, uint color);

    [LibraryImport(LibraryName)]
    public static partial nint CreatePatternBrush(nint hbmp);

    #endregion

    #region Pen

    [LibraryImport(LibraryName)]
    public static partial nint CreatePen(int iStyle, int cWidth, uint color);

    [LibraryImport(LibraryName)]
    public static partial nint ExtCreatePen(uint iPenStyle, uint cWidth, ref LOGBRUSH plbrush, uint cStyle, nint pstyle);

    #endregion

    #region Font

    [LibraryImport(LibraryName, EntryPoint = "CreateFontW", StringMarshalling = StringMarshalling.Utf16)]
    public static partial nint CreateFont(
        int cHeight,
        int cWidth,
        int cEscapement,
        int cOrientation,
        int cWeight,
        uint bItalic,
        uint bUnderline,
        uint bStrikeOut,
        uint iCharSet,
        uint iOutPrecision,
        uint iClipPrecision,
        uint iQuality,
        uint iPitchAndFamily,
        string pszFaceName);

    [LibraryImport(LibraryName, EntryPoint = "CreateFontIndirectW")]
    public static partial nint CreateFontIndirect(ref LOGFONT lplf);

    [LibraryImport(LibraryName, EntryPoint = "GetTextMetricsW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTextMetrics(nint hdc, out TEXTMETRIC lptm);

    [LibraryImport(LibraryName, EntryPoint = "GetTextExtentPoint32W", StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetTextExtentPoint32(nint hdc, string lpString, int c, out SIZE lpSize);

    #endregion

    #region Drawing - Shapes

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Rectangle(nint hdc, int left, int top, int right, int bottom);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool RoundRect(nint hdc, int left, int top, int right, int bottom, int width, int height);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Ellipse(nint hdc, int left, int top, int right, int bottom);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Polygon(nint hdc, POINT[] apt, int cpt);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool Polyline(nint hdc, POINT[] apt, int cpt);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool MoveToEx(nint hdc, int x, int y, out POINT lpPoint);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool LineTo(nint hdc, int x, int y);

    #endregion

    #region Drawing - Fill

    [LibraryImport("user32.dll")]
    public static partial int FillRect(nint hDC, ref RECT lprc, nint hbr);

    [LibraryImport("user32.dll")]
    public static partial int FrameRect(nint hDC, ref RECT lprc, nint hbr);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvertRect(nint hDC, ref RECT lprc);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ExtFloodFill(nint hdc, int x, int y, uint color, uint type);

    #endregion

    #region Drawing - Text

    [LibraryImport(LibraryName, EntryPoint = "TextOutW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool TextOut(nint hdc, int x, int y, char* lpString, int c);

    [LibraryImport(LibraryName, EntryPoint = "ExtTextOutW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static unsafe partial bool ExtTextOut(nint hdc, int x, int y, uint options, ref RECT lprect, char* lpString, int c, nint lpDx);

    [LibraryImport("user32.dll", EntryPoint = "DrawTextW")]
    public static unsafe partial int DrawText(nint hdc, char* lpchText, int cchText, ref RECT lprc, uint format);

    [LibraryImport(LibraryName)]
    public static partial uint SetTextColor(nint hdc, uint color);

    [LibraryImport(LibraryName)]
    public static partial uint GetTextColor(nint hdc);

    [LibraryImport(LibraryName)]
    public static partial int SetBkMode(nint hdc, int mode);

    [LibraryImport(LibraryName)]
    public static partial int GetBkMode(nint hdc);

    [LibraryImport(LibraryName)]
    public static partial uint SetBkColor(nint hdc, uint color);

    [LibraryImport(LibraryName)]
    public static partial uint GetBkColor(nint hdc);

    [LibraryImport(LibraryName)]
    public static partial uint SetTextAlign(nint hdc, uint align);

    [LibraryImport(LibraryName)]
    public static partial uint GetTextAlign(nint hdc);

    #endregion

    #region Bitmap

    [LibraryImport(LibraryName)]
    public static partial nint CreateCompatibleBitmap(nint hdc, int cx, int cy);

    [LibraryImport(LibraryName)]
    public static partial nint CreateDIBSection(nint hdc, ref BITMAPINFO pbmi, uint usage, out nint ppvBits, nint hSection, uint offset);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool BitBlt(nint hdc, int x, int y, int cx, int cy, nint hdcSrc, int x1, int y1, uint rop);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool StretchBlt(nint hdcDest, int xDest, int yDest, int wDest, int hDest,
        nint hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);

    [LibraryImport(LibraryName)]
    public static partial int StretchDIBits(nint hdc, int xDest, int yDest, int DestWidth, int DestHeight,
        int xSrc, int ySrc, int SrcWidth, int SrcHeight, nint lpBits, ref BITMAPINFO lpbmi, uint iUsage, uint rop);

    [LibraryImport(LibraryName)]
    public static partial int SetStretchBltMode(nint hdc, int mode);

    [LibraryImport(LibraryName)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetBrushOrgEx(nint hdc, int x, int y, out Structs.POINT lppt);

    #endregion

    #region Clipping

    [LibraryImport(LibraryName)]
    public static partial nint CreateRectRgn(int x1, int y1, int x2, int y2);

    [LibraryImport(LibraryName)]
    public static partial nint CreateRoundRectRgn(int x1, int y1, int x2, int y2, int cx, int cy);

    [LibraryImport(LibraryName)]
    public static partial int SelectClipRgn(nint hdc, nint hrgn);

    [LibraryImport(LibraryName)]
    public static partial int IntersectClipRect(nint hdc, int left, int top, int right, int bottom);

    [LibraryImport(LibraryName)]
    public static partial int ExcludeClipRect(nint hdc, int left, int top, int right, int bottom);

    #endregion

    #region Pixel

    [LibraryImport(LibraryName)]
    public static partial uint SetPixel(nint hdc, int x, int y, uint color);

    [LibraryImport(LibraryName)]
    public static partial uint GetPixel(nint hdc, int x, int y);

    #endregion

    #region Alpha Blending

    [LibraryImport("msimg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AlphaBlend(nint hdcDest, int xoriginDest, int yoriginDest, int wDest, int hDest,
        nint hdcSrc, int xoriginSrc, int yoriginSrc, int wSrc, int hSrc, BLENDFUNCTION ftn);

    [LibraryImport("msimg32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GradientFill(nint hdc, TRIVERTEX[] pVertex, uint nVertex,
        nint pMesh, uint nMesh, uint ulMode);

    #endregion
}
