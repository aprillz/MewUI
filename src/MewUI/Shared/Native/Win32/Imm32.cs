using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

internal static partial class Imm32
{
    internal static class CompositionStringFlags
    {
        public const int GCS_COMPSTR = 0x0008;
        public const int GCS_RESULTSTR = 0x0800;
    }

    [LibraryImport("imm32.dll")]
    public static partial nint ImmGetContext(nint hWnd);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmReleaseContext(nint hWnd, nint hIMC);

    [LibraryImport("imm32.dll")]
    public static partial int ImmGetCompositionStringW(nint hIMC, int dwIndex, nint lpBuf, int dwBufLen);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmSetCompositionWindow(nint hIMC, ref COMPOSITIONFORM lpCompForm);

    public const int CFS_POINT = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    public struct COMPOSITIONFORM
    {
        public int dwStyle;
        public POINT ptCurrentPos;
        public RECT rcArea;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int left;
        public int top;
        public int right;
        public int bottom;
    }
}
