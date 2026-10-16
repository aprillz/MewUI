using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native;

// Linux fbdev ioctls (linux/fb.h).
internal static class Fbdev
{
    public const nuint FBIOGET_VSCREENINFO = 0x4600;
    public const nuint FBIOPUT_VSCREENINFO = 0x4601;
    public const nuint FBIOGET_FSCREENINFO = 0x4602;
    public const nuint FBIO_WAITFORVSYNC = 0x40044620;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FbBitfield
{
    public uint Offset;
    public uint Length;
    public uint MsbRight;
}

[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FbVarScreenInfo
{
    public uint XRes;
    public uint YRes;
    public uint XResVirtual;
    public uint YResVirtual;
    public uint XOffset;
    public uint YOffset;
    public uint BitsPerPixel;
    public uint Grayscale;
    public FbBitfield Red;
    public FbBitfield Green;
    public FbBitfield Blue;
    public FbBitfield Transp;
    public uint NonStd;
    public uint Activate;
    public uint Height;
    public uint Width;
    public uint AccelFlags;
    public uint PixClock;
    public uint LeftMargin;
    public uint RightMargin;
    public uint UpperMargin;
    public uint LowerMargin;
    public uint HSyncLen;
    public uint VSyncLen;
    public uint Sync;
    public uint VMode;
    public uint Rotate;
    public uint ColorSpace;
    public fixed uint Reserved[4];
}

// LP64 layout of struct fb_fix_screeninfo (unsigned long is 8 bytes).
[StructLayout(LayoutKind.Explicit, Size = 80)]
internal unsafe struct FbFixScreenInfo
{
    [FieldOffset(0)] public fixed byte Id[16];
    [FieldOffset(16)] public ulong SmemStart;
    [FieldOffset(24)] public uint SmemLen;
    [FieldOffset(28)] public uint Type;
    [FieldOffset(32)] public uint TypeAux;
    [FieldOffset(36)] public uint Visual;
    [FieldOffset(40)] public ushort XPanStep;
    [FieldOffset(42)] public ushort YPanStep;
    [FieldOffset(44)] public ushort YWrapStep;
    [FieldOffset(48)] public uint LineLength;
    [FieldOffset(56)] public ulong MmioStart;
    [FieldOffset(64)] public uint MmioLen;
    [FieldOffset(68)] public uint Accel;
    [FieldOffset(72)] public ushort Capabilities;
}
