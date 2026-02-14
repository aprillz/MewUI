using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal struct WNDCLASSEX
{
    public uint cbSize;
    public uint style;
    public nint lpfnWndProc;
    public int cbClsExtra;
    public int cbWndExtra;
    public nint hInstance;
    public nint hIcon;
    public nint hCursor;
    public nint hbrBackground;
    public nint lpszMenuName;
    public nint lpszClassName;
    public nint hIconSm;

    public static WNDCLASSEX Create()
    {
        var wc = new WNDCLASSEX();
        wc.cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>();
        return wc;
    }
}

internal delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);
