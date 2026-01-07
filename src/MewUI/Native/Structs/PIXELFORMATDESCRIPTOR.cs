using System.Runtime.InteropServices;

using Aprillz.MewUI.Native.Constants;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential)]
internal struct PIXELFORMATDESCRIPTOR
{
    public ushort nSize;
    public ushort nVersion;
    public uint dwFlags;
    public byte iPixelType;
    public byte cColorBits;
    public byte cRedBits;
    public byte cRedShift;
    public byte cGreenBits;
    public byte cGreenShift;
    public byte cBlueBits;
    public byte cBlueShift;
    public byte cAlphaBits;
    public byte cAlphaShift;
    public byte cAccumBits;
    public byte cAccumRedBits;
    public byte cAccumGreenBits;
    public byte cAccumBlueBits;
    public byte cAccumAlphaBits;
    public byte cDepthBits;
    public byte cStencilBits;
    public byte cAuxBuffers;
    public byte iLayerType;
    public byte bReserved;
    public uint dwLayerMask;
    public uint dwVisibleMask;
    public uint dwDamageMask;

    public static PIXELFORMATDESCRIPTOR CreateOpenGlDoubleBuffered()
    {
        var pfd = new PIXELFORMATDESCRIPTOR
        {
            nSize = (ushort)Marshal.SizeOf<PIXELFORMATDESCRIPTOR>(),
            nVersion = 1,
            dwFlags = PixelFormatDescriptorFlags.PFD_DRAW_TO_WINDOW |
                      PixelFormatDescriptorFlags.PFD_SUPPORT_OPENGL |
                      PixelFormatDescriptorFlags.PFD_DOUBLEBUFFER,
            iPixelType = PixelFormatDescriptorFlags.PFD_TYPE_RGBA,
            cColorBits = 32,
            cAlphaBits = 8,
            cDepthBits = 0,
            cStencilBits = 0,
            iLayerType = PixelFormatDescriptorFlags.PFD_MAIN_PLANE,
        };
        return pfd;
    }
}
