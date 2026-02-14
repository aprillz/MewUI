using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct PAINTSTRUCT
{
    public nint hdc;
    public int fErase;
    public RECT rcPaint;
    public int fRestore;
    public int fIncUpdate;
    public Byte32 rgbReserved;
}
