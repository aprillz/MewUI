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

    [LibraryImport("imm32.dll")]
    public static partial nint ImmAssociateContext(nint hWnd, nint hIMC);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmSetOpenStatus(nint hIMC, [MarshalAs(UnmanagedType.Bool)] bool fOpen);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmGetOpenStatus(nint hIMC);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmSetConversionStatus(nint hIMC, uint fdwConversion, uint fdwSentence);

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmGetConversionStatus(nint hIMC, out uint fdwConversion, out uint fdwSentence);

    public const uint IME_CMODE_ALPHANUMERIC = 0x0000;

    public const int CFS_POINT = 0x0002;
    public const int CFS_FORCE_POSITION = 0x0020;
    public const int CFS_CANDIDATEPOS = 0x0040;
    public const int CFS_EXCLUDE = 0x0080;

    [LibraryImport("imm32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ImmSetCandidateWindow(nint hIMC, ref CANDIDATEFORM lpCandidate);


    [StructLayout(LayoutKind.Sequential)]
    public struct CANDIDATEFORM
    {
        public int dwIndex;
        public int dwStyle;
        public POINT ptCurrentPos;
        public RECT rcArea;
    }

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
