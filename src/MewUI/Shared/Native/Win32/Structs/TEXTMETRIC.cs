using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct TEXTMETRIC
{
    public int tmHeight;
    public int tmAscent;
    public int tmDescent;
    public int tmInternalLeading;
    public int tmExternalLeading;
    public int tmAveCharWidth;
    public int tmMaxCharWidth;
    public int tmWeight;
    public int tmOverhang;
    public int tmDigitizedAspectX;
    public int tmDigitizedAspectY;
    public char tmFirstChar;
    public char tmLastChar;
    public char tmDefaultChar;
    public char tmBreakChar;
    public byte tmItalic;
    public byte tmUnderlined;
    public byte tmStruckOut;
    public byte tmPitchAndFamily;
    public byte tmCharSet;
}
