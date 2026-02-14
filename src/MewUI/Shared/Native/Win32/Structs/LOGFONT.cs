using System.Runtime.InteropServices;

namespace Aprillz.MewUI.Native.Structs;

[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal unsafe struct LOGFONT
{
    public int lfHeight;
    public int lfWidth;
    public int lfEscapement;
    public int lfOrientation;
    public int lfWeight;
    public byte lfItalic;
    public byte lfUnderline;
    public byte lfStrikeOut;
    public byte lfCharSet;
    public byte lfOutPrecision;
    public byte lfClipPrecision;
    public byte lfQuality;
    public byte lfPitchAndFamily;
    public Char32 lfFaceName;

    public void SetFaceName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        int len = Math.Min(name.Length, 31);
        for (int i = 0; i < len; i++)
        {
            lfFaceName[i] = name[i];
        }

        lfFaceName[len] = '\0';

        for (int i = len + 1; i < 32; i++)
        {
            lfFaceName[i] = '\0';
        }
    }
}
