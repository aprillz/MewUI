using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal struct BLENDFUNCTION
{
    public byte BlendOp;
    public byte BlendFlags;
    public byte SourceConstantAlpha;
    public byte AlphaFormat;

    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    public static BLENDFUNCTION SourceOver(byte alpha) => new()
    {
        BlendOp = AC_SRC_OVER,
        BlendFlags = 0,
        SourceConstantAlpha = alpha,
        AlphaFormat = AC_SRC_ALPHA
    };
}

[StructLayout(LayoutKind.Sequential)]
internal struct TRIVERTEX
{
    public int x;
    public int y;
    public ushort Red;
    public ushort Green;
    public ushort Blue;
    public ushort Alpha;
}
