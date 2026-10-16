using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal struct POINTER_INFO
{
    public const uint PT_POINTER = 1;
    public const uint PT_TOUCH = 2;
    public const uint PT_PEN = 3;
    public const uint PT_MOUSE = 4;
    public const uint PT_TOUCHPAD = 5;

    public const uint POINTER_FLAG_INCONTACT = 0x00000004;
    public const uint POINTER_FLAG_CANCELED = 0x00008000;

    public uint pointerType;
    public uint pointerId;
    public uint frameId;
    public uint pointerFlags;
    public nint sourceDevice;
    public nint hwndTarget;
    public POINT ptPixelLocation;
    public POINT ptHimetricLocation;
    public POINT ptPixelLocationRaw;
    public POINT ptHimetricLocationRaw;
    public uint dwTime;
    public uint historyCount;
    public int inputData;
    public uint dwKeyStates;
    public ulong performanceCount;
    public int buttonChangeType;
}
